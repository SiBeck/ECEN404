namespace CardiacGatingMRI;

public readonly record struct CardioSample(
    double TimeMs,
    double Signal
);

public sealed class CardiogramReader
{
    public IReadOnlyList<CardioSample> Read(string csvPath)
    {
        var samples = new List<CardioSample>();

        foreach (string raw in File.ReadLines(csvPath))
        {
            string line = raw.Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            string[] parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                continue;

            if (!double.TryParse(
                    parts[0],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double t))
                continue;

            if (!double.TryParse(
                    parts[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double s))
                continue;

            samples.Add(new CardioSample(t, s));
        }

        if (samples.Count == 0)
            throw new InvalidDataException($"No valid data rows found in cardiogram file: {csvPath}");

        samples.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        return samples;
    }
}

public sealed class MotionClassifier
{
    private readonly double _sensitivity;

    public MotionClassifier(double sensitivity = 0.25)
        => _sensitivity = Math.Clamp(sensitivity, 0.05, 2.0);

    public bool[] ClassifyLines(IReadOnlyList<CardioSample> cardiogram, double[] lineTimesMs)
    {
        if (cardiogram.Count < 3)
            return Enumerable.Repeat(true, lineTimesMs.Length).ToArray();

        double[] times = cardiogram.Select(s => s.TimeMs).ToArray();
        double[] signals = cardiogram.Select(s => s.Signal).ToArray();
        double[] smoothed = SmoothSignal(signals, windowRadius: 2);
        double[] rPeakTimesMs = DetectRPeakTimes(times, smoothed);

        if (rPeakTimesMs.Length < 2)
            return Enumerable.Repeat(true, lineTimesMs.Length).ToArray();

        (double quietStartPhase, double quietEndPhase) = ComputeQuiescentWindow();

        int npe = lineTimesMs.Length;
        bool[] corrupted = new bool[npe];

        for (int pe = 0; pe < npe; pe++)
            corrupted[pe] = !IsWithinQuiescentWindow(
                lineTimesMs[pe],
                rPeakTimesMs,
                quietStartPhase,
                quietEndPhase);

        return corrupted;
    }

    private (double StartPhase, double EndPhase) ComputeQuiescentWindow()
    {
        double centerPhase = 0.70;
        double halfWidth = Math.Clamp(0.09 + _sensitivity * 0.06, 0.08, 0.16);
        return (centerPhase - halfWidth, centerPhase + halfWidth);
    }

    private static bool IsWithinQuiescentWindow(
        double lineTimeMs,
        double[] rPeakTimesMs,
        double quietStartPhase,
        double quietEndPhase)
    {
        int idx = Array.BinarySearch(rPeakTimesMs, lineTimeMs);
        if (idx >= 0)
        {
            if (idx >= rPeakTimesMs.Length - 1)
                return false;

            double rr = rPeakTimesMs[idx + 1] - rPeakTimesMs[idx];
            if (rr <= 0.0)
                return false;

            return quietStartPhase <= 0.0;
        }

        int upper = ~idx;
        if (upper <= 0 || upper >= rPeakTimesMs.Length)
            return false;

        double cycleStart = rPeakTimesMs[upper - 1];
        double cycleEnd = rPeakTimesMs[upper];
        double rrInterval = cycleEnd - cycleStart;
        if (rrInterval <= 0.0)
            return false;

        double phase = (lineTimeMs - cycleStart) / rrInterval;
        return phase >= quietStartPhase && phase <= quietEndPhase;
    }

    private static double[] DetectRPeakTimes(double[] times, double[] signals)
    {
        if (times.Length < 3 || signals.Length < 3)
            return [];

        double median = Median(signals);
        double maxValue = signals.Max();
        double minPeakHeight = median + (maxValue - median) * 0.55;
        double minPeakDistanceMs = Math.Max(250.0, EstimateMedianStep(times) * 8.0);

        var peakIndices = new List<int>();

        for (int i = 1; i < signals.Length - 1; i++)
        {
            bool isLocalMax = signals[i] >= signals[i - 1] && signals[i] >= signals[i + 1];
            if (!isLocalMax || signals[i] < minPeakHeight)
                continue;

            if (peakIndices.Count == 0)
            {
                peakIndices.Add(i);
                continue;
            }

            int lastPeak = peakIndices[^1];
            if (times[i] - times[lastPeak] >= minPeakDistanceMs)
            {
                peakIndices.Add(i);
                continue;
            }

            if (signals[i] > signals[lastPeak])
                peakIndices[^1] = i;
        }

        return peakIndices.Select(index => times[index]).ToArray();
    }

    private static double[] SmoothSignal(double[] signals, int windowRadius)
    {
        var smoothed = new double[signals.Length];

        for (int i = 0; i < signals.Length; i++)
        {
            int start = Math.Max(0, i - windowRadius);
            int end = Math.Min(signals.Length - 1, i + windowRadius);

            double sum = 0.0;
            for (int j = start; j <= end; j++)
                sum += signals[j];

            smoothed[i] = sum / (end - start + 1);
        }

        return smoothed;
    }

    private static double EstimateMedianStep(double[] times)
    {
        if (times.Length < 2)
            return 1.0;

        double[] steps = new double[times.Length - 1];
        for (int i = 1; i < times.Length; i++)
            steps[i - 1] = Math.Max(0.0, times[i] - times[i - 1]);

        return Math.Max(1.0, Median(steps));
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
            return 0.0;

        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);

        double median = sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0
            : sorted[sorted.Length / 2];

        return median;
    }

}
