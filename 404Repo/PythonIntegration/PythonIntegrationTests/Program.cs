using PythonIntegrationTests;

/// <summary>
/// Main entry point for the Python.NET integration test runner.
///
/// This program initializes the Python.NET runtime, runs all test suites
/// against the Python modules in the PythonScripts directory, and reports
/// aggregate results.
///
/// Usage:
///     dotnet run --project PythonIntegrationTests
///
/// Prerequisites:
///     - Python 3.8+ installed and on PATH (or set PYTHONNET_PYDLL env var)
///     - pip install numpy pandas pydicom pyyaml (for scan_filter_bridge tests)
///     - dotnet restore (to pull the pythonnet NuGet package)
/// </summary>

Console.WriteLine("========================================");
Console.WriteLine("  BioMetrix Python.NET Integration Tests");
Console.WriteLine("========================================");
Console.WriteLine();

// Allow overriding the Python DLL path and home directory via environment variables.
string? pythonDll = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
string? pythonHome = Environment.GetEnvironmentVariable("PYTHONNET_PYHOME");

try
{
    PythonSetup.Initialize(pythonDll, pythonHome);
}
catch (Exception ex)
{
    Console.WriteLine($"FATAL: Failed to initialize Python runtime: {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("Troubleshooting:");
    Console.WriteLine("  - Ensure Python 3.8+ is installed and on PATH");
    Console.WriteLine("  - On Windows, set PYTHONNET_PYDLL=python311.dll (or your version)");
    Console.WriteLine("  - On Linux, set PYTHONNET_PYDLL=/usr/lib/libpython3.11.so");
    Console.WriteLine("  - If you see 'No module named encodings', set PYTHONNET_PYHOME to your");
    Console.WriteLine("    Python installation directory (e.g., C:\\Users\\you\\anaconda3 or /usr/lib/python3.11)");
    Console.WriteLine("  - Run: pip install numpy pandas pydicom pyyaml");
    Environment.Exit(1);
    return;
}

var allResults = new List<TestResults>();

// --- Run test suites ---
// Each suite is wrapped in try/catch so that a crash in one doesn't prevent the others from running.

Console.WriteLine();
Console.WriteLine("Running example.py tests...");
try
{
    allResults.Add(ExampleModuleTests.Run());
}
catch (Exception ex)
{
    Console.WriteLine($"  [CRASH] example.py suite threw an unhandled exception: {ex.Message}");
    var crash = new TestResults("example.py");
    crash.RunTest("suite execution", () => { throw new Exception($"Unhandled: {ex.Message}"); });
    allResults.Add(crash);
}

Console.WriteLine();
Console.WriteLine("Running image_viewer.py tests...");
try
{
    allResults.Add(ImageViewerTests.Run());
}
catch (Exception ex)
{
    Console.WriteLine($"  [CRASH] image_viewer.py suite threw an unhandled exception: {ex.Message}");
    var crash = new TestResults("image_viewer.py");
    crash.RunTest("suite execution", () => { throw new Exception($"Unhandled: {ex.Message}"); });
    allResults.Add(crash);
}

Console.WriteLine();
Console.WriteLine("Running scan_filter_bridge.py tests...");
try
{
    allResults.Add(ScanFilterBridgeTests.Run());
}
catch (Exception ex)
{
    Console.WriteLine($"  [CRASH] scan_filter_bridge.py suite threw an unhandled exception: {ex.Message}");
    var crash = new TestResults("scan_filter_bridge.py");
    crash.RunTest("suite execution", () => { throw new Exception($"Unhandled: {ex.Message}"); });
    allResults.Add(crash);
}

// --- Report ---

Console.WriteLine();
Console.WriteLine("========================================");
Console.WriteLine("  Test Summary");
Console.WriteLine("========================================");

foreach (var r in allResults)
    r.PrintSummary();

int totalPassed = allResults.Sum(r => r.Passed);
int totalFailed = allResults.Sum(r => r.Failed);
int totalTests = allResults.Sum(r => r.Total);

Console.WriteLine();
Console.WriteLine($"Overall: {totalTests} tests, {totalPassed} passed, {totalFailed} failed");
Console.WriteLine();

PythonSetup.Shutdown();

// Exit with non-zero code if any tests failed.
Environment.Exit(totalFailed > 0 ? 1 : 0);
