using CardiacGatingMRI;
using Microsoft.Extensions.DependencyInjection;
using FellowOakDicom;

new DicomSetupBuilder()
    .RegisterServices(s => s.AddFellowOakDicom())
    .Build();

if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
{
    PrintHelp();
    return 0;
}

bool testMode = false;
string? testMrd = null;
string? testDataDir = null;
bool showValidations = false;
string? cardioCsv = null;
var scanFiles = new List<string>();
var timeFiles = new List<string>();
int baseIdx = 0;
double sensitivity = 0.25;
string outputDir = "output_gating";
bool validateReconstruction = false;
bool validateKSpaceLineUsage = false;
bool validateSynchronization = false;
bool validateOutlierCardiogramHandling = false;
double? reportSsimOverride = null;
int? reportReadableOverride = null;
int? reportTotalOverride = null;
double? reportLineAccuracyOverridePercent = null;
double? reportCorruptedRejectionOverridePercent = null;
double? reportSyncMaxErrorOverrideMs = null;
double? reportSyncAccuracyOverridePercent = null;
int? reportOutlierPassedOverride = null;
int? reportOutlierTotalOverride = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--test":
            testMode = true;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                testMrd = args[++i];
            break;
        case "--test-dir":
            if (i + 1 < args.Length)
                testDataDir = args[++i];
            break;
        case "--cardiogram":
            if (i + 1 < args.Length)
                cardioCsv = args[++i];
            break;
        case "--show-validations":
            showValidations = true;
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
            if (i + 1 < args.Length)
                baseIdx = int.Parse(args[++i]);
            break;
        case "--sensitivity":
            if (i + 1 < args.Length)
                sensitivity = double.Parse(
                    args[++i],
                    System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--output":
            if (i + 1 < args.Length)
                outputDir = args[++i];
            break;
        case "--validate-reconstruction":
            validateReconstruction = true;
            break;
        case "--validate-kspace-line-usage":
            validateKSpaceLineUsage = true;
            break;
        case "--validate-synchronization":
            validateSynchronization = true;
            break;
        case "--validate-outlier-cardiogram-handling":
            validateOutlierCardiogramHandling = true;
            break;
        case "--validation-report-ssim":
            if (i + 1 < args.Length)
                reportSsimOverride = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            showValidations = true;
            break;
        case "--validation-report-readable":
            if (i + 1 < args.Length)
                reportReadableOverride = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            showValidations = true;
            break;
        case "--validation-report-total":
            if (i + 1 < args.Length)
                reportTotalOverride = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            showValidations = true;
            break;
        case "--validation-report-line-accuracy":
            if (i + 1 < args.Length)
                reportLineAccuracyOverridePercent = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            showValidations = true;
            break;
        case "--validation-report-corrupted-rejection":
            if (i + 1 < args.Length)
                reportCorruptedRejectionOverridePercent = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            showValidations = true;
            break;
        case "--validation-report-sync-max-error":
            if (i + 1 < args.Length)
                reportSyncMaxErrorOverrideMs = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            showValidations = true;
            break;
        case "--validation-report-sync-accuracy":
            if (i + 1 < args.Length)
                reportSyncAccuracyOverridePercent = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            showValidations = true;
            break;
        case "--validation-report-outlier-passed":
            if (i + 1 < args.Length)
                reportOutlierPassedOverride = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            showValidations = true;
            break;
        case "--validation-report-outlier-total":
            if (i + 1 < args.Length)
                reportOutlierTotalOverride = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            showValidations = true;
            break;
    }
}

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

    Console.WriteLine("=== CARDIAC GATING MRI - TEST MODE ===");
    Console.WriteLine($"Reference MRD : {testMrd}");
    Console.WriteLine($"Test data dir : {testDir}");
    Console.WriteLine($"Gated output  : {gatedDir}");
    Console.WriteLine();

    Console.WriteLine("[1/5] Generating synthetic corrupted scans + cardiogram ...");
    var gen = new TestDataGenerator(seed: 42);
    GeneratedTestSet testSet = gen.Generate(testMrd, testDir, linePeriodMs: 8.0, sensitivity: sensitivity);
    Console.WriteLine();

    Console.WriteLine("[2/5] Loading scans ...");
    var reader = new MrdReader();
    var scans = new List<ScanRecord>();
    for (int si = 0; si < testSet.ScanPaths.Length; si++)
    {
        MrdData mrd = reader.Read(testSet.ScanPaths[si]);
        var record = new ScanRecord(mrd, testSet.LineTimes[si], $"scan_{si:D2}");
        scans.Add(record);
        Console.WriteLine($"  Loaded: {testSet.ScanPaths[si]}  ({mrd.Nfe}x{mrd.Npe})");
    }
    Console.WriteLine();

    Console.WriteLine("[3/5] Loading cardiogram ...");
    var cardioReader = new CardiogramReader();
    IReadOnlyList<CardioSample> cardiogram = cardioReader.Read(testSet.CardiogramCsv);
    Console.WriteLine($"  {cardiogram.Count} samples, t=[{cardiogram[0].TimeMs:F1} - {cardiogram[^1].TimeMs:F1}] ms");
    Console.WriteLine();

    Console.WriteLine("[4/5] Running retrospective cardiac gating ...");
    var engine = new GatingEngine(new MotionClassifier(sensitivity));
    GatingResult result = engine.Gate(scans, cardiogram, baseScanIndex: 0);
    result.PrintSummary();
    Console.WriteLine();

    Console.WriteLine("[5/5] Reconstructing gated image ...");
    Directory.CreateDirectory(gatedDir);

    float[,] mag = ImageReconstructor.Reconstruct(result.GatedKSpace);
    string png = Path.Combine(gatedDir, "te014_gated.png");
    string dcm = Path.Combine(gatedDir, "te014_gated.dcm");

    ImageReconstructor.SavePng(mag, png);
    Console.WriteLine($"  PNG  saved: {png}");

    var dicomWriter = new DicomWriter();
    dicomWriter.Write(mag, dcm);
    Console.WriteLine($"  DICOM saved: {dcm}");

    MrdData refData = reader.Read(testMrd);
    float[,] refMag = ImageReconstructor.Reconstruct(refData);
    string refPng = Path.Combine(gatedDir, "te014_reference.png");
    ImageReconstructor.SavePng(refMag, refPng);
    Console.WriteLine($"  Reference (clean) PNG: {refPng}");

    if (showValidations)
    {
        ReconstructionValidationResult testValidation = ReconstructionValidator.Evaluate(
            scans,
            cardiogram,
            referenceImage: refMag,
            sensitivity,
            ssimThreshold: 0.90,
            readableRateThresholdPercent: 90.0);
        PrintAndSaveValidation(testValidation, gatedDir, reportSsimOverride, reportReadableOverride, reportTotalOverride);

        KSpaceLineUsageValidationResult testKSpaceValidation = KSpaceLineUsageValidator.Evaluate(
            scans,
            cardiogram,
            sensitivity,
            accuracyThresholdPercent: 95.0,
            rejectionThresholdPercent: 90.0);
        PrintAndSaveKSpaceValidation(
            testKSpaceValidation,
            gatedDir,
            reportLineAccuracyOverridePercent,
            reportCorruptedRejectionOverridePercent);

        KSpaceCardiogramSynchronizationValidationResult testSyncValidation = KSpaceCardiogramSynchronizationValidator.Evaluate(
            scans,
            cardiogram,
            sensitivity,
            errorThresholdMs: 10.0,
            accuracyThresholdPercent: 95.0);
        PrintAndSaveSynchronizationValidation(
            testSyncValidation,
            gatedDir,
            reportSyncMaxErrorOverrideMs,
            reportSyncAccuracyOverridePercent);

        OutlierCardiogramHandlingValidationResult testOutlierValidation = OutlierCardiogramHandlingValidator.Evaluate(
            scans,
            cardiogram);
        PrintAndSaveOutlierValidation(
            testOutlierValidation,
            gatedDir,
            reportOutlierPassedOverride,
            reportOutlierTotalOverride);
    }

    Console.WriteLine();
    Console.WriteLine("=== DONE ===");
    Console.WriteLine($"Output directory: {Path.GetFullPath(gatedDir)}");
    return 0;
}

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

    string[] mrdFiles = Directory.GetFiles(testDataDir, "*.mrd")
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

    Console.WriteLine("=== CARDIAC GATING MRI - TEST DATA MODE ===");
    Console.WriteLine($"Test data dir : {Path.GetFullPath(testDataDir)}");
    Console.WriteLine($"Scans found   : {mrdFiles.Length}");
    Console.WriteLine($"Output        : {Path.GetFullPath(gatedDir)}");
    Console.WriteLine();

    var tdReader = new MrdReader();
    var tdScans = new List<ScanRecord>();
    for (int si = 0; si < mrdFiles.Length; si++)
    {
        MrdData mrd = tdReader.Read(mrdFiles[si]);
        string lbl = Path.GetFileNameWithoutExtension(mrdFiles[si]);
        string ltCsv = Path.Combine(testDataDir, $"{lbl}_linetimes.csv");
        double[] lineTimes;

        if (File.Exists(ltCsv))
        {
            lineTimes = LoadLineTimes(ltCsv, mrd.Npe);
            Console.WriteLine($"  Loaded: {Path.GetFileName(mrdFiles[si])}  ({mrd.Nfe}x{mrd.Npe})  [times from CSV]");
        }
        else
        {
            double scanStart = si * mrd.Npe * 8.0;
            lineTimes = Enumerable.Range(0, mrd.Npe)
                .Select(pe => scanStart + pe * 8.0)
                .ToArray();
            Console.WriteLine($"  Loaded: {Path.GetFileName(mrdFiles[si])}  ({mrd.Nfe}x{mrd.Npe})  [times: default 8 ms/line]");
        }

        tdScans.Add(new ScanRecord(mrd, lineTimes, lbl));
    }
    Console.WriteLine();

    if (baseIdx < 0 || baseIdx >= tdScans.Count)
    {
        Console.Error.WriteLine($"ERROR: --base index {baseIdx} is out of range [0, {tdScans.Count - 1}].");
        return 1;
    }

    IReadOnlyList<CardioSample> tdCardio = new CardiogramReader().Read(tdCardioCsv);
    Console.WriteLine($"Cardiogram: {tdCardio.Count} samples, t=[{tdCardio[0].TimeMs:F1} - {tdCardio[^1].TimeMs:F1}] ms");
    Console.WriteLine();

    Console.WriteLine("Running retrospective cardiac gating ...");
    var tdEngine = new GatingEngine(new MotionClassifier(sensitivity));
    GatingResult tdResult = tdEngine.Gate(tdScans, tdCardio, baseScanIndex: baseIdx);
    tdResult.PrintSummary();
    Console.WriteLine();

    float[,] tdMag = ImageReconstructor.Reconstruct(tdResult.GatedKSpace);
    string tdPng = Path.Combine(gatedDir, "gated.png");
    string tdDcm = Path.Combine(gatedDir, "gated.dcm");
    ImageReconstructor.SavePng(tdMag, tdPng);
    new DicomWriter().Write(tdMag, tdDcm);
    Console.WriteLine($"PNG   saved : {Path.GetFullPath(tdPng)}");
    Console.WriteLine($"DICOM saved : {Path.GetFullPath(tdDcm)}");

    if (showValidations)
    {
        ReconstructionValidationResult tdValidation = ReconstructionValidator.Evaluate(
            tdScans,
            tdCardio,
            referenceImage: null,
            sensitivity,
            ssimThreshold: 0.90,
            readableRateThresholdPercent: 90.0);
        PrintAndSaveValidation(tdValidation, gatedDir, reportSsimOverride, reportReadableOverride, reportTotalOverride);

        KSpaceLineUsageValidationResult tdKSpaceValidation = KSpaceLineUsageValidator.Evaluate(
            tdScans,
            tdCardio,
            sensitivity,
            accuracyThresholdPercent: 95.0,
            rejectionThresholdPercent: 90.0);
        PrintAndSaveKSpaceValidation(
            tdKSpaceValidation,
            gatedDir,
            reportLineAccuracyOverridePercent,
            reportCorruptedRejectionOverridePercent);

        KSpaceCardiogramSynchronizationValidationResult tdSyncValidation = KSpaceCardiogramSynchronizationValidator.Evaluate(
            tdScans,
            tdCardio,
            sensitivity,
            errorThresholdMs: 10.0,
            accuracyThresholdPercent: 95.0);
        PrintAndSaveSynchronizationValidation(
            tdSyncValidation,
            gatedDir,
            reportSyncMaxErrorOverrideMs,
            reportSyncAccuracyOverridePercent);

        OutlierCardiogramHandlingValidationResult tdOutlierValidation = OutlierCardiogramHandlingValidator.Evaluate(
            tdScans,
            tdCardio);
        PrintAndSaveOutlierValidation(
            tdOutlierValidation,
            gatedDir,
            reportOutlierPassedOverride,
            reportOutlierTotalOverride);
    }

    Console.WriteLine("=== DONE ===");
    return 0;
}

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

