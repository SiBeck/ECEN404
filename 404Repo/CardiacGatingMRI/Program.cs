using CardiacGatingMRI;
using Microsoft.Extensions.DependencyInjection;
using FellowOakDicom;

// ── fo-dicom service bootstrap (required in fo-dicom 5.x) ───────────────────
new DicomSetupBuilder()
    .RegisterServices(s => s.AddFellowOakDicom())
    .Build();

// ── Parse arguments ──────────────────────────────────────────────────────────
// Two usage modes:
//
//   MODE A – Test / demo (generate synthetic data from te014.mrd, then gate):
//     CardiacGatingMRI --test <te014.mrd> [--output <dir>]
//
//   MODE B – Production (supply your own scans + cardiogram):
//     CardiacGatingMRI --cardiogram <cardio.csv>
//                      --scans <scan1.mrd> <scan2.mrd> ... <scanN.mrd>
//                      --line-times <times1.csv> <times2.csv> ...   (optional)
//                      [--base <index>]
//                      [--sensitivity <0.25>]
//                      [--output <dir>]
//
//   In MODE B, if --line-times is omitted the program assigns evenly-spaced
//   timestamps starting at t=0 with a default period of 8 ms per line.
//   --base selects which scan is used as the "template" (default: 0).

if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
{
    PrintHelp();
    return 0;
}

