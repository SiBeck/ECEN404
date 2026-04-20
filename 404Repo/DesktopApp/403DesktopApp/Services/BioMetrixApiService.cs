using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using _403DesktopApp.Models;

namespace _403DesktopApp.Services
{
    /// <summary>
    /// HTTP client wrapper for the BioMetrixDatabase REST API.
    /// Maintains the JWT access token and handles transparent token refresh.
    /// </summary>
    public class BioMetrixApiService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _http;
        private string? _accessToken;
        private string? _refreshToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public bool IsAuthenticated => _accessToken != null && DateTime.UtcNow < _tokenExpiry;
        public ApiUserInfo? CurrentUser { get; private set; }

        // Base URL is configurable so both local and deployed environments work.
        // Default targets a locally-running BioMetrixDatabase instance.
        public string BaseUrl { get; set; } = "https://localhost:5001";

        public BioMetrixApiService()
        {
            var handler = new HttpClientHandler
            {
                // Allow self-signed certs in development; remove for production
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            _http = new HttpClient(handler);
        }

        // ── Authentication ────────────────────────────────────────────────────

        /// <summary>
        /// Authenticates a provider against the BioMetrixDatabase API.
        /// Returns the auth response on success, null on invalid credentials.
        /// Throws on network errors so callers can distinguish between
        /// "wrong password" (null) and "server unreachable" (exception).
        /// </summary>
        public async Task<ApiAuthResponse?> LoginAsync(string providerId, string password)
        {
            var request = new ApiLoginRequest { ProviderId = providerId, Password = password };
            using var response = await _http.PostAsJsonAsync($"{BaseUrl}/api/auth/login", request);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return null;

            response.EnsureSuccessStatusCode();

            var auth = await response.Content.ReadFromJsonAsync<ApiAuthResponse>(_jsonOptions);
            if (auth == null) return null;

            StoreTokens(auth);
            return auth;
        }

        /// <summary>
        /// Silently refreshes the access token using the stored refresh token.
        /// Returns false if the refresh token is also expired.
        /// </summary>
        public async Task<bool> RefreshAsync()
        {
            if (_refreshToken == null) return false;

            var request = new ApiRefreshRequest
            {
                AccessToken = _accessToken ?? "",
                RefreshToken = _refreshToken
            };

            using var response = await _http.PostAsJsonAsync($"{BaseUrl}/api/auth/refresh", request);
            if (!response.IsSuccessStatusCode) return false;

            var auth = await response.Content.ReadFromJsonAsync<ApiAuthResponse>(_jsonOptions);
            if (auth == null) return false;

            StoreTokens(auth);
            return true;
        }

        public void Logout()
        {
            _accessToken = null;
            _refreshToken = null;
            _tokenExpiry = DateTime.MinValue;
            CurrentUser = null;
        }

        // ── Patients ──────────────────────────────────────────────────────────

        public async Task<List<ApiPatient>> GetAllPatientsAsync()
        {
            await EnsureTokenAsync();
            using var response = await SendAsync(HttpMethod.Get, "/api/patients");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<ApiPatient>>(_jsonOptions) ?? new();
        }

        public async Task<ApiPatient?> GetPatientAsync(Guid id)
        {
            await EnsureTokenAsync();
            using var response = await SendAsync(HttpMethod.Get, $"/api/patients/{id}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ApiPatient>(_jsonOptions);
        }

        public async Task<List<ApiPatient>> SearchPatientsAsync(string query)
        {
            await EnsureTokenAsync();
            using var response = await SendAsync(HttpMethod.Get,
                $"/api/patients/search?q={Uri.EscapeDataString(query)}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<ApiPatient>>(_jsonOptions) ?? new();
        }

        public async Task<ApiPatient?> CreatePatientAsync(ApiCreatePatientRequest request)
        {
            await EnsureTokenAsync();
            using var response = await SendAsJsonAsync(HttpMethod.Post, "/api/patients", request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ApiPatient>(_jsonOptions);
        }

        public async Task<ApiPatient?> UpdatePatientAsync(Guid id, ApiUpdatePatientRequest request)
        {
            await EnsureTokenAsync();
            using var response = await SendAsJsonAsync(HttpMethod.Put, $"/api/patients/{id}", request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ApiPatient>(_jsonOptions);
        }

        public async Task<bool> DeletePatientAsync(Guid id)
        {
            await EnsureTokenAsync();
            using var response = await SendAsync(HttpMethod.Delete, $"/api/patients/{id}");
            return response.IsSuccessStatusCode;
        }

        // ── Patient Files ─────────────────────────────────────────────────────

        public async Task<List<ApiPatientFile>> GetPatientFilesAsync(Guid patientId)
        {
            await EnsureTokenAsync();
            using var response = await SendAsync(HttpMethod.Get, $"/api/patientfiles/{patientId}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<ApiPatientFile>>(_jsonOptions) ?? new();
        }

        /// <summary>
        /// Uploads a file from disk to the BioMetrixDatabase for the given patient.
        /// </summary>
        public async Task<ApiPatientFile?> UploadFileAsync(
            Guid patientId,
            string localFilePath,
            string category = "DICOM",
            string description = "")
        {
            await EnsureTokenAsync();

            await using var fileStream = File.OpenRead(localFilePath);
            string fileName = Path.GetFileName(localFilePath);
            string fileType = Path.GetExtension(localFilePath).TrimStart('.').ToUpperInvariant();

            using var content = new MultipartFormDataContent();
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", fileName);
            content.Add(new StringContent(category), "category");
            content.Add(new StringContent(description), "description");

            var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/patientfiles/{patientId}/upload")
            {
                Content = content
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            using var response = await _http.SendAsync(req);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ApiPatientFile>(_jsonOptions);
        }

        /// <summary>
        /// Downloads a file from BioMetrixDatabase and writes it to <paramref name="destinationPath"/>.
        /// </summary>
        public async Task DownloadFileAsync(Guid fileId, string destinationPath)
        {
            await EnsureTokenAsync();
            using var response = await SendAsync(HttpMethod.Get, $"/api/patientfiles/{fileId}/download");
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(destinationPath);
            await stream.CopyToAsync(file);
        }

        public async Task<bool> DeleteFileAsync(Guid fileId)
        {
            await EnsureTokenAsync();
            using var response = await SendAsync(HttpMethod.Delete, $"/api/patientfiles/{fileId}");
            return response.IsSuccessStatusCode;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void StoreTokens(ApiAuthResponse auth)
        {
            _accessToken = auth.AccessToken;
            _refreshToken = auth.RefreshToken;
            // Subtract 30 s so we refresh before expiry
            _tokenExpiry = DateTime.UtcNow.AddSeconds(auth.ExpiresIn - 30);
            CurrentUser = auth.User;
        }

        private async Task EnsureTokenAsync()
        {
            if (_accessToken == null)
                throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");

            if (DateTime.UtcNow >= _tokenExpiry)
            {
                bool ok = await RefreshAsync();
                if (!ok)
                    throw new InvalidOperationException("Session expired. Please log in again.");
            }
        }

        private Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl)
        {
            var req = new HttpRequestMessage(method, $"{BaseUrl}{relativeUrl}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            return _http.SendAsync(req);
        }

        private Task<HttpResponseMessage> SendAsJsonAsync<T>(HttpMethod method, string relativeUrl, T body)
        {
            var req = new HttpRequestMessage(method, $"{BaseUrl}{relativeUrl}")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            return _http.SendAsync(req);
        }
    }
}
