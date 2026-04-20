namespace MedicalImagingAPI.Database.Entities
{
    public class AuditLog
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Action { get; set; } // "VIEW_IMAGE", "UPLOAD_IMAGE", "DELETE_IMAGE", etc.
        public string ResourceType { get; set; } // "DicomImage", "Patient", "User"
        public string ResourceId { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime Timestamp { get; set; }
        public string? AdditionalData { get; set; } // JSON for extra context
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
    }
}