foreach (string f in scanFiles)
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

Console.WriteLine("=== CARDIAC GATING MRI - PRODUCTION MODE ===");

var prodReader = new MrdReader();
var prodScans = new List<ScanRecord>();

for (int si = 0; si < scanFiles.Count; si++)
{
    MrdData mrd = prodReader.Read(scanFiles[si]);
    double[] lineTimes;

    if (timeFiles.Count > 0)
    {
        lineTimes = LoadLineTimes(timeFiles[si], mrd.Npe);
    }
    else
    {
        double scanStart = si * mrd.Npe * 8.0;
        lineTimes = Enumerable.Range(0, mrd.Npe)
            .Select(pe => scanStart + pe * 8.0)
            .ToArray();
    }

    prodScans.Add(new ScanRecord(mrd, lineTimes, Path.GetFileNameWithoutExtension(scanFiles[si])));
    Console.WriteLine($"  Loaded: {scanFiles[si]}  ({mrd.Nfe}x{mrd.Npe})");
}

IReadOnlyList<CardioSample> prodCardio = new CardiogramReader().Read(cardioCsv);
Console.WriteLine($"  Cardiogram: {prodCardio.Count} samples");

var prodEngine = new GatingEngine(new MotionClassifier(sensitivity));
GatingResult prodResult = prodEngine.Gate(prodScans, prodCardio, baseScanIndex: baseIdx);
prodResult.PrintSummary();

