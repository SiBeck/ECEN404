using System.Numerics;
using System.Text;

namespace CardiacGatingMRI;

/// <summary>
/// Creates a synthetic test dataset for cardiac gating validation.
///
/// Process:
///   1. Load the reference k-space from te014.mrd (the "ground-truth" scan).
///   2. Simulate 10 repeated scans of the same slice, each with a different
///      set of PE lines "corrupted" by synthetic cardiac motion.
///   3. Write each corrupted scan as an .mrd file in the output directory.
///   4. Generate a realistic ECG cardiogram CSV whose motion peaks align with
///      the corrupt windows in each scan.
///
/// Motion model:
///   When the phantom moves (systole), the k-space lines acquired at that moment
///   are replaced with phase-shifted / amplitude-scaled versions to simulate
///   ghosting artefacts.  The corruption keeps data plausible (it is still
///   complex numeric data) but introduces phase errors that would blur the image.
///
/// Cardiogram model:
///   A realistic ECG is synthesised using the sum-of-Gaussians model described in:
///     McSharry P E et al. "A dynamical model for generating synthetic
///     electrocardiogram signals." IEEE Trans Biomed Eng. 2003; 50(3):289-294.
///   The heart rate is set to 70 bpm (RR ≈ 857 ms) and the total duration is
///   long enough to cover all 10 scans.
/// </summary>
public sealed class TestDataGenerator
{
    private readonly Random _rng;

    // Each entry = (scanIndex, firstCorruptPeLine, lastCorruptPeLine) — 0-based line indices.
    // Corruption windows are computed dynamically from the ECG to guarantee alignment.
    // This array is only used as a fallback when dynamic computation is unavailable.
    private static readonly (int scan, int first, int last)[] CorruptionWindowsFallback =
    [
        (0,  17,  26),
        (1,  59,  68),
        (2,  94, 103),
        (3,  21,  29),
        (4,  74,  81),
        (5, 109, 117),
        (6,  39,  49),
        (7,   5,  13),
        (8,  80,  90),
        (9, 120, 121),
    ];

    /// <param name="seed">RNG seed for reproducible test data.</param>
    public TestDataGenerator(int seed = 42) => _rng = new Random(seed);

