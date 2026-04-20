using System.Numerics; // Complex number type for k-space samples.

namespace CardiacGating; // Project namespace.

// Immutable cardiogram sample (time + signal value).
public sealed record CardioSample(
    double TimestampMs, // Sample timestamp in milliseconds.
    double Signal);     // Sample signal amplitude.

// Read-only cardiogram container with convenience projections.
public sealed class CardioSeries
{
    public IReadOnlyList<CardioSample> Samples { get; } // Immutable sample list.

    // Materialize incoming enumeration once and expose as read-only.
    public CardioSeries(IEnumerable<CardioSample> samples)
        => Samples = samples.ToList().AsReadOnly(); // Prevent accidental mutation.

    public double[] Timestamps => Samples.Select(s => s.TimestampMs).ToArray(); // Time axis projection.
    public double[] Values     => Samples.Select(s => s.Signal).ToArray();      // Signal axis projection.

    // Estimates average sample period from adjacent timestamp differences.
    public double EstimateSamplePeriodMs()
    {
        double[] ts = Timestamps; // Extract timestamp vector.

        if (ts.Length < 2) // Need at least two timestamps for a delta.
            return 1.0;     // Safe fallback to avoid division by zero downstream.

        double sum = 0; // Sum of adjacent deltas.

        for (int i = 1; i < ts.Length; i++) // Walk from second sample to end.
            sum += ts[i] - ts[i - 1];       // Add each neighbor difference.

        return sum / (ts.Length - 1); // Arithmetic mean delta.
    }
}

// Immutable cardiac cycle descriptor derived from peak detection.
public sealed record CardiacCycle(
    double StartMs, // Start timestamp of cycle window.
    double PeakMs,  // Peak timestamp for cycle.
    double EndMs,   // End timestamp of cycle window.
    double RrMs,    // RR interval in milliseconds.
    bool IsStable)  // Stability classification.
{
    public double DurationMs => EndMs - StartMs; // Cached cycle duration helper.
}

// Single MRI frame carrying timestamp + k-space matrix + optional metadata.
public sealed class MRIFrame
{
    public int FrameId { get; } // Source frame index.
    public double TimestampMs { get; } // Frame timestamp in milliseconds.
    public Complex[,] Dataset { get; } // Complex k-space matrix.
    public IReadOnlyDictionary<string, object>? Metadata { get; } // Optional metadata map.

    // Constructor stores frame payload and metadata.
    public MRIFrame(
        int frameId,                                   // Unique frame index.
        double timestampMs,                            // Frame timestamp.
        Complex[,] dataset,                            // Frame k-space matrix.
        IReadOnlyDictionary<string, object>? metadata = null) // Optional metadata.
    {
        FrameId = frameId;       // Save ID.
        TimestampMs = timestampMs;// Save timestamp.
        Dataset = dataset;       // Save matrix reference.
        Metadata = metadata;     // Save metadata reference.
    }

    // Creates a copy with shifted timestamp and same data payload.
    public MRIFrame WithOffset(double offsetMs)
        => new(
            FrameId,                  // Keep same frame index.
            TimestampMs + offsetMs,   // Shift timestamp by offset.
            Dataset,                  // Reuse same k-space matrix.
            Metadata);                // Reuse same metadata.
}

// Read-only MRI frame sequence plus associated alignment metadata.
public sealed class MRIFrameSeries
{
    public IReadOnlyList<MRIFrame> Frames { get; } // Immutable frame list.
    public double AlignmentOffsetMs { get; } // Applied series-level offset.

    // Constructor stores frame collection and optional alignment offset.
    public MRIFrameSeries(
        IEnumerable<MRIFrame> frames,      // Source frame sequence.
        double alignmentOffsetMs = 0.0)    // Optional offset metadata.
    {
        Frames = frames.ToList().AsReadOnly(); // Materialize and freeze list.
        AlignmentOffsetMs = alignmentOffsetMs; // Store offset info.
    }

    public double[] Timestamps => Frames.Select(f => f.TimestampMs).ToArray(); // Time projection.

    // Applies uniform timestamp shift to every frame.
    public MRIFrameSeries WithOffset(double offsetMs)
        => new(
            Frames.Select(f => f.WithOffset(offsetMs)), // Shift each frame timestamp.
            offsetMs);                                   // Save applied offset.
}
