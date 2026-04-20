using System.Numerics;
using System.Text;

namespace CardiacGatingMRI;

public sealed class TestDataGenerator
{
    private readonly Random _rng;

    private static readonly (int scan, int first, int last)[] CorruptionWindowsFallback =
    [
        (0, 17, 26),
        (1, 59, 68),
        (2, 94, 103),
        (3, 21, 29),
        (4, 74, 81),
        (5, 109, 117),
        (6, 39, 49),
        (7, 5, 13),
        (8, 80, 90),
        (9, 120, 121),
    ];

    public TestDataGenerator(int seed = 42) => _rng = new Random(seed);

    public GeneratedTestSet(
        string referenceMrd,
        string outputDir,
        double linePeriodMs = 8.0,
        double sensitivity = 0.25)
    {
        Directory.CreateDirectory(outputDir);

        var reader = new MrdReader();
        MrdData reference = reader.Read(referenceMrd);
        int npe = reference.Npe;
        int nfe = reference.Nfe;

        double scanDurationMs = npe * linePeriodMs;
        int numScans = 10;

        var scanStartTimes = new double[numScans];
        for (int i = 0; i < numScans; i++)
            scanStartTimes[i] = i * scanDurationMs;

        double GetLineTime(int scanIdx, int pe) => scanStartTimes[scanIdx] + pe * linePeriodMs;

        double normalRrMs = 60_000.0 / 70.0;
        double mediumRrMs = 60_000.0 / 92.0;
        double fastRrMs = 60_000.0 / 115.0;
        double sampleIntervalMs = 2.0;

        var highArtifactScans = new HashSet<int> { 0, 1, 5, 6 };
        var mediumArtifactScans = new HashSet<int> { 2, 3, 4, 9 };

        double totalDurationMs = numScans * scanDurationMs + 2000;
        var rPeakTimes = new List<double>();

        {
            double t = 0.0;
            while (t < totalDurationMs)
            {
                rPeakTimes.Add(t);

                int scanIdx = -1;
                for (int si = 0; si < numScans; si++)
                {
                    if (t >= scanStartTimes[si] && t < scanStartTimes[si] + scanDurationMs)
                    {
                        scanIdx = si;
                        break;
                    }
                }

                double rr = (scanIdx >= 0 && highArtifactScans.Contains(scanIdx)) ? fastRrMs
                          : (scanIdx >= 0 && mediumArtifactScans.Contains(scanIdx)) ? mediumRrMs
                          : normalRrMs;

                t += rr;
            }
        }

        (double a, double w, double dt)[] waves =
        [
            (0.15, 25.0, -180.0),
            (-0.10, 12.0, -80.0),
            (1.00, 15.0, 0.0),
            (-0.20, 18.0, +80.0),
            (0.35, 50.0, +250.0),
        ];

        int nSamples = (int)(totalDurationMs / sampleIntervalMs) + 1;
        var cardioTimes = new double[nSamples];
        var cardioSignals = new double[nSamples];

        for (int i = 0; i < nSamples; i++)
        {
            double tMs = i * sampleIntervalMs;
            cardioTimes[i] = tMs;
            double sig = 0.0;

            foreach (double rp in rPeakTimes)
            {
                double delta = tMs - rp;
                if (delta < -600 || delta > 600) continue;

                foreach (var (a, w, offset) in waves)
                {
                    double d = delta - offset;
                    sig += a * Math.Exp(-d * d / (2.0 * w * w));
                }
            }

            sig += 0.03 * Math.Sin(2 * Math.PI * tMs / 4000.0);
            cardioSignals[i] = sig;
        }

        var cardiogram = new List<CardioSample>(nSamples);
        for (int i = 0; i < nSamples; i++)
            cardiogram.Add(new CardioSample(cardioTimes[i], cardioSignals[i]));

        var classifier = new MotionClassifier(sensitivity);

        var corruptSets = new HashSet<int>[numScans];
        for (int si = 0; si < numScans; si++)
        {
            corruptSets[si] = [];
            double[] lineTimes = Enumerable.Range(0, npe)
                .Select(pe => GetLineTime(si, pe))
                .ToArray();

            bool[] corrupted = classifier.ClassifyLines(cardiogram, lineTimes);
            for (int pe = 0; pe < corrupted.Length; pe++)
            {
                if (corrupted[pe])
                    corruptSets[si].Add(pe);
            }
        }

        var scanPaths = new string[numScans];
        var allLineTimes = new double[numScans][];

        for (int si = 0; si < numScans; si++)
        {
            MrdData corrupted = reference.Clone();

            foreach (int pe in corruptSets[si])
            {
                Complex[] line = corrupted.GetLine(pe);
                double phi0 = (_rng.NextDouble() - 0.5) * 2 * Math.PI;
                double phi1 = (_rng.NextDouble() - 0.5) * 0.12;
                double epsilon = (_rng.NextDouble() - 0.5) * 0.4;

                for (int fe = 0; fe < nfe; fe++)
                {
                    double angle = phi0 + phi1 * fe;
                    var rot = new Complex(Math.Cos(angle), Math.Sin(angle));
                    line[fe] = line[fe] * rot * (1.0 + epsilon);
                }

                corrupted.SetLine(pe, line);
            }

            string mrdPath = Path.Combine(outputDir, $"scan_{si:D2}.mrd");
            WriteMrd(corrupted, mrdPath);
            scanPaths[si] = mrdPath;

            string pngPath = Path.Combine(outputDir, $"scan_{si:D2}.png");
            float[,] mag = ImageReconstructor.Reconstruct(corrupted);
            ImageReconstructor.SavePng(mag, pngPath);

            allLineTimes[si] = new double[npe];
            for (int pe = 0; pe < npe; pe++)
                allLineTimes[si][pe] = GetLineTime(si, pe);

            string ltPath = Path.Combine(outputDir, $"scan_{si:D2}_linetimes.csv");
            WriteLineTimesCsv(ltPath, allLineTimes[si]);
        }

        string csvPath = Path.Combine(outputDir, "cardiogram.csv");
        WriteCardiogramCsv(csvPath, cardioTimes, cardioSignals);

        Console.WriteLine($"[test-gen] Written {numScans} scan files + cardiogram to: {outputDir}");
        Console.WriteLine($"[test-gen] Reference: {referenceMrd}  ({nfe}x{npe} k-space)");

        return new GeneratedTestSet(scanPaths, allLineTimes, csvPath, corruptSets);
    }

