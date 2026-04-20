namespace MedicalImagingAPI.Database.Entities
{
    public class Patient
    {
        public Guid Id { get; set; }
        public string PatientId { get; set; } // External patient ID (like MRN)
        public string FirstName { get; set; }
        public string LastName { get; set; }
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
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; }

        // Navigation properties
        public List<DicomImage>? DicomImages { get; set; }
        public List<PatientFile>? PatientFiles { get; set; }
    }
}