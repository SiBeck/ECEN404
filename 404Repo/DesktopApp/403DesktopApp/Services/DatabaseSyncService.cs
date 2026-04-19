using System.Net.Http;
using System.Text;
using System.Text.Json;
using _403DesktopApp.Models;

namespace _403DesktopApp.Services
{
    /// <summary>
    /// Mirrors patient records to the BioMetrix SQLite web API.
    /// The local AES-256 encrypted files are the primary store; this service
    /// syncs asynchronously and swallows errors so a missing API never blocks the UI.
    /// </summary>
    public class DatabaseSyncService
    {
        // Base URL of the BioMetrixDatabase ASP.NET Core API (launchSettings port)
        private const string ApiBase = "http://localhost:5168/api";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Creates or updates a patient record in the SQLite database.
        /// Serialises the desktop PatientProfile into the API's expected DTO shape.
        /// </summary>
        public async Task UpsertPatientAsync(PatientProfile profile)
        {
            try
            {
                var dto = BuildDto(profile);
                string json = JsonSerializer.Serialize(dto, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _http.PutAsync($"{ApiBase}/patients/{profile.PatientId}", content);
            }
            catch
            {
                // API not running is non-fatal; local .enc file is the source of truth
            }
        }

        /// <summary>Removes a patient from the SQLite database.</summary>
        public async Task DeletePatientAsync(string patientId)
        {
            try
            {
                await _http.DeleteAsync($"{ApiBase}/patients/{patientId}");
            }
            catch { }
        }

        // Build a plain object whose property names match the API's PatientEntity DTO.
        private static object BuildDto(PatientProfile p) => new
        {
            patientId = p.PatientId,
            firstName = p.FirstName,
            lastName = p.LastName,
            dateOfBirth = p.DateOfBirth,
            gender = p.Gender,
            medicalRecordNumber = p.MedicalRecordNumber,
            phoneNumber = p.PhoneNumber,
            email = p.Email,
            address = p.Address,
            emergencyContactName = p.EmergencyContactName,
            emergencyContactPhone = p.EmergencyContactPhone,
            medicalNotes = p.MedicalNotes,
            createdDate = p.CreatedDate,
            createdByProviderId = p.CreatedByProviderId,
            lastModifiedDate = p.LastModifiedDate,
            images = p.AssociatedImages.Select(img => new
            {
                imageId = img.ImageId,
                patientId = p.PatientId,
                fileName = img.FileName,
                tag = img.Tag,
                description = img.Description,
                sourceType = img.SourceType,
                savedDate = img.SavedDate,
                savedByProviderId = img.SavedByProviderId,
                filterStatsJson = img.FilterStats == null
                    ? null
                    : JsonSerializer.Serialize(img.FilterStats)
            })
        };
    }
}
