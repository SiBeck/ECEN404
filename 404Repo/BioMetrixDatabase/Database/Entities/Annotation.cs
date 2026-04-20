namespace MedicalImagingAPI.Database.Entities
{
    public class Annotation
    {
        public Guid Id { get; set; }
        public Guid DicomImageId { get; set; }
        public Guid CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public AnnotationType Type { get; set; }
        public string Data { get; set; } // JSON: coordinates, text, etc.
        public string Color { get; set; }
        public int FrameIndex { get; set; } // For multi-frame images

        // Navigation properties
        public DicomImage DicomImage { get; set; }
        public User Creator { get; set; }
    }

    public enum AnnotationType
    {
        Arrow = 1,
        Rectangle = 2,
        Ellipse = 3,
        Line = 4,
        Text = 5,
        Measurement = 6
    }
}