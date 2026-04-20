using System;
using System.Collections.Generic;

namespace _403DesktopApp.Models
{
    // ── Auth ──────────────────────────────────────────────────────────────────

    public class ApiLoginRequest
    {
        public string ProviderId { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class ApiAuthResponse
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public int ExpiresIn { get; set; }
        public ApiUserInfo User { get; set; } = new();
    }

    public class ApiUserInfo
    {
        public Guid Id { get; set; }
        public string ProviderId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
    }

    // ── Patient ───────────────────────────────────────────────────────────────

    public class ApiPatient
    {
        public Guid Id { get; set; }
        public string PatientId { get; set; } = "";   // external MRN
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public DateTime DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }
        public string? InsuranceProvider { get; set; }
        public string? InsuranceNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsActive { get; set; }
    }

    public class ApiCreatePatientRequest
    {
        public string PatientId { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public DateTime DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }
        public string? InsuranceProvider { get; set; }
        public string? InsuranceNumber { get; set; }
    }

    public class ApiUpdatePatientRequest
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public DateTime DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }
        public string? InsuranceProvider { get; set; }
        public string? InsuranceNumber { get; set; }
    }

    // ── Patient Files ─────────────────────────────────────────────────────────

    public class ApiPatientFile
    {
        public Guid Id { get; set; }
        public Guid PatientId { get; set; }
        public string FileName { get; set; } = "";
        public string FileType { get; set; } = "";
        public string? Category { get; set; }
        public string? Description { get; set; }
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; }
        public string UploadedBy { get; set; } = "";
    }

    // ── Refresh Token ─────────────────────────────────────────────────────────

    public class ApiRefreshRequest
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }
}
