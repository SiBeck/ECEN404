using System.ComponentModel.DataAnnotations;

namespace BioMetrixDatabase.Models
{
    public class PatientImageEntity
    {
        [Key]
        public string ImageId { get; set; } = "";
        public string PatientId { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Description { get; set; } = "";
        public string SourceType { get; set; } = "";
        public DateTime SavedDate { get; set; }
        public string SavedByProviderId { get; set; } = "";
        /// <summary>Serialised ScanFilterStats JSON, or null if not applicable.</summary>
        public string? FilterStatsJson { get; set; }

        public PatientEntity? Patient { get; set; }
    }
}
