using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalImagingAPI.Database.Entities
{
    [Table("PatientFiles")]
    public class PatientFile
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid PatientId { get; set; }

        [ForeignKey(nameof(PatientId))]
        public Patient? Patient { get; set; }

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string FileType { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Category { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        public long FileSize { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(50)]
        public string? UploadedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}