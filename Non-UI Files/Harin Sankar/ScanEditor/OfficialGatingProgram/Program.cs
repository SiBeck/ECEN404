using FellowOakDicom;
using Microsoft.Extensions.DependencyInjection;
using OfficialGatingProgram;

new DicomSetupBuilder()
    .RegisterServices(s => s.AddFellowOakDicom())
    .Build();

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    PrintHelp();
    return 0;
}

bool generateTestData = false;
string? referenceScanPath = null;
string? motionCsv = null;
var scanPaths = new List<string>();
var lineTimesCsvs = new List<string>();
string outputDir = "output_object_motion_gating";
string testDataDir = Path.Combine("OfficialGatingProgram", "test_data", "te014_multiscan_case");
double linePeriodMs = 8.0;
double startTimeMs = 0.0;
double scanGapMs = 160.0;
double? stillThreshold = null;
double minStillMs = 40.0;
int baseScanIndex = 0;
int numScans = 8;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--generate-test-data":
            generateTestData = true;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                referenceScanPath = args[++i];
            break;
        case "--scan":
            if (i + 1 < args.Length)
                scanPaths.Add(args[++i]);
            break;
        case "--scans":
            while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                scanPaths.Add(args[++i]);
            break;
        case "--motion":
            if (i + 1 < args.Length)
                motionCsv = args[++i];
            break;
        case "--line-times":
            while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                lineTimesCsvs.Add(args[++i]);
            break;
        case "--line-period-ms":
            if (i + 1 < args.Length)
                linePeriodMs = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--start-time-ms":
            if (i + 1 < args.Length)
                startTimeMs = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--scan-gap-ms":
            if (i + 1 < args.Length)
                scanGapMs = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--still-threshold":
            if (i + 1 < args.Length)
                stillThreshold = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--min-still-ms":
            if (i + 1 < args.Length)
                minStillMs = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--base":
            if (i + 1 < args.Length)
                baseScanIndex = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--num-scans":
            if (i + 1 < args.Length)
                numScans = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--test-output":
            if (i + 1 < args.Length)
                testDataDir = args[++i];
            break;
        case "--output":
            if (i + 1 < args.Length)
                outputDir = args[++i];
            break;
    }
}

if (linePeriodMs <= 0.0)
{
    Console.Error.WriteLine($"ERROR: --line-period-ms must be > 0, got {linePeriodMs}.");
    return 1;
}

if (minStillMs < 0.0)
{
    Console.Error.WriteLine($"ERROR: --min-still-ms must be >= 0, got {minStillMs}.");
    return 1;
}

if (scanGapMs < 0.0)
{
    Console.Error.WriteLine($"ERROR: --scan-gap-ms must be >= 0, got {scanGapMs}.");
    return 1;
}

if (generateTestData)
{
    referenceScanPath ??= Path.Combine("mrd_code (1)", "te014.mrd");

    if (!File.Exists(referenceScanPath))
    {
        Console.Error.WriteLine($"ERROR: Reference MRD file not found: {referenceScanPath}");
        return 1;
    }

    if (numScans < 2)
    {
        Console.Error.WriteLine($"ERROR: --num-scans must be >= 2, got {numScans}.");
        return 1;
    }

    Console.WriteLine("=== OBJECT MOTION GATING - TEST DATA GENERATION ===");
    Console.WriteLine($"Reference MRD : {referenceScanPath}");
    Console.WriteLine($"Output dir    : {Path.GetFullPath(testDataDir)}");
    Console.WriteLine();

    var generator = new MotionTestDataGenerator(seed: 42);
    GeneratedMotionTestSet generated = generator.Generate(
        referenceScanPath,
        testDataDir,
        numScans,
        linePeriodMs,
        scanGapMs,
        motionSamplePeriodMs: 4.0,
        stillThreshold);

    Console.WriteLine($"Generated scans     : {generated.ScanPaths.Length}");
    Console.WriteLine($"Motion CSV          : {generated.MotionCsvPath}");
    Console.WriteLine($"Reference PNG       : {generated.ReferencePngPath}");
    Console.WriteLine("=== DONE ===");
    return 0;
}

if (scanPaths.Count == 0 || string.IsNullOrWhiteSpace(motionCsv))
{
    Console.Error.WriteLine("ERROR: gating mode requires --motion and at least one scan via --scan or --scans.");
    Console.Error.WriteLine("Run with --help for usage.");
    return 1;
}

foreach (string path in scanPaths)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"ERROR: MRD file not found: {path}");
        return 1;
    }
}

if (!File.Exists(motionCsv))
{
    Console.Error.WriteLine($"ERROR: Motion CSV file not found: {motionCsv}");
    return 1;
}

