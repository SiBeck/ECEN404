using System.Text.Json.Serialization;

namespace _403DesktopApp.Models
{
    public class PatientImage
    {
        public string ImageId { get; set; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Description { get; set; } = "";
        public string SourceType { get; set; } = "";
        public DateTime SavedDate { get; set; } = DateTime.UtcNow;
        public string SavedByProviderId { get; set; } = "";

        [JsonIgnore]
        public string DisplayLabel =>
            string.IsNullOrEmpty(Tag)
                ? FileName
                : $"{Tag} — {FileName}";
    }
}
