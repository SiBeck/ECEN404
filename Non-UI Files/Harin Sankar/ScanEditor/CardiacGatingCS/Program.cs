using CardiacGating; // Pipeline types.
using FellowOakDicom; // DICOM service setup.
using Microsoft.Extensions.DependencyInjection; // Service registration helpers.

// FellowOakDicom v5 requires one-time service bootstrap before use.
new DicomSetupBuilder() // Build fo-dicom service locator.
    .RegisterServices(s => s.AddFellowOakDicom()) // Register default DICOM services.
    .Build(); // Finalize registration.

if (args.Length < 2) // Require at least cardiogram + MRD paths.
{
    Console.Error.WriteLine( // Print usage to stderr for invalid invocation.
        "Usage: CardiacGating <cardiogram.csv> <scan.mrd> [--config <path>] [--output <dir>]");
    return 1; // Non-zero exit for argument failure.
}

string cardioCsv  = args[0]; // Positional arg 0: cardiogram CSV.
string mrdFile    = args[1]; // Positional arg 1: MRD scan file.
string configPath = "config/default.yaml"; // Default config path.
string outputDir  = "output/gated_dicom";  // Default output folder.

for (int i = 2; i < args.Length - 1; i++) // Parse optional flag-value pairs.
{
    if      (args[i] == "--config") configPath = args[i + 1]; // Override config path.
    else if (args[i] == "--output") outputDir  = args[i + 1]; // Override output folder.
}

var config   = ScanFilterConfig.Load(configPath); // Load YAML config (or defaults).
var pipeline = new ScanFilterPipeline(config);    // Construct orchestrator.
var result   = pipeline.Run(cardioCsv, mrdFile, outputDir); // Execute full workflow.

Console.WriteLine( // Human-readable execution summary.
    $"\nCardiac gating completed.\n" +
    $"  Accepted frames : {result.AcceptedFrames}\n" +
    $"  Rejected frames : {result.RejectedFrames}\n" +
    $"  Stable cycles   : {result.StableCycles}\n" +
    $"  Alignment offset: {result.AlignmentOffsetMs:F2} ms\n" +
    $"  Output          : {result.OutputDir}");

return 0; // Success exit code.