foreach (string path in lineTimesCsvs)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"ERROR: Line-times CSV file not found: {path}");
        return 1;
    }
}

if (lineTimesCsvs.Count > 0 && lineTimesCsvs.Count != scanPaths.Count)
{
    Console.Error.WriteLine("ERROR: --line-times must provide exactly one CSV per scan, or be omitted.");
    return 1;
}

if (baseScanIndex < 0 || baseScanIndex >= scanPaths.Count)
{
    Console.Error.WriteLine($"ERROR: --base index {baseScanIndex} is out of range [0, {scanPaths.Count - 1}].");
    return 1;
}

Console.WriteLine("=== OBJECT MOTION GATING ===");
Console.WriteLine($"Scan count    : {scanPaths.Count}");
Console.WriteLine($"Motion trace  : {motionCsv}");
Console.WriteLine($"Output dir    : {Path.GetFullPath(outputDir)}");
Console.WriteLine();

var mrdReader = new MrdReader();
var scans = new List<MotionScanRecord>();

for (int si = 0; si < scanPaths.Count; si++)
{
    MrdData scan = mrdReader.Read(scanPaths[si]);
    double[] lineTimes = lineTimesCsvs.Count > 0
        ? LoadLineTimes(lineTimesCsvs[si], scan.Npe)
        : Enumerable.Range(0, scan.Npe)
            .Select(pe => startTimeMs + si * (scan.Npe * linePeriodMs + scanGapMs) + pe * linePeriodMs)
            .ToArray();

    scans.Add(new MotionScanRecord(scan, lineTimes, Path.GetFileNameWithoutExtension(scanPaths[si])));
    Console.WriteLine($"Loaded MRD: {scan.FileName} ({scan.Nfe}x{scan.Npe})");
}

var motionReader = new MotionTraceReader();
IReadOnlyList<MotionSample> motion = motionReader.Read(motionCsv);
Console.WriteLine($"Loaded motion samples: {motion.Count}, t=[{motion[0].TimeMs:F1} - {motion[^1].TimeMs:F1}] ms");

var detector = new MotionStillnessDetector(stillThreshold, minStillMs);
MotionAnalysis analysis = detector.Analyze(motion);
Console.WriteLine($"Stillness threshold : {analysis.VelocityThreshold:F6} displacement-units/ms");
Console.WriteLine($"Still segments      : {analysis.StillSegments.Length}");

if (analysis.StillSegments.Length == 0)
    Console.WriteLine("WARNING: No still segments detected. Reconstruction may remain corrupted.");

for (int i = 0; i < analysis.StillSegments.Length; i++)
{
    StillSegment segment = analysis.StillSegments[i];
    Console.WriteLine($"  Segment {i + 1}: {segment.StartTimeMs:F1} ms -> {segment.EndTimeMs:F1} ms ({segment.DurationMs:F1} ms)");
}

Console.WriteLine();
bool[][] acceptedMasks = scans
    .Select(scan => detector.ClassifyLineTimes(analysis, scan.LineTimesMs))
    .ToArray();

MotionGatingResult result = MotionGatingEngine.Gate(scans, acceptedMasks, baseScanIndex);
result.PrintSummary();

Directory.CreateDirectory(outputDir);
float[,] magnitude = ImageReconstructor.Reconstruct(result.GatedKSpace);
string pngPath = Path.Combine(outputDir, "gated.png");
string dcmPath = Path.Combine(outputDir, "gated.dcm");
string summaryPath = Path.Combine(outputDir, "gating_summary.txt");
string lineSourcePath = Path.Combine(outputDir, "line_source_log.csv");

ImageReconstructor.SavePng(magnitude, pngPath);
new DicomWriter().Write(magnitude, dcmPath);
File.WriteAllText(summaryPath, BuildSummary(scanPaths, motionCsv, lineTimesCsvs, analysis, result));
File.WriteAllText(lineSourcePath, BuildLineSourceLog(result));

Console.WriteLine();
Console.WriteLine($"PNG   saved: {Path.GetFullPath(pngPath)}");
Console.WriteLine($"DICOM saved: {Path.GetFullPath(dcmPath)}");
Console.WriteLine($"Report saved: {Path.GetFullPath(summaryPath)}");
Console.WriteLine($"Line log saved: {Path.GetFullPath(lineSourcePath)}");
Console.WriteLine("=== DONE ===");
return 0;