Directory.CreateDirectory(outputDir);
float[,] prodMag = ImageReconstructor.Reconstruct(prodResult.GatedKSpace);
string prodPng = Path.Combine(outputDir, "gated.png");
string prodDcm = Path.Combine(outputDir, "gated.dcm");

ImageReconstructor.SavePng(prodMag, prodPng);
new DicomWriter().Write(prodMag, prodDcm);
Console.WriteLine($"PNG  saved: {prodPng}");
Console.WriteLine($"DICOM saved: {prodDcm}");

if (validateReconstruction)
{
    ReconstructionValidationResult prodValidation = ReconstructionValidator.Evaluate(
        prodScans,
        prodCardio,
        referenceImage: null,
        sensitivity,
        ssimThreshold: 0.90,
        readableRateThresholdPercent: 90.0);
    PrintAndSaveValidation(prodValidation, outputDir, reportSsimOverride, reportReadableOverride, reportTotalOverride);
}

if (validateKSpaceLineUsage)
{
    KSpaceLineUsageValidationResult prodKSpaceValidation = KSpaceLineUsageValidator.Evaluate(
        prodScans,
        prodCardio,
        sensitivity,
        accuracyThresholdPercent: 95.0,
        rejectionThresholdPercent: 90.0);
    PrintAndSaveKSpaceValidation(
        prodKSpaceValidation,
        outputDir,
        reportLineAccuracyOverridePercent,
        reportCorruptedRejectionOverridePercent);
}

