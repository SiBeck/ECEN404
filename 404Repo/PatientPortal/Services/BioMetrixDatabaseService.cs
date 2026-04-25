using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PatientPortal.Models;

namespace PatientPortal.Services
{
    /// <summary>
    /// Server-to-server bridge from PatientPortal to the BioMetrixDatabase REST API.
    ///
    /// A privileged service-account JWT is obtained once at startup (and silently
    /// refreshed) so every call is already authenticated. Callers only need to
    /// supply the patient MRN and BioMetrixDatabase handles authorization on its side.
    ///
    /// MRN mapping: PatientPortal stores patients with an int primary key that is
    /// used directly as the MRN string in BioMetrixDatabase (e.g. PatientPortal
    /// PatientId 42 → BioMetrixDatabase PatientId "42").
    /// </summary>
    public class BioMetrixDatabaseService : IBioMetrixDatabaseService
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly HttpClient _http;
        private readonly ILogger<BioMetrixDatabaseService> _logger;
        private readonly string _baseUrl;
        private readonly string _serviceAccountId;
        private readonly string _serviceAccountPassword;

        // Cached credentials — refreshed automatically when the token expires
        private string? _accessToken;
        private string? _refreshToken;
        private DateTime _tokenExpiry = DateTime.MinValue;
        private readonly SemaphoreSlim _tokenLock = new(1, 1);

        public BioMetrixDatabaseService(
            HttpClient http,
            IConfiguration configuration,
            ILogger<BioMetrixDatabaseService> logger)
        {
            _http = http;
            _logger = logger;

            var section = configuration.GetSection("BioMetrixDatabase");
            _baseUrl = section["BaseUrl"] ?? "https://localhost:5001";
            _serviceAccountId = section["ServiceAccountId"] ?? "PORTAL_SVC";
            _serviceAccountPassword = section["ServiceAccountPassword"] ?? "";
        }

        // ── IBioMetrixDatabaseService ─────────────────────────────────────────

        public async Task<List<PatientFile>> GetPatientFilesAsync(string mrn)
        {
            var patientGuid = await ResolvePatientGuidAsync(mrn);
            if (patientGuid == null) return new List<PatientFile>();

            await EnsureTokenAsync();
            using var response = await GetAsync($"/api/patients/{patientGuid}/files");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GetPatientFiles failed: {Status} for MRN {Mrn}", response.StatusCode, mrn);
                return new List<PatientFile>();
            }

            var apiFiles = await response.Content.ReadFromJsonAsync<List<ApiFileDto>>(JsonOpts)
                           ?? new List<ApiFileDto>();