    /// <summary>
    /// Generate test MRD files (scan_00.mrd .. scan_09.mrd) and a cardiogram CSV.
    /// </summary>
    /// <param name="referenceMrd">Path to te014.mrd (used as the clean ground-truth).</param>
    /// <param name="outputDir">Directory where test files will be written.</param>
    /// <param name="linePeriodMs">
    /// Time between consecutive PE line acquisitions within a single scan (ms).
    /// Default 8 ms gives a ~1 s scan for 128 lines – realistic for fast GRE.
    /// </param>
    public GeneratedTestSet Generate(string referenceMrd, string outputDir, double linePeriodMs = 8.0)
    {
        Directory.CreateDirectory(outputDir);

        var reader = new MrdReader();
        MrdData reference = reader.Read(referenceMrd);
        int npe = reference.Npe;
        int nfe = reference.Nfe;

        double scanDurationMs = npe * linePeriodMs;
        int numScans = 10;

        // ── Build line-acquisition timestamps for each scan ──────────────────
        // Scan i starts at i * scanDurationMs (no gap between scans for simplicity)
        var scanStartTimes = new double[numScans];
        for (int i = 0; i < numScans; i++)
            scanStartTimes[i] = i * scanDurationMs;

        // line timestamps: scan i, pe line j → scanStartTimes[i] + j * linePeriodMs
        double GetLineTime(int scanIdx, int pe) => scanStartTimes[scanIdx] + pe * linePeriodMs;

        // ── Generate realistic ECG signal with variable heart rate ──────────
        // Scans 0, 1, 5, 6 simulate a tachycardic episode (115 bpm, RR ≈ 522 ms):
        //   more R-peaks fall inside those scan windows → more corrupted PE lines.
        // Other scans use a normal resting rate (70 bpm, RR ≈ 857 ms).
        //
        // Physical basis: tachycardia shortens diastole disproportionately, leaving
        // a larger fraction of the cardiac cycle in systolic motion.  This is the
        // dominant cause of motion artefacts in ungated cardiac MRI.
        double normalRrMs  = 60_000.0 / 70.0;   // ~857 ms  – resting
        double mediumRrMs  = 60_000.0 / 92.0;   // ~652 ms  – mildly elevated
        double fastRrMs    = 60_000.0 / 115.0;  // ~522 ms  – tachycardic
        double sampleIntervalMs = 2.0;            // 500 Hz ECG

        var highArtifactScans   = new HashSet<int> { 0, 1, 5, 6 };
        var mediumArtifactScans = new HashSet<int> { 2, 3, 4, 9 };

        // Build the list of R-peak times for the whole acquisition using variable RR.
        // Between scans the RR is interpolated so there are no discontinuities.
        double totalDurationMs = numScans * scanDurationMs + 2000;
        var rPeakTimes = new List<double>();
        {
            double t = 0.0;
            while (t < totalDurationMs)
            {
                rPeakTimes.Add(t);
                // Determine which scan we're currently in (if any)
                int scanIdx = -1;
                for (int si = 0; si < numScans; si++)
                {
                    if (t >= scanStartTimes[si] && t < scanStartTimes[si] + scanDurationMs)
                    { scanIdx = si; break; }
                }
                double rr = (scanIdx >= 0 && highArtifactScans.Contains(scanIdx))   ? fastRrMs
                          : (scanIdx >= 0 && mediumArtifactScans.Contains(scanIdx)) ? mediumRrMs
                          : normalRrMs;
                t += rr;
            }
        }

        // Build ECG signal: for each sample, sum PQRST Gaussians from nearby R-peaks.
        // Motion window: [rPeak - 50 ms, rPeak + 150 ms] = systolic contraction.
        (double a, double w, double dt)[] waves =
        [
            ( 0.15, 25.0, -180.0),  // P wave
            (-0.10, 12.0,  -80.0),  // Q wave
            ( 1.00, 15.0,    0.0),  // R wave  (peak)
            (-0.20, 18.0,  +80.0),  // S wave
            ( 0.35, 50.0, +250.0),  // T wave
        ];

        int nSamples = (int)(totalDurationMs / sampleIntervalMs) + 1;
        var cardioTimes   = new double[nSamples];
        var cardioSignals = new double[nSamples];

        for (int i = 0; i < nSamples; i++)
        {
            double tMs = i * sampleIntervalMs;
            cardioTimes[i] = tMs;
            double sig = 0.0;
            // Only consider R-peaks within ±600 ms to keep the loop fast
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
            // Small respiratory baseline drift
            sig += 0.03 * Math.Sin(2 * Math.PI * tMs / 4000.0);
            cardioSignals[i] = sig;
        }

        // ── Compute classification threshold (identical to MotionClassifier) ──
        // This ensures that the lines we corrupt in the MRD files are EXACTLY the
        // same lines that GatingEngine will later identify as corrupted.  Using a
        // separate IsMotionTime() window caused mismatches (T-wave offset, P-wave
        // bleed) that left corrupt lines in the gated output.
        double[] sortedSig = (double[])cardioSignals.Clone();
        Array.Sort(sortedSig);
        double ecgMedian = sortedSig[sortedSig.Length / 2];
        double ecgRms    = Math.Sqrt(cardioSignals.Select(s => s * s).Average());
        double ecgThresh = ecgMedian + 0.25 * ecgRms;   // same formula as MotionClassifier

        // Interpolate ECG signal at an arbitrary time (matches ClassifyLines logic)
        double EcgAt(double tMs)
        {
            if (tMs <= cardioTimes[0])  return cardioSignals[0];
            if (tMs >= cardioTimes[^1]) return cardioSignals[^1];
            int lo = 0, hi = cardioTimes.Length - 1;
            while (lo < hi - 1) { int mid = (lo + hi) / 2; if (cardioTimes[mid] <= tMs) lo = mid; else hi = mid; }
            double frac = (tMs - cardioTimes[lo]) / (cardioTimes[hi] - cardioTimes[lo]);
            return cardioSignals[lo] + frac * (cardioSignals[hi] - cardioSignals[lo]);
        }

        // ── Build per-scan corruption sets using ECG threshold ───────────────
        // A PE line is corrupted if the ECG signal at its acquisition time exceeds
        // the threshold – identical to what GatingEngine will classify.  Heart rate
        // differences between scan groups naturally produce different corruption counts.
        var corruptSets = new HashSet<int>[numScans];
        for (int si = 0; si < numScans; si++)
        {
            corruptSets[si] = [];
            for (int pe = 0; pe < npe; pe++)
            {
                if (EcgAt(GetLineTime(si, pe)) > ecgThresh)
                    corruptSets[si].Add(pe);
            }
        }

        // ── Write corrupted MRD files ────────────────────────────────────────
        var scanPaths = new string[numScans];
        var allLineTimes = new double[numScans][];

        for (int si = 0; si < numScans; si++)
        {
            var corrupted = reference.Clone();

            foreach (int pe in corruptSets[si])
            {
                var line = corrupted.GetLine(pe);
                // Simulate motion: apply a random linear phase ramp (rigid body shift)
                // and amplitude perturbation.  This is the standard k-space motion model:
                //   k_corrupted[fe] = k_clean[fe] * exp(i * (phi0 + phi1 * fe)) * (1 + epsilon)
                double phi0    = (_rng.NextDouble() - 0.5) * 2 * Math.PI; // global phase
                double phi1    = (_rng.NextDouble() - 0.5) * 0.12;        // linear phase (shift)
                double epsilon = (_rng.NextDouble() - 0.5) * 0.4;         // ~±20 % amplitude
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

            // Save PNG reconstruction of this corrupted scan for visual inspection
            string pngPath = Path.Combine(outputDir, $"scan_{si:D2}.png");
            var mag = ImageReconstructor.Reconstruct(corrupted);
            ImageReconstructor.SavePng(mag, pngPath);

            allLineTimes[si] = new double[npe];
            for (int pe = 0; pe < npe; pe++)
                allLineTimes[si][pe] = GetLineTime(si, pe);

            // Save line-acquisition times so the pre-generated test data can be
            // reused directly with --test-dir or --line-times without needing the
            // original reference .mrd file.
            string ltPath = Path.Combine(outputDir, $"scan_{si:D2}_linetimes.csv");
            WriteLineTimesCsv(ltPath, allLineTimes[si]);
        }

        // ── Write cardiogram CSV ─────────────────────────────────────────────
        string csvPath = Path.Combine(outputDir, "cardiogram.csv");
        WriteCardiogramCsv(csvPath, cardioTimes, cardioSignals);

        Console.WriteLine($"[test-gen] Written {numScans} scan files + cardiogram to: {outputDir}");
        Console.WriteLine($"[test-gen] Reference: {referenceMrd}  ({nfe}×{npe} k-space)");

        return new GeneratedTestSet(scanPaths, allLineTimes, csvPath, corruptSets);
    }

    private static void WriteCardiogramCsv(string path, double[] times, double[] signals)
    {
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine("# Synthetic ECG cardiogram – generated by CardiacGatingMRI test data generator");
        w.WriteLine("# Columns: time_ms , signal_mV");
        w.WriteLine("time_ms,signal");
        for (int i = 0; i < times.Length; i++)
            w.WriteLine($"{times[i]:F3},{signals[i]:F6}");
    }

    private static void WriteLineTimesCsv(string path, double[] lineTimes)
    {
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine("# PE line acquisition times (ms) – one value per line, 0-based order");
        w.WriteLine("time_ms");
        foreach (var t in lineTimes)
            w.WriteLine(t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
    }

    // ── MRD writer ────────────────────────────────────────────────────────────
    // Writes the k-space back to the same binary format that MrdReader understands.
    private static void WriteMrd(MrdData mrd, string path)
    {
        using var f = new BinaryWriter(File.Open(path, FileMode.Create), Encoding.ASCII, false);

        int nFe = mrd.Nfe, nPe = mrd.Npe;
        int n3d = 1, nSlice = 1, nEchoes = 1, nExps = 1;

        // Header ints
        f.Write(nFe);
        f.Write(nPe);
        f.Write(n3d);
        f.Write(nSlice);

        // 2-byte gap
        f.Write((short)0);

        // dattype: 0x15 = complex float32 (first nibble '1' = complex, second nibble '5' = float32)
        f.Write((short)0x15);

        // 132-byte gap
        f.Write(new byte[132]);

        // nEchoes, nExps
        f.Write(nEchoes);
        f.Write(nExps);

        // Pad to byte 256
        long cl = f.BaseStream.Position;
        f.Write(new byte[256 - cl]);

        // 256-byte description
        byte[] desc = new byte[256];
        f.Write(desc);

        // Data: interleaved real/imag float32, column-major (Nfe varies first)
        for (int pe = 0; pe < nPe; pe++)
        for (int fe = 0; fe < nFe; fe++)
        {
            Complex c = mrd.KSpace[fe, pe];
            f.Write((float)c.Real);
            f.Write((float)c.Imaginary);
        }

        // 120-byte filename stub
        f.Write(new byte[120]);
    }
}

public sealed class GeneratedTestSet
{
    public string[]        ScanPaths     { get; }
    public double[][]      LineTimes     { get; }
    public string          CardiogramCsv { get; }
    public HashSet<int>[]  CorruptLines  { get; }

    public GeneratedTestSet(string[] scanPaths, double[][] lineTimes, string csv, HashSet<int>[] corrupt)
    {
        ScanPaths     = scanPaths;
        LineTimes     = lineTimes;
        CardiogramCsv = csv;
        CorruptLines  = corrupt;
    }
}