// ── Parse flags ──────────────────────────────────────────────────────────────
bool testMode      = false;
string?  testMrd   = null;
string?  testDataDir = null;
string?  cardioCsv = null;
var      scanFiles = new List<string>();
var      timeFiles = new List<string>();
int      baseIdx   = 0;
double   sensitivity = 0.25;
string   outputDir = "output_gating";

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--test":
            testMode = true;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) testMrd = args[++i];
            break;
        case "--test-dir":
            if (i + 1 < args.Length) testDataDir = args[++i];
            break;
        case "--cardiogram":
            if (i + 1 < args.Length) cardioCsv = args[++i];
            break;
        case "--scans":
            while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                scanFiles.Add(args[++i]);
            break;
        case "--line-times":
            while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                timeFiles.Add(args[++i]);
            break;
        case "--base":
            if (i + 1 < args.Length) baseIdx = int.Parse(args[++i]);
            break;
        case "--sensitivity":
            if (i + 1 < args.Length) sensitivity = double.Parse(args[++i],
                                                       System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--output":
            if (i + 1 < args.Length) outputDir = args[++i];
            break;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// MODE A – automated test
// ═════════════════════════════════════════════════════════════════════════════
if (testMode)
{
    if (string.IsNullOrEmpty(testMrd))
    {
        Console.Error.WriteLine("ERROR: --test requires a path to the reference .mrd file (e.g. te014.mrd)");
        return 1;
    }
    if (!File.Exists(testMrd))
    {
        Console.Error.WriteLine($"ERROR: Reference MRD file not found: {testMrd}");
        return 1;
    }

    string testDir = Path.Combine(outputDir, "test_data");
    string gatedDir = Path.Combine(outputDir, "gated");

    Console.WriteLine("=== CARDIAC GATING MRI – TEST MODE ===");
    Console.WriteLine($"Reference MRD : {testMrd}");
    Console.WriteLine($"Test data dir : {testDir}");
    Console.WriteLine($"Gated output  : {gatedDir}");
    Console.WriteLine();

    // ── Step 1: Generate synthetic test data ──────────────────────────────
    Console.WriteLine("[1/5] Generating synthetic corrupted scans + cardiogram ...");
    var gen    = new TestDataGenerator(seed: 42);
    var testSet = gen.Generate(testMrd, testDir, linePeriodMs: 8.0);
    Console.WriteLine();

    // ── Step 2: Load all scans ────────────────────────────────────────────
    Console.WriteLine("[2/5] Loading scans ...");
    var reader  = new MrdReader();
    var scans   = new List<ScanRecord>();
    for (int si = 0; si < testSet.ScanPaths.Length; si++)
    {
        var mrd    = reader.Read(testSet.ScanPaths[si]);
        var record = new ScanRecord(mrd, testSet.LineTimes[si], $"scan_{si:D2}");
        scans.Add(record);
        Console.WriteLine($"  Loaded: {testSet.ScanPaths[si]}  ({mrd.Nfe}×{mrd.Npe})");
    }
    Console.WriteLine();

    // ── Step 3: Load cardiogram ───────────────────────────────────────────
    Console.WriteLine("[3/5] Loading cardiogram ...");
    var cardioReader = new CardiogramReader();
    var cardiogram   = cardioReader.Read(testSet.CardiogramCsv);
    Console.WriteLine($"  {cardiogram.Count} samples, " +
                      $"t=[{cardiogram[0].TimeMs:F1} – {cardiogram[^1].TimeMs:F1}] ms");
    Console.WriteLine();

    // ── Step 4: Run gating ────────────────────────────────────────────────
    Console.WriteLine("[4/5] Running retrospective cardiac gating ...");
    var engine = new GatingEngine(new MotionClassifier(sensitivity));
    var result = engine.Gate(scans, cardiogram, baseScanIndex: 0);
    result.PrintSummary();
    Console.WriteLine();

    // ── Step 5: Reconstruct image → PNG → DICOM ───────────────────────────
    Console.WriteLine("[5/5] Reconstructing gated image ...");
    Directory.CreateDirectory(gatedDir);

    var mag     = ImageReconstructor.Reconstruct(result.GatedKSpace);
    string png  = Path.Combine(gatedDir, "te014_gated.png");
    string dcm  = Path.Combine(gatedDir, "te014_gated.dcm");

    ImageReconstructor.SavePng(mag, png);
    Console.WriteLine($"  PNG  saved: {png}");

    var dicomWriter = new DicomWriter();
    dicomWriter.Write(mag, dcm);
    Console.WriteLine($"  DICOM saved: {dcm}");

    // And reconstruct the reference (clean te014)
    var refData = reader.Read(testMrd);
    var refMag  = ImageReconstructor.Reconstruct(refData);
    string refPng = Path.Combine(gatedDir, "te014_reference.png");
    ImageReconstructor.SavePng(refMag, refPng);
    Console.WriteLine($"  Reference (clean) PNG: {refPng}");

    Console.WriteLine();
    Console.WriteLine("=== DONE ===");
    Console.WriteLine($"Output directory: {Path.GetFullPath(gatedDir)}");
    return 0;
}

// ═════════════════════════════════════════════════════════════════════════════
// MODE C – run with pre-generated test data directory
// ═════════════════════════════════════════════════════════════════════════════
if (testDataDir != null)
{
    if (!Directory.Exists(testDataDir))
    {
        Console.Error.WriteLine($"ERROR: Test data directory not found: {testDataDir}");
        return 1;
    }

    string tdCardioCsv = Path.Combine(testDataDir, "cardiogram.csv");
    if (!File.Exists(tdCardioCsv))
    {
        Console.Error.WriteLine($"ERROR: cardiogram.csv not found in: {testDataDir}");
        return 1;
    }

    var mrdFiles = Directory.GetFiles(testDataDir, "*.mrd")
        .OrderBy(f => f)
        .ToArray();

    if (mrdFiles.Length == 0)
    {
        Console.Error.WriteLine($"ERROR: No .mrd files found in: {testDataDir}");
        return 1;
    }

    string gatedDir = outputDir == "output_gating"
        ? Path.Combine(testDataDir, "..", "gated")
        : outputDir;
    Directory.CreateDirectory(gatedDir);

    Console.WriteLine("=== CARDIAC GATING MRI – TEST DATA MODE ===");
    Console.WriteLine($"Test data dir : {Path.GetFullPath(testDataDir)}");
    Console.WriteLine($"Scans found   : {mrdFiles.Length}");
    Console.WriteLine($"Output        : {Path.GetFullPath(gatedDir)}");
    Console.WriteLine();

    var tdReader = new MrdReader();
    var tdScans  = new List<ScanRecord>();
    for (int si = 0; si < mrdFiles.Length; si++)
    {
        var mrd   = tdReader.Read(mrdFiles[si]);
        string lbl = Path.GetFileNameWithoutExtension(mrdFiles[si]);
        string ltCsv = Path.Combine(testDataDir, $"{lbl}_linetimes.csv");
        double[] lineTimes;
        if (File.Exists(ltCsv))
        {
            lineTimes = LoadLineTimes(ltCsv, mrd.Npe);
            Console.WriteLine($"  Loaded: {Path.GetFileName(mrdFiles[si])}  ({mrd.Nfe}\u00d7{mrd.Npe})  [times from CSV]");
        }
        else
        {
            double scanStart = si * mrd.Npe * 8.0;
            lineTimes = Enumerable.Range(0, mrd.Npe).Select(pe => scanStart + pe * 8.0).ToArray();
            Console.WriteLine($"  Loaded: {Path.GetFileName(mrdFiles[si])}  ({mrd.Nfe}\u00d7{mrd.Npe})  [times: default 8 ms/line]");
        }
        tdScans.Add(new ScanRecord(mrd, lineTimes, lbl));
    }
    Console.WriteLine();

    if (baseIdx < 0 || baseIdx >= tdScans.Count)
    {
        Console.Error.WriteLine($"ERROR: --base index {baseIdx} is out of range [0, {tdScans.Count - 1}].");
        return 1;
    }

    var tdCardio = new CardiogramReader().Read(tdCardioCsv);
    Console.WriteLine($"Cardiogram: {tdCardio.Count} samples, " +
                      $"t=[{tdCardio[0].TimeMs:F1} – {tdCardio[^1].TimeMs:F1}] ms");
    Console.WriteLine();

    Console.WriteLine("Running retrospective cardiac gating ...");
    var tdEngine = new GatingEngine(new MotionClassifier(sensitivity));
    var tdResult = tdEngine.Gate(tdScans, tdCardio, baseScanIndex: baseIdx);
    tdResult.PrintSummary();
    Console.WriteLine();

    var tdMag    = ImageReconstructor.Reconstruct(tdResult.GatedKSpace);
    string tdPng = Path.Combine(gatedDir, "gated.png");
    string tdDcm = Path.Combine(gatedDir, "gated.dcm");
    ImageReconstructor.SavePng(tdMag, tdPng);
    new DicomWriter().Write(tdMag, tdDcm);
    Console.WriteLine($"PNG   saved : {Path.GetFullPath(tdPng)}");
    Console.WriteLine($"DICOM saved : {Path.GetFullPath(tdDcm)}");
    Console.WriteLine("=== DONE ===");
    return 0;
}

// ═════════════════════════════════════════════════════════════════════════════
// MODE B – production
// ═════════════════════════════════════════════════════════════════════════════
if (string.IsNullOrEmpty(cardioCsv) || scanFiles.Count == 0)
{
    Console.Error.WriteLine("ERROR: Production mode requires --cardiogram and --scans.");
    Console.Error.WriteLine("Run with --help for usage.");
    return 1;
}

if (!File.Exists(cardioCsv))
{
    Console.Error.WriteLine($"ERROR: Cardiogram file not found: {cardioCsv}");
    return 1;
}

foreach (var f in scanFiles)
{
    if (!File.Exists(f))
    {
        Console.Error.WriteLine($"ERROR: Scan file not found: {f}");
        return 1;
    }
}

if (timeFiles.Count > 0 && timeFiles.Count != scanFiles.Count)
{
    Console.Error.WriteLine("ERROR: --line-times must provide exactly one CSV per scan (or be omitted).");
    return 1;
}

if (baseIdx < 0 || baseIdx >= scanFiles.Count)
{
    Console.Error.WriteLine($"ERROR: --base index {baseIdx} is out of range [0, {scanFiles.Count - 1}].");
    return 1;
}

if (sensitivity <= 0 || sensitivity > 2.0)
{
    Console.Error.WriteLine($"ERROR: --sensitivity must be in range (0, 2.0], got {sensitivity}.");
    return 1;
}

Console.WriteLine("=== CARDIAC GATING MRI – PRODUCTION MODE ===");

var prodReader = new MrdReader();
var prodScans  = new List<ScanRecord>();

for (int si = 0; si < scanFiles.Count; si++)
{
    var mrd = prodReader.Read(scanFiles[si]);
    double[] lineTimes;

    if (timeFiles.Count > 0)
    {
        // Load per-scan line-time CSV (two columns: pe_index, time_ms — or just time_ms per row)
        lineTimes = LoadLineTimes(timeFiles[si], mrd.Npe);
    }
    else
    {
        // Assign evenly-spaced timestamps: scan i starts after all previous scans
        double scanStart = si * mrd.Npe * 8.0;
        lineTimes = Enumerable.Range(0, mrd.Npe).Select(pe => scanStart + pe * 8.0).ToArray();
    }

    prodScans.Add(new ScanRecord(mrd, lineTimes, Path.GetFileNameWithoutExtension(scanFiles[si])));
    Console.WriteLine($"  Loaded: {scanFiles[si]}  ({mrd.Nfe}×{mrd.Npe})");
}

var prodCardioReader = new CardiogramReader();
var prodCardio = prodCardioReader.Read(cardioCsv);
Console.WriteLine($"  Cardiogram: {prodCardio.Count} samples");

var prodEngine = new GatingEngine(new MotionClassifier(sensitivity));
var prodResult = prodEngine.Gate(prodScans, prodCardio, baseScanIndex: baseIdx);
prodResult.PrintSummary();

Directory.CreateDirectory(outputDir);
var prodMag    = ImageReconstructor.Reconstruct(prodResult.GatedKSpace);
string prodPng = Path.Combine(outputDir, "gated.png");
string prodDcm = Path.Combine(outputDir, "gated.dcm");

ImageReconstructor.SavePng(prodMag, prodPng);
new DicomWriter().Write(prodMag, prodDcm);
Console.WriteLine($"PNG  saved: {prodPng}");
Console.WriteLine($"DICOM saved: {prodDcm}");
Console.WriteLine("=== DONE ===");
return 0;

// ── Helper ────────────────────────────────────────────────────────────────────
static double[] LoadLineTimes(string csvPath, int npe)
{
    var rows = File.ReadLines(csvPath)
        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
        .ToArray();

    double[] result = new double[npe];
    int filled = 0;
    foreach (var row in rows)
    {
        if (filled >= npe) break;
        var parts = row.Split(',', StringSplitOptions.TrimEntries);
        // Accept either one column (time) or two columns (pe_index, time)
        string timePart = parts.Length >= 2 ? parts[1] : parts[0];
        if (!double.TryParse(timePart, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out double t))
            continue; // skip header or non-numeric rows
        result[filled++] = t;
    }
    return result;
}

static void PrintHelp()
{
    Console.WriteLine("""
        CardiacGatingMRI – Retrospective K-Space Cardiac Gating Tool
        ================================================================

        MODE A – Test/demo with synthetic data (requires reference .mrd):
          CardiacGatingMRI --test <te014.mrd> [--output <dir>]

          Generates 10 corrupted scans + a synthetic ECG from the reference .mrd,
          runs the gating algorithm, and outputs PNG + DICOM images for comparison.

        MODE B – Run with pre-generated test data (recommended for sharing):
          CardiacGatingMRI --test-dir <dir> [--output <dir>] [--base <index>] [--sensitivity <value>]

          Loads all *.mrd files + cardiogram.csv from the supplied directory.
          Uses <scan>_linetimes.csv if present, otherwise assumes 8 ms/line spacing.
          Default output: <dir>/../gated/

          Example:
            CardiacGatingMRI --test-dir tests\test_data

        MODE C – Production use with your own data:
          CardiacGatingMRI --cardiogram <cardio.csv>
                           --scans <scan1.mrd> [scan2.mrd ...]
                           [--line-times <times1.csv> [times2.csv ...]]
                           [--base <index>]              (default: 0)
                           [--sensitivity <value>]       (default: 0.25)
                           [--output <dir>]              (default: output_gating)

          --cardiogram  : ECG/cardiogram CSV (columns: time_ms, signal)
          --scans       : One or more .mrd k-space files of the SAME slice
          --line-times  : Optional per-scan CSV with acquisition time (ms) per PE line.
                          If omitted, evenly-spaced times at 8 ms/line are assumed.
          --base        : Index of the scan used as the reconstruction template (0-based)
          --sensitivity : Motion-detection threshold multiplier.
                          Lower = more lines flagged as corrupted. Range: 0.05 – 1.0
          --output      : Output directory for PNG and DICOM files

        Output files:
          <output>/gated.png   – Magnitude image of the gated (motion-free) reconstruction
          <output>/gated.dcm   – DICOM Secondary Capture of the gated image
        """);
}

