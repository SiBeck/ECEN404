using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PatientPortal.Models
{
    [Table("Patients")]
    public class Patient
    {
        [Key]
        [Column("PatientId")]
        public int PatientId { get; set; }

        [Required]
        [MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        public DateTime DateOfBirth { get; set; }

        public string? Gender { get; set; }
        public string? Email { get; set; }

        // Match the actual database column name for phone
        [Column("Phone")]
        public string? Phone { get; set; }

        public string? Address { get; set; }

        [NotMapped]
        public string FullName => $"{FirstName} {LastName}";

        [NotMapped]
        public int Age
        {
            get
            {
                var today = DateTime.Today;
                var age = today.Year - DateOfBirth.Year;
                if (DateOfBirth.Date > today.AddYears(-age)) age--;
                return age;
            }
        }

        // For portal authentication (will be added to database)
        public string? PasswordHash { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public ICollection<PatientFile>? PatientFiles { get; set; }
    }
}