    private static void WriteCardiogramCsv(string path, double[] times, double[] signals)
    {
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine("# Synthetic ECG cardiogram - generated by CardiacGatingMRI test data generator");
        w.WriteLine("# Columns: time_ms , signal_mV");
        w.WriteLine("time_ms,signal");
        for (int i = 0; i < times.Length; i++)
            w.WriteLine($"{times[i]:F3},{signals[i]:F6}");
    }

    private static void WriteLineTimesCsv(string path, double[] lineTimes)
    {
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine("# PE line acquisition times (ms) - one value per line, 0-based order");
        w.WriteLine("time_ms");
        foreach (double t in lineTimes)
            w.WriteLine(t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void WriteMrd(MrdData mrd, string path)
    {
        using var f = new BinaryWriter(File.Open(path, FileMode.Create), Encoding.ASCII, false);

        int nFe = mrd.Nfe;
        int nPe = mrd.Npe;
        int n3d = 1;
        int nSlice = 1;
        int nEchoes = 1;
        int nExps = 1;

        f.Write(nFe);
        f.Write(nPe);
        f.Write(n3d);
        f.Write(nSlice);

        f.Write((short)0);
        f.Write((short)0x15);

        f.Write(new byte[132]);

        f.Write(nEchoes);
        f.Write(nExps);

        long cl = f.BaseStream.Position;
        f.Write(new byte[256 - cl]);

        byte[] desc = new byte[256];
        f.Write(desc);

        for (int pe = 0; pe < nPe; pe++)
        for (int fe = 0; fe < nFe; fe++)
        {
            Complex c = mrd.KSpace[fe, pe];
            f.Write((float)c.Real);
            f.Write((float)c.Imaginary);
        }

        f.Write(new byte[120]);
    }
}

public sealed class GeneratedTestSet
{
    public string[] ScanPaths { get; }
    public double[][] LineTimes { get; }
    public string CardiogramCsv { get; }
    public HashSet<int>[] CorruptLines { get; }

    public GeneratedTestSet(string[] scanPaths, double[][] lineTimes, string csv, HashSet<int>[] corrupt)
    {
        ScanPaths = scanPaths;
        LineTimes = lineTimes;
        CardiogramCsv = csv;
        CorruptLines = corrupt;
    }
}
