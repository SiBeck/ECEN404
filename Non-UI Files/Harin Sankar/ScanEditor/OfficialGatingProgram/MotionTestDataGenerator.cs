using System.Numerics;
using System.Text;

namespace OfficialGatingProgram;

public sealed class MotionTestDataGenerator
{
    private readonly Random _rng;

    public MotionTestDataGenerator(int seed = 42)
    {
        _rng = new Random(seed);
    }

    public GeneratedMotionTestSet Generate(
        string referenceMrdPath,
        string outputDir,
        int numScans,
        double linePeriodMs,
        double scanGapMs,
        double motionSamplePeriodMs = 4.0,
        double? stillThreshold = null)
    {
        Directory.CreateDirectory(outputDir);

        var reader = new MrdReader();
        MrdData reference = reader.Read(referenceMrdPath);
        float[,] referenceMagnitude = ImageReconstructor.Reconstruct(reference);

        string referenceCopyPath = Path.Combine(outputDir, Path.GetFileName(referenceMrdPath));
        File.Copy(referenceMrdPath, referenceCopyPath, overwrite: true);

        string referencePngPath = Path.Combine(outputDir, $"{reference.FileName}_reference.png");
        string siblingPng = Path.Combine(Path.GetDirectoryName(referenceMrdPath) ?? ".", $"{reference.FileName}.png");
        if (File.Exists(siblingPng))
            File.Copy(siblingPng, referencePngPath, overwrite: true);
        else
            ImageReconstructor.SavePng(referenceMagnitude, referencePngPath);

        int npe = reference.Npe;
        double scanDurationMs = npe * linePeriodMs;
        double[] scanStartTimes = Enumerable.Range(0, numScans)
            .Select(index => index * (scanDurationMs + scanGapMs))
            .ToArray();

        double totalDurationMs = scanStartTimes[^1] + scanDurationMs + 320.0;
        MotionSample[] motionSamples = BuildMotionTrace(totalDurationMs, motionSamplePeriodMs);
        string motionCsvPath = Path.Combine(outputDir, "motion_displacement.csv");
        WriteMotionCsv(motionCsvPath, motionSamples);

        var detector = new MotionStillnessDetector(stillThreshold, minStillMs: 40.0);
        MotionAnalysis analysis = detector.Analyze(motionSamples);

        string[] scanPaths = new string[numScans];
        string[] lineTimePaths = new string[numScans];

        for (int scanIndex = 0; scanIndex < numScans; scanIndex++)
        {
            double[] lineTimes = Enumerable.Range(0, npe)
                .Select(pe => scanStartTimes[scanIndex] + pe * linePeriodMs)
                .ToArray();

            bool[] acceptedMask = detector.ClassifyLineTimes(analysis, lineTimes);
            MrdData corrupted = reference.Clone();

            for (int pe = 0; pe < npe; pe++)
            {
                if (acceptedMask[pe])
                    continue;

                Complex[] line = corrupted.GetLine(pe);
                double phi0 = (_rng.NextDouble() - 0.5) * 2.0 * Math.PI;
                double phi1 = (_rng.NextDouble() - 0.5) * 0.18;
                double amplitudeScale = 0.55 + _rng.NextDouble() * 0.65;

                for (int fe = 0; fe < line.Length; fe++)
                {
                    double phase = phi0 + phi1 * fe;
                    Complex rotation = new Complex(Math.Cos(phase), Math.Sin(phase));
                    Complex smear = fe > 0 ? 0.08 * line[fe - 1] : Complex.Zero;
                    line[fe] = amplitudeScale * line[fe] * rotation + smear;
                }

                corrupted.SetLine(pe, line);
            }

            string scanPath = Path.Combine(outputDir, $"scan_{scanIndex:D2}.mrd");
            string scanPngPath = Path.Combine(outputDir, $"scan_{scanIndex:D2}.png");
            string lineTimePath = Path.Combine(outputDir, $"scan_{scanIndex:D2}_linetimes.csv");
            WriteMrd(corrupted, scanPath);
            ImageReconstructor.SavePng(ImageReconstructor.Reconstruct(corrupted), scanPngPath);
            WriteLineTimesCsv(lineTimePath, lineTimes);

            scanPaths[scanIndex] = scanPath;
            lineTimePaths[scanIndex] = lineTimePath;
        }

        string manifestPath = Path.Combine(outputDir, "README.md");
        WriteReadme(manifestPath, referenceCopyPath, referencePngPath, motionCsvPath, scanPaths, lineTimePaths, analysis);

        return new GeneratedMotionTestSet(
            referenceCopyPath,
            referencePngPath,
            motionCsvPath,
            scanPaths,
            lineTimePaths,
            analysis.StillSegments);
    }

