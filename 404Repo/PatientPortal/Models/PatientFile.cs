using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PatientPortal.Models
{
    [Table("PatientFiles")]
    public class PatientFile
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public int PatientId { get; set; }

        [ForeignKey(nameof(PatientId))]
        public Patient? Patient { get; set; }

        [Required]
        public string FileName { get; set; } = string.Empty;

        [Required]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        public string FileType { get; set; } = string.Empty;

        public string? Category { get; set; }
        public string? Description { get; set; }
        public long FileSize { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public string? UploadedBy { get; set; }

        [NotMapped]
        public string FileSizeFormatted
        {
            get
            {
                string[] sizes = { "B", "KB", "MB", "GB" };
                double len = FileSize;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }

        [NotMapped]
        public string FileExtension => Path.GetExtension(FileName).ToLowerInvariant();

        [NotMapped]
        public bool IsImage => new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" }.Contains(FileExtension);

        [NotMapped]
        public bool IsDicom => new[] { ".dcm", ".dicom" }.Contains(FileExtension);

        [NotMapped]
        public bool IsPdf => FileExtension == ".pdf";
    }
}