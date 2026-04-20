using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MedicalImagingAPI.Database.Entities
{
    public class DicomImage
    {
        public Guid Id { get; set; }
        public string PatientId { get; set; } // Which patient...
        public string StudyId { get; set; } // Which study...
        public string SeriesId { get; set; }
        public string InstanceId { get; set; }
        public string FilePath { get; set; } // Relative path or blob name
        public string FileName { get; set; }
        public long FileSizeBytes { get; set; }
        public string FileHash { get; set; } // SHA-256 for integrity verification
        public DateTime UploadedAt { get; set; }
        public Guid UploadedBy { get; set; }
        public int FrameCount { get; set; }
        public string Modality { get; set; } // CT, MR, US, XR, etc.
        public DateTime? StudyDate { get; set; }
        public string StudyDescription { get; set; }
        public string SeriesDescription { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        // Navigation properties
        public User UploadedByUser { get; set; }
        public List<Annotation> Annotations { get; set; }
    }
}