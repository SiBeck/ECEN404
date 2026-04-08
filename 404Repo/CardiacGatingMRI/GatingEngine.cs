using System.Numerics;

namespace CardiacGatingMRI;

/// <summary>
/// Represents one scan in the set of repeated acquisitions along with
/// when each phase-encode (PE) line was acquired.
/// </summary>
public sealed class ScanRecord
{
    public MrdData     Data        { get; }
    /// <summary>lineTimesMs[pe] = millisecond timestamp at which PE line pe was acquired.</summary>
    public double[]    LineTimesMs { get; }
    public string      Label       { get; }

    public ScanRecord(MrdData data, double[] lineTimesMs, string label)
    {
        Data        = data;
        LineTimesMs = lineTimesMs;
        Label       = label;
    }
}

/// <summary>
/// Reconstructs a motion-free k-space by retrospective cardiac gating.
///
/// Algorithm (retrospective line-replacement gating):
///   For each phase-encode (PE) line index 0..Npe-1:
///     1. Try the "base" scan first – if that line is clean, use it.
///     2. Otherwise search the remaining scans in order; use the first
///        scan whose copy of that line was acquired during quiescence.
///     3. If no clean copy exists across all scans (very rare edge case),
///        keep the base scan's line and log a warning.
///
/// This mirrors the approach described in:
///   Ehman R L, Felmlee J P. "Adaptive technique for high-definition MR
///   imaging of moving structures." Radiology 1989; 173:255-263.
///   Pipe J G. "Motion correction with PROPELLER MRI." MRM 1999; 42:963-969.
/// </summary>
public sealed class GatingEngine
{
    private readonly MotionClassifier _classifier;

    public GatingEngine(MotionClassifier? classifier = null)
        => _classifier = classifier ?? new MotionClassifier();

    public GatingResult Gate(
        IReadOnlyList<ScanRecord>           scans,
        IReadOnlyList<CardioSample>         cardiogram,
        int                                 baseScanIndex = 0)
    {
        if (scans.Count == 0)
            throw new ArgumentException("At least one scan is required.");

        int npe = scans[0].Data.Npe;
        int nfe = scans[0].Data.Nfe;

        // Validate all scans share the same k-space dimensions
        foreach (var s in scans)
        {
            if (s.Data.Npe != npe || s.Data.Nfe != nfe)
                throw new InvalidOperationException(
                    $"Scan '{s.Label}' has mismatched dimensions ({s.Data.Nfe}x{s.Data.Npe}) " +
                    $"vs base ({nfe}x{npe}).");
        }

        // Classify every line in every scan
        var corruptedMaps = scans
            .Select(s => _classifier.ClassifyLines(cardiogram, s.LineTimesMs))
            .ToArray();

        var output = scans[baseScanIndex].Data.Clone();
        var lineSourceLog = new LineSourceInfo[npe];

        int replacedLines = 0;
        int unfixableLines = 0;

        for (int pe = 0; pe < npe; pe++)
        {
            if (!corruptedMaps[baseScanIndex][pe])
            {
                // Base scan line is clean – keep it
                lineSourceLog[pe] = new LineSourceInfo(pe, baseScanIndex, scans[baseScanIndex].Label, false);
                continue;
            }

            // Search other scans for a clean copy of this PE line
            bool replaced = false;
            for (int si = 0; si < scans.Count; si++)
            {
                if (si == baseScanIndex) continue;
                if (!corruptedMaps[si][pe])
                {
                    output.SetLine(pe, scans[si].Data.GetLine(pe));
                    lineSourceLog[pe] = new LineSourceInfo(pe, si, scans[si].Label, true);
                    replacedLines++;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                // No clean copy found – keep base scan's line as fallback
                lineSourceLog[pe] = new LineSourceInfo(pe, baseScanIndex, scans[baseScanIndex].Label, false, Unfixable: true);
                unfixableLines++;
                Console.Error.WriteLine($"  [WARNING] PE line {pe}: no clean copy found in any scan – keeping base.");
            }
        }

        return new GatingResult(output, lineSourceLog, replacedLines, unfixableLines, baseScanIndex, npe);
    }
}

public sealed record LineSourceInfo(
    int    PeIndex,
    int    ScanIndex,
    string ScanLabel,
    bool   WasReplaced,
    bool   Unfixable = false);

public sealed class GatingResult
{
    public MrdData           GatedKSpace      { get; }
    public LineSourceInfo[]  LineSourceLog    { get; }
    public int               ReplacedLines    { get; }
    public int               UnfixableLines   { get; }
    public int               BaseScanIndex    { get; }
    public int               TotalLines       { get; }
    public int               CleanBaseLines   => TotalLines - ReplacedLines - UnfixableLines;

    public GatingResult(MrdData gated, LineSourceInfo[] log, int replaced, int unfixable, int baseIdx, int total)
    {
        GatedKSpace    = gated;
        LineSourceLog  = log;
        ReplacedLines  = replaced;
        UnfixableLines = unfixable;
        BaseScanIndex  = baseIdx;
        TotalLines     = total;
    }

    public void PrintSummary()
    {
        Console.WriteLine($"  Base scan index   : {BaseScanIndex}");
        Console.WriteLine($"  Total PE lines    : {TotalLines}");
        Console.WriteLine($"  Clean from base   : {CleanBaseLines}");
        Console.WriteLine($"  Replaced (gated)  : {ReplacedLines}");
        Console.WriteLine($"  Unfixable (warned): {UnfixableLines}");
    }
}
