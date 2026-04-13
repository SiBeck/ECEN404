using System.Text.Json.Serialization;

namespace _403DesktopApp.Models
{
    public class PatientProfile
    {
        public string PatientId { get; set; } = Guid.NewGuid().ToString();
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public DateTime DateOfBirth { get; set; } = DateTime.Today;
        public string Gender { get; set; } = "";
        public string MedicalRecordNumber { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Address { get; set; } = "";
        public string EmergencyContactName { get; set; } = "";
        public string EmergencyContactPhone { get; set; } = "";
        public string MedicalNotes { get; set; } = "";
        public List<PatientImage> AssociatedImages { get; set; } = new();
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public string CreatedByProviderId { get; set; } = "";
        public DateTime LastModifiedDate { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public string FullName => $"{FirstName} {LastName}".Trim();

        [JsonIgnore]
        public string DisplayInfo =>
            string.IsNullOrEmpty(MedicalRecordNumber)
                ? FullName
                : $"{FullName} (MRN: {MedicalRecordNumber})";
    }
}
