namespace OfficialGatingProgram;

public sealed record StillSegment(double StartTimeMs, double EndTimeMs)
{
    public double DurationMs => EndTimeMs - StartTimeMs;
}

public sealed class MotionAnalysis
{
    public IReadOnlyList<MotionSample> Samples { get; }
    public double[] SmoothedDisplacement { get; }
    public double[] VelocityUnitsPerMs { get; }
    public bool[] IsStillSample { get; }
    public StillSegment[] StillSegments { get; }
    public double VelocityThreshold { get; }

    public MotionAnalysis(
        IReadOnlyList<MotionSample> samples,
        double[] smoothedDisplacement,
        double[] velocityUnitsPerMs,
        bool[] isStillSample,
        StillSegment[] stillSegments,
        double velocityThreshold)
    {
        Samples = samples;
        SmoothedDisplacement = smoothedDisplacement;
        VelocityUnitsPerMs = velocityUnitsPerMs;
        IsStillSample = isStillSample;
        StillSegments = stillSegments;
        VelocityThreshold = velocityThreshold;
    }
}

public sealed class MotionStillnessDetector
{
    private readonly double? _velocityThreshold;
    private readonly double _minStillMs;
    private readonly int _smoothRadius;

    public MotionStillnessDetector(double? velocityThreshold = null, double minStillMs = 40.0, int smoothRadius = 2)
    {
        _velocityThreshold = velocityThreshold;
        _minStillMs = minStillMs;
        _smoothRadius = Math.Max(1, smoothRadius);
    }

    public MotionAnalysis Analyze(IReadOnlyList<MotionSample> samples)
    {
        if (samples.Count < 3)
            throw new ArgumentException("At least 3 motion samples are required.", nameof(samples));

        double[] times = samples.Select(s => s.TimeMs).ToArray();
        double[] displacements = samples.Select(s => s.Displacement).ToArray();
        double[] smoothed = Smooth(displacements, _smoothRadius);
        double[] velocity = ComputeVelocity(times, smoothed);
        double[] absVelocity = velocity.Select(Math.Abs).ToArray();
        double threshold = _velocityThreshold ?? ComputeAdaptiveThreshold(absVelocity);
        bool[] rawStill = absVelocity.Select(v => v <= threshold).ToArray();
        StillSegment[] segments = BuildStillSegments(times, rawStill, _minStillMs);
        bool[] filteredStill = BuildStillMask(times, segments);

        return new MotionAnalysis(samples, smoothed, velocity, filteredStill, segments, threshold);
    }

    public bool[] ClassifyLineTimes(MotionAnalysis analysis, double[] lineTimesMs)
    {
        var accepted = new bool[lineTimesMs.Length];
        for (int i = 0; i < lineTimesMs.Length; i++)
            accepted[i] = IsStillAtTime(analysis.StillSegments, lineTimesMs[i]);
        return accepted;
    }

    private static bool IsStillAtTime(IReadOnlyList<StillSegment> segments, double timeMs)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            if (timeMs >= segments[i].StartTimeMs && timeMs <= segments[i].EndTimeMs)
                return true;
        }

        return false;
    }

    private static bool[] BuildStillMask(double[] times, IReadOnlyList<StillSegment> segments)
    {
        var mask = new bool[times.Length];
        for (int i = 0; i < times.Length; i++)
            mask[i] = IsStillAtTime(segments, times[i]);
        return mask;
    }

    private static StillSegment[] BuildStillSegments(double[] times, bool[] rawStill, double minStillMs)
    {
        var segments = new List<StillSegment>();
        int start = -1;

        for (int i = 0; i < rawStill.Length; i++)
        {
            if (rawStill[i] && start < 0)
            {
                start = i;
                continue;
            }

            if (!rawStill[i] && start >= 0)
            {
                double startTime = times[start];
                double endTime = times[i - 1];
                if (endTime - startTime >= minStillMs)
                    segments.Add(new StillSegment(startTime, endTime));
                start = -1;
            }
        }

        if (start >= 0)
        {
            double startTime = times[start];
            double endTime = times[^1];
            if (endTime - startTime >= minStillMs)
                segments.Add(new StillSegment(startTime, endTime));
        }

        return segments.ToArray();
    }

    private static double[] Smooth(double[] values, int radius)
    {
        var smoothed = new double[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            int start = Math.Max(0, i - radius);
            int end = Math.Min(values.Length - 1, i + radius);
            double sum = 0.0;

            for (int j = start; j <= end; j++)
                sum += values[j];

            smoothed[i] = sum / (end - start + 1);
        }

        return smoothed;
    }

    private static double[] ComputeVelocity(double[] times, double[] displacement)
    {
        var velocity = new double[displacement.Length];

        for (int i = 0; i < displacement.Length; i++)
        {
            if (i == 0)
            {
                velocity[i] = SafeSlope(times[i], displacement[i], times[i + 1], displacement[i + 1]);
                continue;
            }

            if (i == displacement.Length - 1)
            {
                velocity[i] = SafeSlope(times[i - 1], displacement[i - 1], times[i], displacement[i]);
                continue;
            }

            velocity[i] = SafeSlope(times[i - 1], displacement[i - 1], times[i + 1], displacement[i + 1]);
        }

        return velocity;
    }

    private static double SafeSlope(double t0, double y0, double t1, double y1)
    {
        double dt = t1 - t0;
        if (Math.Abs(dt) < 1e-9)
            return 0.0;
        return (y1 - y0) / dt;
    }

    private static double ComputeAdaptiveThreshold(double[] absVelocity)
    {
        double p20 = Percentile(absVelocity, 0.20);
        double p60 = Percentile(absVelocity, 0.60);
        double p90 = Percentile(absVelocity, 0.90);
        double spread = Math.Max(0.0, p90 - p20);
        return Math.Max(1e-6, p20 + 0.15 * Math.Max(spread, p60 - p20));
    }

    private static double Percentile(double[] values, double p)
    {
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);

        if (sorted.Length == 1)
            return sorted[0];

        double position = p * (sorted.Length - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sorted[lower];

        double frac = position - lower;
        return sorted[lower] + frac * (sorted[upper] - sorted[lower]);
    }
}