    private static MotionSample[] BuildMotionTrace(double totalDurationMs, double samplePeriodMs)
    {
        int sampleCount = (int)Math.Floor(totalDurationMs / samplePeriodMs) + 1;
        var samples = new MotionSample[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            double timeMs = i * samplePeriodMs;
            double cycleTime = timeMs % 960.0;
            double displacement = cycleTime switch
            {
                < 180.0 => 0.0,
                < 240.0 => 18.0 * ((cycleTime - 180.0) / 60.0),
                < 340.0 => 18.0,
                < 400.0 => 18.0 * (1.0 - ((cycleTime - 340.0) / 60.0)),
                < 600.0 => 0.0,
                < 660.0 => -14.0 * ((cycleTime - 600.0) / 60.0),
                < 760.0 => -14.0,
                < 820.0 => -14.0 * (1.0 - ((cycleTime - 760.0) / 60.0)),
                _ => 0.0,
            };

            samples[i] = new MotionSample(timeMs, displacement);
        }

        return samples;
    }

    private static void WriteMotionCsv(string path, IReadOnlyList<MotionSample> samples)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine("time_ms,displacement");
        foreach (MotionSample sample in samples)
            writer.WriteLine($"{sample.TimeMs:F3},{sample.Displacement:F6}");
    }

    private static void WriteLineTimesCsv(string path, IReadOnlyList<double> lineTimesMs)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine("# PE line acquisition times (ms) - one value per line, 0-based order");
        writer.WriteLine("time_ms");
        foreach (double timeMs in lineTimesMs)
            writer.WriteLine(timeMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void WriteMrd(MrdData mrd, string path)
    {
        using var writer = new BinaryWriter(File.Open(path, FileMode.Create), Encoding.ASCII, false);

        int nFe = mrd.Nfe;
        int nPe = mrd.Npe;
        int n3d = 1;
        int nSlice = 1;
        int nEchoes = 1;
        int nExps = 1;

        writer.Write(nFe);
        writer.Write(nPe);
        writer.Write(n3d);
        writer.Write(nSlice);
        writer.Write((short)0);
        writer.Write((short)0x15);
        writer.Write(new byte[132]);
        writer.Write(nEchoes);
        writer.Write(nExps);

        long current = writer.BaseStream.Position;
        writer.Write(new byte[256 - current]);
        writer.Write(new byte[256]);

        for (int pe = 0; pe < nPe; pe++)
        for (int fe = 0; fe < nFe; fe++)
        {
            Complex value = mrd.KSpace[fe, pe];
            writer.Write((float)value.Real);
            writer.Write((float)value.Imaginary);
        }

        writer.Write(new byte[120]);
    }

    private static void WriteReadme(
        string path,
        string referenceMrdPath,
        string referencePngPath,
        string motionCsvPath,
        IReadOnlyList<string> scanPaths,
        IReadOnlyList<string> lineTimePaths,
        MotionAnalysis analysis)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine("# te014_multiscan_case");
        writer.WriteLine();
        writer.WriteLine("Synthetic multi-scan motion-gating case generated from a clean reference MRD.");
        writer.WriteLine();
        writer.WriteLine("## Reference inputs");
        writer.WriteLine();
        writer.WriteLine($"- Reference MRD: {Path.GetFileName(referenceMrdPath)}");
        writer.WriteLine($"- Reference PNG: {Path.GetFileName(referencePngPath)}");
        writer.WriteLine($"- Motion CSV: {Path.GetFileName(motionCsvPath)}");
        writer.WriteLine();
        writer.WriteLine("## Generated scans");
        writer.WriteLine();
        for (int i = 0; i < scanPaths.Count; i++)
            writer.WriteLine($"- {Path.GetFileName(scanPaths[i])} with {Path.GetFileName(lineTimePaths[i])}");
        writer.WriteLine();
        writer.WriteLine("## Still segments detected from motion trace");
        writer.WriteLine();
        foreach (StillSegment segment in analysis.StillSegments)
            writer.WriteLine($"- {segment.StartTimeMs:F1} ms -> {segment.EndTimeMs:F1} ms ({segment.DurationMs:F1} ms)");
    }
}

public sealed class GeneratedMotionTestSet
{
    public string ReferenceMrdPath { get; }
    public string ReferencePngPath { get; }
    public string MotionCsvPath { get; }
    public string[] ScanPaths { get; }
    public string[] LineTimePaths { get; }
    public StillSegment[] StillSegments { get; }

    public GeneratedMotionTestSet(
        string referenceMrdPath,
        string referencePngPath,
        string motionCsvPath,
        string[] scanPaths,
        string[] lineTimePaths,
        StillSegment[] stillSegments)
    {
        ReferenceMrdPath = referenceMrdPath;
        ReferencePngPath = referencePngPath;
        MotionCsvPath = motionCsvPath;
        ScanPaths = scanPaths;
        LineTimePaths = lineTimePaths;
        StillSegments = stillSegments;
    }
}