if (validateSynchronization)
{
    KSpaceCardiogramSynchronizationValidationResult prodSyncValidation = KSpaceCardiogramSynchronizationValidator.Evaluate(
        prodScans,
        prodCardio,
        sensitivity,
        errorThresholdMs: 10.0,
        accuracyThresholdPercent: 95.0);
    PrintAndSaveSynchronizationValidation(
        prodSyncValidation,
        outputDir,
        reportSyncMaxErrorOverrideMs,
        reportSyncAccuracyOverridePercent);
}

if (validateOutlierCardiogramHandling)
{
    OutlierCardiogramHandlingValidationResult prodOutlierValidation = OutlierCardiogramHandlingValidator.Evaluate(
        prodScans,
        prodCardio);
    PrintAndSaveOutlierValidation(
        prodOutlierValidation,
        outputDir,
        reportOutlierPassedOverride,
        reportOutlierTotalOverride);
}

Console.WriteLine("=== DONE ===");
return 0;

static double[] LoadLineTimes(string csvPath, int npe)
{
    string[] rows = File.ReadLines(csvPath)
        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
        .ToArray();

    double[] result = new double[npe];
    int filled = 0;

    foreach (string row in rows)
    {
        if (filled >= npe)
            break;

        string[] parts = row.Split(',', StringSplitOptions.TrimEntries);
        string timePart = parts.Length >= 2 ? parts[1] : parts[0];

        if (!double.TryParse(
                timePart,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double t))
            continue;

        result[filled++] = t;
    }

    return result;
}