static double[] LoadLineTimes(string csvPath, int npe)
{
    string[] rows = File.ReadLines(csvPath)
        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
        .ToArray();

    double[] result = new double[npe];
    int filled = 0;

    foreach (string row in rows)
    {
        if (filled >= npe)
            break;

        string[] parts = row.Split(',', StringSplitOptions.TrimEntries);
        string timePart = parts.Length >= 2 ? parts[1] : parts[0];

        if (!double.TryParse(timePart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
            continue;

        result[filled++] = value;
    }

    if (filled != npe)
        throw new InvalidDataException($"Line-times CSV provided {filled} valid rows, expected {npe}.");

    return result;
}

static string BuildSummary(
    IReadOnlyList<string> scanPaths,
    string motionCsv,
    IReadOnlyList<string> lineTimesCsvs,
    MotionAnalysis analysis,
    MotionGatingResult result)
{
    var lines = new List<string>
    {
        "Object Motion Gating Summary",
        "============================",
        $"Scan count       : {scanPaths.Count}",
        $"Motion CSV       : {Path.GetFullPath(motionCsv)}",
        $"Line-times mode  : {(lineTimesCsvs.Count == 0 ? "generated" : "per-scan csv")}",
        $"Velocity thresh  : {analysis.VelocityThreshold:F6} displacement-units/ms",
        $"Still segments   : {analysis.StillSegments.Length}",
        $"Base scan index  : {result.BaseScanIndex}",
        $"Accepted lines   : {result.AcceptedLines}",
        $"Replaced lines   : {result.ReplacedLines}",
        $"Unfixable lines  : {result.UnfixableLines}",
        $"Acceptance rate  : {(result.TotalLines == 0 ? 0.0 : 100.0 * result.AcceptedLines / result.TotalLines):F1}%",
        string.Empty,
        "Scan inputs:",
    };

    for (int i = 0; i < scanPaths.Count; i++)
    {
        string lineTimes = lineTimesCsvs.Count == 0 ? "<generated>" : Path.GetFullPath(lineTimesCsvs[i]);
        lines.Add($"  {i}. {Path.GetFullPath(scanPaths[i])} | line times: {lineTimes}");
    }

    lines.Add(string.Empty);
    lines.Add("Detected still segments:");

    if (analysis.StillSegments.Length == 0)
    {
        lines.Add("  <none>");
    }
    else
    {
        for (int i = 0; i < analysis.StillSegments.Length; i++)
        {
            StillSegment segment = analysis.StillSegments[i];
            lines.Add($"  {i + 1}. {segment.StartTimeMs:F1} ms -> {segment.EndTimeMs:F1} ms ({segment.DurationMs:F1} ms)");
        }
    }

    return string.Join(Environment.NewLine, lines) + Environment.NewLine;
}

static string BuildLineSourceLog(MotionGatingResult result)
{
    var lines = new List<string> { "pe_index,scan_index,scan_label,was_replaced,unfixable" };

    foreach (MotionLineSourceInfo info in result.LineSourceLog)
        lines.Add($"{info.PeIndex},{info.ScanIndex},{info.ScanLabel},{info.WasReplaced},{info.Unfixable}");

    return string.Join(Environment.NewLine, lines) + Environment.NewLine;
}

static void PrintHelp()
{
    Console.WriteLine("""
        OfficialGatingProgram - MRD Gating From Displacement Trace
        =======================================================

        MODE A - Generate synthetic multi-scan motion test data:
          OfficialGatingProgram --generate-test-data [reference.mrd]
                             [--num-scans <count>]      (default: 8)
                             [--line-period-ms <ms>]    (default: 8.0)
                             [--scan-gap-ms <ms>]       (default: 160.0)
                             [--still-threshold <displacement_per_ms>]
                             [--test-output <dir>]

        MODE B - Gate a set of sequential scans:
          OfficialGatingProgram --motion <motion.csv>
                             --scans <scan_00.mrd> [scan_01.mrd ...]
                             [--line-times <times_00.csv> [times_01.csv ...]]
                             [--base <index>]           (default: 0)
                             [--line-period-ms <ms>]    (default: 8.0)
                             [--start-time-ms <ms>]     (default: 0.0)
                             [--scan-gap-ms <ms>]       (default: 160.0)
                             [--still-threshold <displacement_per_ms>]
                             [--min-still-ms <ms>]      (default: 40.0)
                             [--output <dir>]           (default: output_object_motion_gating)

        Concept:
          Multiple copies of the same object are assumed to be acquired sequentially.
          Each PE line has its own acquisition time. Lines acquired while the displacement
          trace is moving are treated as corrupted. The gating engine keeps clean base lines
          and replaces corrupted base lines with the same PE line from another scan acquired
          during a still time window.

        Outputs:
          <output>/gated.png           Reconstructed image after line replacement.
          <output>/gated.dcm           DICOM export of the gated image.
          <output>/gating_summary.txt  Summary of still windows and line recovery.
          <output>/line_source_log.csv Source scan used for each PE line.
        """);
}