            return apiFiles.Select(f => MapToPortalFile(f, mrn)).ToList();
        }

        public async Task<PatientFile?> GetFileMetadataAsync(Guid fileId, string mrn)
        {
            var patientGuid = await ResolvePatientGuidAsync(mrn);
            if (patientGuid == null) return null;

            await EnsureTokenAsync();
            // Retrieve metadata via the listing endpoint and find by ID.
            // BioMetrixDatabase's per-file GET returns a binary stream, not metadata.
            using var response = await GetAsync($"/api/patients/{patientGuid}/files");
            if (!response.IsSuccessStatusCode) return null;

            var apiFiles = await response.Content.ReadFromJsonAsync<List<ApiFileDto>>(JsonOpts)
                           ?? new List<ApiFileDto>();

            var match = apiFiles.FirstOrDefault(f => f.Id == fileId);
            return match == null ? null : MapToPortalFile(match, mrn);
        }

        public async Task<Stream?> DownloadFileAsync(Guid fileId)
        {
            // We need the patient GUID to build the path, but we only have the file GUID.
            // Strategy: locate the file in the DB via search or use the patientId embedded
            // in the FilePath ("biometrix://{patientGuid}/{fileId}").
            // Callers always pass a PatientFile whose FilePath was set by this service,
            // so we parse it.
            _logger.LogInformation("DownloadFile {FileId}", fileId);
            await EnsureTokenAsync();

            // Try all accessible patients to find the file — acceptable for service accounts.
            // A production optimisation would cache the patientGuid alongside fileId.
            var patientsResponse = await GetAsync("/api/patients");
            if (!patientsResponse.IsSuccessStatusCode) return null;

            var patients = await patientsResponse.Content.ReadFromJsonAsync<List<ApiPatientDto>>(JsonOpts)
                           ?? new List<ApiPatientDto>();

            foreach (var patient in patients)
            {
                using var fileResponse = await GetAsync(
                    $"/api/patients/{patient.Id}/files/{fileId}");

                if (fileResponse.IsSuccessStatusCode)
                    return await fileResponse.Content.ReadAsStreamAsync();
            }

            return null;
        }

        /// <summary>
        /// Efficient overload: caller supplies the patient GUID parsed from FilePath.
        /// </summary>
        public async Task<Stream?> DownloadFileAsync(Guid patientGuid, Guid fileId)
        {
            await EnsureTokenAsync();
            using var response = await GetAsync($"/api/patients/{patientGuid}/files/{fileId}");
            if (!response.IsSuccessStatusCode) return null;
            // Copy to MemoryStream so the HttpResponseMessage can be disposed
            var ms = new MemoryStream();
            await response.Content.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }

        public async Task<PatientFile?> UploadFileAsync(
            string mrn,
            Stream fileStream,
            string fileName,
            string fileType,
            string category,
            string description)
        {
            var patientGuid = await ResolvePatientGuidAsync(mrn);
            if (patientGuid == null)
            {
                _logger.LogWarning("UploadFile: patient with MRN {Mrn} not found in BioMetrixDatabase", mrn);
                return null;
            }

            await EnsureTokenAsync();

            using var content = new MultipartFormDataContent();
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", fileName);
            content.Add(new StringContent(fileType), "fileType");
            content.Add(new StringContent(category), "category");
            content.Add(new StringContent(description ?? ""), "description");

            var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_baseUrl}/api/patients/{patientGuid}/files")
            {
                Content = content
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            using var response = await _http.SendAsync(req);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("UploadFile failed: {Status}", response.StatusCode);
                return null;
            }

            var apiFile = await response.Content.ReadFromJsonAsync<ApiFileDto>(JsonOpts);
            return apiFile == null ? null : MapToPortalFile(apiFile, mrn);
        }

        public async Task<bool> DeleteFileAsync(Guid fileId)
        {
            // Find the patient that owns this file so we can build the correct URL
            await EnsureTokenAsync();
            var patientsResponse = await GetAsync("/api/patients");
            if (!patientsResponse.IsSuccessStatusCode) return false;

            var patients = await patientsResponse.Content.ReadFromJsonAsync<List<ApiPatientDto>>(JsonOpts)
                           ?? new List<ApiPatientDto>();

            foreach (var patient in patients)
            {
                var req = new HttpRequestMessage(HttpMethod.Delete,
                    $"{_baseUrl}/api/patients/{patient.Id}/files/{fileId}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                using var response = await _http.SendAsync(req);
                if (response.IsSuccessStatusCode) return true;
                if (response.StatusCode != System.Net.HttpStatusCode.NotFound) break;
            }

            return false;
        }

        // ── Patient MRN → GUID resolution ────────────────────────────────────

        private readonly Dictionary<string, Guid> _mrnCache = new();

        private async Task<Guid?> ResolvePatientGuidAsync(string mrn)
        {
            if (_mrnCache.TryGetValue(mrn, out var cached)) return cached;

            await EnsureTokenAsync();
            using var response = await GetAsync(
                $"/api/patients/search?q={Uri.EscapeDataString(mrn)}");

            if (!response.IsSuccessStatusCode) return null;

            var patients = await response.Content.ReadFromJsonAsync<List<ApiPatientDto>>(JsonOpts)
                           ?? new List<ApiPatientDto>();

            // Find the exact MRN match (search may return partial matches)
            var match = patients.FirstOrDefault(p =>
                string.Equals(p.PatientId, mrn, StringComparison.OrdinalIgnoreCase));

            if (match == null) return null;

            _mrnCache[mrn] = match.Id;
            return match.Id;
        }

        // ── Token management ──────────────────────────────────────────────────

        private async Task EnsureTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiry) return;

            await _tokenLock.WaitAsync();
            try
            {
                // Re-check after acquiring lock
                if (_accessToken != null && DateTime.UtcNow < _tokenExpiry) return;

                if (_refreshToken != null)
                {
                    bool refreshed = await TryRefreshAsync();
                    if (refreshed) return;
                }

                await LoginAsync();
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private async Task LoginAsync()
        {
            var body = JsonSerializer.Serialize(new
            {
                providerId = _serviceAccountId,
                password = _serviceAccountPassword
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/auth/login")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            using var response = await _http.SendAsync(req);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"BioMetrixDatabase service-account login failed: {response.StatusCode}");

            var auth = await response.Content.ReadFromJsonAsync<ApiAuthResponseDto>(JsonOpts)
                       ?? throw new InvalidOperationException("Empty login response");

            StoreTokens(auth);
            _logger.LogInformation("BioMetrixDatabase service account authenticated successfully");
        }

        private async Task<bool> TryRefreshAsync()
        {
            var body = JsonSerializer.Serialize(new
            {
                accessToken = _accessToken,
                refreshToken = _refreshToken
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/auth/refresh")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            using var response = await _http.SendAsync(req);
            if (!response.IsSuccessStatusCode) return false;

            var auth = await response.Content.ReadFromJsonAsync<ApiAuthResponseDto>(JsonOpts);
            if (auth == null) return false;

            StoreTokens(auth);
            return true;
        }

        private void StoreTokens(ApiAuthResponseDto auth)
        {
            _accessToken = auth.AccessToken;
            _refreshToken = auth.RefreshToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(auth.ExpiresIn - 30);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Task<HttpResponseMessage> GetAsync(string relativeUrl)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{relativeUrl}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            return _http.SendAsync(req);
        }

        private static PatientFile MapToPortalFile(ApiFileDto f, string mrn)
        {
            // FilePath encodes both the patient GUID and file GUID so that
            // DownloadFileAsync can be routed efficiently.
            return new PatientFile
            {
                Id = f.Id,
                PatientId = int.TryParse(mrn, out int pid) ? pid : 0,
                FileName = f.FileName,
                FilePath = $"biometrix://{f.PatientId}/{f.Id}",
                FileType = f.FileType ?? "",
                Category = f.Category,
                Description = f.Description,
                FileSize = f.FileSize,
                UploadedAt = f.UploadedAt,
                UploadedBy = f.UploadedBy
            };
        }

        // ── Private DTOs matching BioMetrixDatabase API responses ─────────────

        private class ApiAuthResponseDto
        {
            [JsonPropertyName("accessToken")]
            public string AccessToken { get; set; } = "";

            [JsonPropertyName("refreshToken")]
            public string RefreshToken { get; set; } = "";

            [JsonPropertyName("expiresIn")]
            public int ExpiresIn { get; set; }
        }

        private class ApiPatientDto
        {
            [JsonPropertyName("id")]
            public Guid Id { get; set; }

            [JsonPropertyName("patientId")]
            public string PatientId { get; set; } = "";
        }

        private class ApiFileDto
        {
            [JsonPropertyName("id")]
            public Guid Id { get; set; }

            [JsonPropertyName("patientId")]
            public Guid PatientId { get; set; }

            [JsonPropertyName("fileName")]
            public string FileName { get; set; } = "";

            [JsonPropertyName("fileType")]
            public string? FileType { get; set; }

            [JsonPropertyName("category")]
            public string? Category { get; set; }

            [JsonPropertyName("description")]
            public string? Description { get; set; }

            [JsonPropertyName("fileSize")]
            public long FileSize { get; set; }

            [JsonPropertyName("uploadedAt")]
            public DateTime UploadedAt { get; set; }

            [JsonPropertyName("uploadedBy")]
            public string UploadedBy { get; set; } = "";
        }
    }
}
