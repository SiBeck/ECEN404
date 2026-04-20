using YamlDotNet.Serialization; // YAML mapping attributes + deserializer.
using YamlDotNet.Serialization.NamingConventions; // snake_case naming support.

namespace CardiacGating; // Project namespace.

// Alignment configuration section.
public sealed class AlignmentConfig
{
    [YamlMember(Alias = "max_offset_ms")] // YAML key name.
    public double MaxOffsetMs { get; set; } = 2500.0; // Warn if alignment exceeds this.
}

// Cycle detection configuration section.
public sealed class CycleDetectionConfig
{
    [YamlMember(Alias = "window_ms")] // YAML key name.
    public double WindowMs { get; set; } = 900.0; // Baseline/noise window length.

    [YamlMember(Alias = "min_peak_prominence")] // YAML key name.
    public double MinPeakProminence { get; set; } = 0.5; // Threshold multiplier.

    [YamlMember(Alias = "stable_rr_variation_pct")] // YAML key name.
    public double StableRrVariationPct { get; set; } = 7.5; // Allowed RR drift percent.

    [YamlMember(Alias = "min_rr_ms")] // YAML key name.
    public double MinRrMs { get; set; } = 400.0; // Lower RR bound.

    [YamlMember(Alias = "max_rr_ms")] // YAML key name.
    public double MaxRrMs { get; set; } = 1800.0; // Upper RR bound.
}

// Frame filtering configuration section.
public sealed class FilteringConfig
{
    [YamlMember(Alias = "stable_phase_fraction")] // YAML key name.
    public double StablePhaseFraction { get; set; } = 0.35; // Fraction of cycle to keep.

    [YamlMember(Alias = "stable_phase_offset")] // YAML key name.
    public double StablePhaseOffset { get; set; } = 0.25; // Start offset within cycle.

    [YamlMember(Alias = "fallback_min_frames")] // YAML key name.
    public int FallbackMinFrames { get; set; } = 2; // Ensure at least this many outputs.
}

// Input/output and MRD preprocessing configuration section.
public sealed class IOConfig
{
    [YamlMember(Alias = "default_output_dir")] // YAML key name.
    public string DefaultOutputDir { get; set; } = "output/gated_dicom"; // Default output folder.

    [YamlMember(Alias = "mrd_frame_period_ms")] // YAML key name.
    public double MrdFramePeriodMs { get; set; } = 40.0; // Timestamp delta between frames.

    [YamlMember(Alias = "mrd_start_time_ms")] // YAML key name.
    public double MrdStartTimeMs { get; set; } = 0.0; // Timestamp of first frame.

    [YamlMember(Alias = "mrd_apply_hamming_window")] // YAML key name.
    public bool MrdApplyHammingWindow { get; set; } = false; // Optional apodization.

    [YamlMember(Alias = "mrd_zero_pad_fe")] // YAML key name.
    public int? MrdZeroPadFe { get; set; } = null; // Optional FE target size.

    [YamlMember(Alias = "mrd_zero_pad_pe")] // YAML key name.
    public int? MrdZeroPadPe { get; set; } = null; // Optional PE target size.
}

// Root object containing all config sections.
public sealed class ScanFilterConfig
{
    public AlignmentConfig Alignment { get; set; } = new(); // Alignment section object.

    [YamlMember(Alias = "cycle_detection")] // YAML key for cycle section.
    public CycleDetectionConfig CycleDetection { get; set; } = new(); // Cycle section object.

    public FilteringConfig Filtering { get; set; } = new(); // Filtering section object.

    public IOConfig Io { get; set; } = new(); // IO section object.

    // Loads YAML config from disk, returns defaults when missing/empty.
    public static ScanFilterConfig Load(string path)
    {
        if (!File.Exists(path)) // Missing config is acceptable.
            return new ScanFilterConfig(); // Return default values.

        string yaml = File.ReadAllText(path); // Read YAML text from file.

        var deserializer = new DeserializerBuilder() // Build YAML deserializer.
            .WithNamingConvention(UnderscoredNamingConvention.Instance) // Map snake_case names.
            .IgnoreUnmatchedProperties() // Ignore unknown keys for forward compatibility.
            .Build(); // Finalize deserializer.

        return deserializer.Deserialize<ScanFilterConfig>(yaml) // Parse YAML to object graph.
               ?? new ScanFilterConfig(); // Fallback if parser returns null.
    }
}