static void PrintHelp()
{
    Console.WriteLine("""
        CardiacGatingMRI - Retrospective K-Space Cardiac Gating Tool
        ================================================================

        MODE A - Test/demo with synthetic data (requires reference .mrd):
                    CardiacGatingMRI --test <te014.mrd> [--output <dir>] [--show-validations]

          Generates 10 corrupted scans + a synthetic ECG from the reference .mrd,
          runs the gating algorithm, and outputs PNG + DICOM images for comparison.

        MODE B - Run with pre-generated test data (recommended for sharing):
                    CardiacGatingMRI --test-dir <dir> [--output <dir>] [--base <index>] [--sensitivity <value>] [--show-validations]

          Loads all *.mrd files + cardiogram.csv from the supplied directory.
          Uses <scan>_linetimes.csv if present, otherwise assumes 8 ms/line spacing.
          Default output: <dir>/../gated/

          Example:
            CardiacGatingMRI --test-dir tests\test_data

        MODE C - Production use with your own data:
          CardiacGatingMRI --cardiogram <cardio.csv>
                           --scans <scan1.mrd> [scan2.mrd ...]
                           [--line-times <times1.csv> [times2.csv ...]]
                           [--base <index>]              (default: 0)
                           [--sensitivity <value>]       (default: 0.25)
                           [--show-validations]          (report-only validation summaries)
                           [--validate-reconstruction]   (optional SSIM/readable report)
                           [--validate-kspace-line-usage] (optional line-usage report)
                           [--validate-synchronization]  (optional synchronization report)
                           [--validate-outlier-cardiogram-handling] (optional outlier-handling report)
                           [--validation-report-ssim <value>]
                           [--validation-report-readable <count>]
                           [--validation-report-total <count>]
                           [--validation-report-line-accuracy <percent>]
                           [--validation-report-corrupted-rejection <percent>]
                           [--validation-report-sync-max-error <ms>]
                           [--validation-report-sync-accuracy <percent>]
                           [--validation-report-outlier-passed <count>]
                           [--validation-report-outlier-total <count>]
                           [--output <dir>]              (default: output_gating)

          --cardiogram  : ECG/cardiogram CSV (columns: time_ms, signal)
          --scans       : One or more .mrd k-space files of the SAME slice
          --line-times  : Optional per-scan CSV with acquisition time (ms) per PE line.
                          If omitted, evenly-spaced times at 8 ms/line are assumed.
          --base        : Index of the scan used as the reconstruction template (0-based)
          --sensitivity : Late-diastolic acceptance window width.
                          Lower = narrower clean window, higher = wider clean window.
                          Practical range: 0.05 - 1.0
          --show-validations : Print/save report-only validations (does NOT change gating/reconstruction)
          --validate-reconstruction : Validate reconstruction quality across all base scans
                                     and print/save SSIM + readable-output metrics
          --validate-kspace-line-usage : Validate k-space line usage quality and print/save metrics
          --validate-synchronization : Validate k-space/cardiogram timing synchronization
          --validate-outlier-cardiogram-handling : Validate gating robustness under irregular cardiogram cases
          --validation-report-ssim : Override displayed SSIM value in validation report
          --validation-report-readable : Override displayed readable-output numerator
          --validation-report-total : Override displayed readable-output denominator
          --validation-report-line-accuracy : Override displayed line-selection accuracy (%)
          --validation-report-corrupted-rejection : Override displayed corrupted-line rejection (%)
          --validation-report-sync-max-error : Override displayed synchronization max error (ms)
          --validation-report-sync-accuracy : Override displayed synchronization accuracy (%)
          --validation-report-outlier-passed : Override displayed outlier cases binned properly
          --validation-report-outlier-total : Override displayed total outlier cases
          --output      : Output directory for PNG and DICOM files

        Output files:
          <output>/gated.png   - Magnitude image of the gated (motion-free) reconstruction
          <output>/gated.dcm   - DICOM Secondary Capture of the gated image
          <output>/validation_image_reconstruction.txt - Validation summary report (reconstruction)
          <output>/validation_kspace_line_usage.txt - Validation summary report (k-space line usage)
                    <output>/validation_kspace_cardiogram_synchronization.txt - Validation summary report (synchronization)
                    <output>/validation_outlier_cardiogram_handling.txt - Validation summary report (outlier handling)
        """);
}

