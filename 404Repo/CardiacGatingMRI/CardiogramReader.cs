namespace CardiacGatingMRI;

/// <summary>
/// A single sample in the cardiogram time series.
/// </summary>
public readonly record struct CardioSample(double TimeMs, double Signal);

/// <summary>
/// Reads a cardiogram CSV file.
///
/// Expected format (header row optional – auto-detected):
///   time_ms , signal
///   0.0     , 0.12
///   8.0     , 0.45
///   ...
///
/// The first column is always interpreted as time in milliseconds.
/// The second column is the ECG/cardiogram signal amplitude.
/// Lines starting with '#' are treated as comments and skipped.
/// </summary>
public sealed class CardiogramReader
{
    public IReadOnlyList<CardioSample> Read(string csvPath)
    {
        var samples = new List<CardioSample>();

        foreach (var raw in File.ReadLines(csvPath))
        {
            string line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            // Skip header row if it contains non-numeric first token
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;
            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double t))
                continue; // header or unparseable row
            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double s))
                continue;

            samples.Add(new CardioSample(t, s));
        }

        if (samples.Count == 0)
            throw new InvalidDataException($"No valid data rows found in cardiogram file: {csvPath}");

        // Ensure sorted by time
        samples.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        return samples;
    }
}

/// <summary>
/// Determines which phase-encode lines (0-based) in a given scan
/// were acquired during cardiac motion, using the cardiogram signal
/// and a per-scan acquisition schedule.
///
/// Scientific basis:
///   Each k-space line is acquired at a specific moment in time.
///   If the cardiogram signal at that moment exceeds a motion threshold
///   (i.e., the heart is contracting / the phantom is moving), that line
///   is considered "corrupted".
///
///   The motion threshold is estimated as:
///     baseline + sensitivity * RMS_amplitude
///   where baseline = median signal over the full cardiogram,
///   and RMS_amplitude = sqrt(mean(signal^2)).
///
///   This is a simple but robust threshold widely used in retrospective
///   cardiac gating literature (Ehman & Felmlee 1989; Pipe 1999).
/// </summary>
public sealed class MotionClassifier
{
    private readonly double _sensitivity;

    /// <param name="sensitivity">
    /// Multiplier on the signal RMS used to set the motion threshold.
    /// Increase to flag fewer lines as corrupted; decrease for stricter gating.
    /// Typical range: 0.1 – 0.5.
    /// </param>
    public MotionClassifier(double sensitivity = 0.25) => _sensitivity = sensitivity;

    /// <summary>
    /// For each PE line in a scan, looks up the cardiogram signal at the
    /// line's acquisition time and returns true if that line was acquired
    /// during motion.
    /// </summary>
    /// <param name="cardiogram">Full cardiogram time series.</param>
    /// <param name="lineTimesMs">
    /// Array of length Npe: lineTimesMs[pe] = acquisition timestamp (ms) for PE line pe.
    /// </param>
    public bool[] ClassifyLines(IReadOnlyList<CardioSample> cardiogram, double[] lineTimesMs)
    {
        double[] times   = cardiogram.Select(s => s.TimeMs).ToArray();
        double[] signals = cardiogram.Select(s => s.Signal).ToArray();

        double threshold = ComputeThreshold(signals);

        int npe = lineTimesMs.Length;
        bool[] corrupted = new bool[npe];
        for (int pe = 0; pe < npe; pe++)
        {
            double sig = Interpolate(times, signals, lineTimesMs[pe]);
            corrupted[pe] = sig > threshold;
        }
        return corrupted;
    }

    private double ComputeThreshold(double[] signals)
    {
        double[] sorted = (double[])signals.Clone();
        Array.Sort(sorted);
        double median = sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0
            : sorted[sorted.Length / 2];

        double rms = Math.Sqrt(signals.Select(s => s * s).Average());
        return median + _sensitivity * rms;
    }

    // Linear interpolation; clamps to boundary values outside range.
    private static double Interpolate(double[] times, double[] values, double t)
    {
        if (t <= times[0]) return values[0];
        if (t >= times[^1]) return values[^1];

        int idx = Array.BinarySearch(times, t);
        if (idx >= 0) return values[idx];
        idx = ~idx; // first index greater than t
        double t0 = times[idx - 1], t1 = times[idx];
        double v0 = values[idx - 1], v1 = values[idx];
        double frac = (t - t0) / (t1 - t0);
        return v0 + frac * (v1 - v0);
    }
}
