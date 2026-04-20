namespace OfficialGatingProgram;

public sealed class MotionScanRecord
{
    public MrdData Data { get; }
    public double[] LineTimesMs { get; }
    public string Label { get; }

    public MotionScanRecord(MrdData data, double[] lineTimesMs, string label)
    {
        Data = data;
        LineTimesMs = lineTimesMs;
        Label = label;
    }
}

public sealed record MotionLineSourceInfo(
    int PeIndex,
    int ScanIndex,
    string ScanLabel,
    bool WasReplaced,
    bool Unfixable = false);

public sealed class MotionGatingResult
{
    public MrdData GatedKSpace { get; }
    public MotionLineSourceInfo[] LineSourceLog { get; }
    public int BaseScanIndex { get; }
    public int TotalLines { get; }
    public int AcceptedLines { get; }
    public int ReplacedLines { get; }
    public int UnfixableLines { get; }
    public int CleanBaseLines => TotalLines - ReplacedLines - UnfixableLines;

    public MotionGatingResult(
        MrdData gatedKSpace,
        MotionLineSourceInfo[] lineSourceLog,
        int baseScanIndex,
        int totalLines,
        int acceptedLines,
        int replacedLines,
        int unfixableLines)
    {
        GatedKSpace = gatedKSpace;
        LineSourceLog = lineSourceLog;
        BaseScanIndex = baseScanIndex;
        TotalLines = totalLines;
        AcceptedLines = acceptedLines;
        ReplacedLines = replacedLines;
        UnfixableLines = unfixableLines;
    }

    public void PrintSummary()
    {
        Console.WriteLine($"  Base scan index   : {BaseScanIndex}");
        Console.WriteLine($"  Total PE lines    : {TotalLines}");
        Console.WriteLine($"  Clean from base   : {CleanBaseLines}");
        Console.WriteLine($"  Replaced lines    : {ReplacedLines}");
        Console.WriteLine($"  Unfixable lines   : {UnfixableLines}");
        Console.WriteLine($"  Accepted in final : {AcceptedLines}");
    }
}

public static class MotionGatingEngine
{
    public static MotionGatingResult Gate(
        IReadOnlyList<MotionScanRecord> scans,
        IReadOnlyList<bool[]> acceptedMasks,
        int baseScanIndex)
    {
        if (scans.Count == 0)
            throw new ArgumentException("At least one scan is required.", nameof(scans));

        if (acceptedMasks.Count != scans.Count)
            throw new ArgumentException("Accepted-mask count must match scan count.", nameof(acceptedMasks));

        if (baseScanIndex < 0 || baseScanIndex >= scans.Count)
            throw new ArgumentOutOfRangeException(nameof(baseScanIndex));

        int nfe = scans[0].Data.Nfe;
        int npe = scans[0].Data.Npe;

        for (int i = 0; i < scans.Count; i++)
        {
            if (scans[i].Data.Nfe != nfe || scans[i].Data.Npe != npe)
                throw new InvalidOperationException("All scans must have identical dimensions.");

            if (acceptedMasks[i].Length != npe)
                throw new ArgumentException($"Accepted-mask length {acceptedMasks[i].Length} != Npe {npe}");
        }

        MrdData output = scans[baseScanIndex].Data.Clone();
        var lineSourceLog = new MotionLineSourceInfo[npe];
        int replacedLines = 0;
        int unfixableLines = 0;
        int acceptedLines = 0;

        for (int pe = 0; pe < npe; pe++)
        {
            if (acceptedMasks[baseScanIndex][pe])
            {
                acceptedLines++;
                lineSourceLog[pe] = new MotionLineSourceInfo(pe, baseScanIndex, scans[baseScanIndex].Label, false);
                continue;
            }

            bool replaced = false;
            for (int si = 0; si < scans.Count; si++)
            {
                if (si == baseScanIndex)
                    continue;

                if (!acceptedMasks[si][pe])
                    continue;

                output.SetLine(pe, scans[si].Data.GetLine(pe));
                acceptedLines++;
                replacedLines++;
                replaced = true;
                lineSourceLog[pe] = new MotionLineSourceInfo(pe, si, scans[si].Label, true);
                break;
            }

            if (!replaced)
            {
                unfixableLines++;
                lineSourceLog[pe] = new MotionLineSourceInfo(
                    pe,
                    baseScanIndex,
                    scans[baseScanIndex].Label,
                    false,
                    Unfixable: true);
            }
        }

        return new MotionGatingResult(output, lineSourceLog, baseScanIndex, npe, acceptedLines, replacedLines, unfixableLines);
    }
}
