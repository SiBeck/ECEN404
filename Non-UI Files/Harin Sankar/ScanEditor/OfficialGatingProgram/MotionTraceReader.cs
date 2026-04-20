namespace OfficialGatingProgram;

public readonly record struct MotionSample(double TimeMs, double Displacement);

public sealed class MotionTraceReader
{
    public IReadOnlyList<MotionSample> Read(string csvPath)
    {
        var samples = new List<MotionSample>();

        foreach (string raw in File.ReadLines(csvPath))
        {
            string line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            string[] parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                continue;

            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double timeMs))
                continue;

            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double displacement))
                continue;

            samples.Add(new MotionSample(timeMs, displacement));
        }

        if (samples.Count < 3)
            throw new InvalidDataException($"Motion CSV must contain at least 3 valid rows: {csvPath}");

        samples.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        return samples;
    }
}