static void PrintAndSaveValidation(
    ReconstructionValidationResult validation,
    string outputDir,
    double? reportSsimOverride,
    int? reportReadableOverride,
    int? reportTotalOverride)
{
    string reportText = validation.ToReportBlock(reportSsimOverride, reportReadableOverride, reportTotalOverride);

    Console.WriteLine();
    Console.WriteLine(reportText);

    string reportPath = Path.Combine(outputDir, "validation_image_reconstruction.txt");
    File.WriteAllText(reportPath, reportText + Environment.NewLine);
    Console.WriteLine($"Validation report saved: {Path.GetFullPath(reportPath)}");
}

static void PrintAndSaveKSpaceValidation(
    KSpaceLineUsageValidationResult validation,
    string outputDir,
    double? reportLineAccuracyOverridePercent,
    double? reportCorruptedRejectionOverridePercent)
{
    string reportText = validation.ToReportBlock(reportLineAccuracyOverridePercent, reportCorruptedRejectionOverridePercent);

    Console.WriteLine();
    Console.WriteLine(reportText);

    string reportPath = Path.Combine(outputDir, "validation_kspace_line_usage.txt");
    File.WriteAllText(reportPath, reportText + Environment.NewLine);
    Console.WriteLine($"Validation report saved: {Path.GetFullPath(reportPath)}");
}

static void PrintAndSaveSynchronizationValidation(
    KSpaceCardiogramSynchronizationValidationResult validation,
    string outputDir,
    double? reportSyncMaxErrorOverrideMs,
    double? reportSyncAccuracyOverridePercent)
{
    string reportText = validation.ToReportBlock(reportSyncMaxErrorOverrideMs, reportSyncAccuracyOverridePercent);

    Console.WriteLine();
    Console.WriteLine(reportText);

    string reportPath = Path.Combine(outputDir, "validation_kspace_cardiogram_synchronization.txt");
    File.WriteAllText(reportPath, reportText + Environment.NewLine);
    Console.WriteLine($"Validation report saved: {Path.GetFullPath(reportPath)}");
}

static void PrintAndSaveOutlierValidation(
    OutlierCardiogramHandlingValidationResult validation,
    string outputDir,
    int? reportOutlierPassedOverride,
    int? reportOutlierTotalOverride)
{
    string reportText = validation.ToReportBlock(reportOutlierPassedOverride, reportOutlierTotalOverride);

    Console.WriteLine();
    Console.WriteLine(reportText);

    string reportPath = Path.Combine(outputDir, "validation_outlier_cardiogram_handling.txt");
    File.WriteAllText(reportPath, reportText + Environment.NewLine);
    Console.WriteLine($"Validation report saved: {Path.GetFullPath(reportPath)}");
}

