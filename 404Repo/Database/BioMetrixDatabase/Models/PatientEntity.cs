using System.ComponentModel.DataAnnotations;

namespace BioMetrixDatabase.Models
{
    public class PatientEntity
    {
        [Key]
        public string PatientId { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public DateTime DateOfBirth { get; set; }
        public string Gender { get; set; } = "";
        public string MedicalRecordNumber { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Address { get; set; } = "";
        public string EmergencyContactName { get; set; } = "";
        public string EmergencyContactPhone { get; set; } = "";
        public string MedicalNotes { get; set; } = "";
        public DateTime CreatedDate { get; set; }
        public string CreatedByProviderId { get; set; } = "";
        public DateTime LastModifiedDate { get; set; }

        public List<PatientImageEntity> Images { get; set; } = new();
    }
}
