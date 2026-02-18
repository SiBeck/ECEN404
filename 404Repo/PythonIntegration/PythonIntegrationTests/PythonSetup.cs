using System.Diagnostics;
using System.Runtime.InteropServices;
using Python.Runtime;

namespace PythonIntegrationTests;

/// <summary>
/// Manages the Python.NET runtime lifecycle and module path configuration.
///
/// Python.NET requires explicit initialization before any Python code can execute
/// and explicit shutdown when done. This class centralizes that setup so that
/// individual test classes don't need to manage the runtime themselves.
/// </summary>
public static class PythonSetup
{
    private static bool _initialized;

    /// <summary>
    /// Path to the PythonScripts directory containing the Python modules under test.
    /// Resolved relative to the repository root.
    /// </summary>
    public static string PythonScriptsPath { get; private set; } = string.Empty;

    /// <summary>
    /// Path to the "Dummy python code" subdirectory.
    /// </summary>
    public static string DummyCodePath { get; private set; } = string.Empty;

    /// <summary>
    /// Initialize the Python.NET runtime and configure sys.path so that
    /// the PythonScripts modules can be imported.
    ///
    /// This method is idempotent — calling it multiple times has no effect.
    /// </summary>
    /// <param name="pythonDll">
    /// Optional path to the Python shared library (e.g., "python311.dll" on Windows,
    /// "libpython3.11.so" on Linux). If null, Python.NET will auto-detect.
    /// </param>
    /// <param name="pythonHome">
    /// Optional path to the Python installation directory (PYTHONHOME). Required for
    /// embedded Python to locate the standard library (e.g., the 'encodings' module).
    /// If null, auto-detection is attempted.
    /// </param>
    public static void Initialize(string? pythonDll = null, string? pythonHome = null)
    {
        if (_initialized) return;

        // Locate the PythonScripts directory by walking up from the output directory.
        // Typical output: .../PythonIntegration/PythonIntegrationTests/bin/Debug/net8.0/
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string repoRoot = FindRepoRoot(baseDir);

        PythonScriptsPath = Path.Combine(repoRoot, "404Repo", "PythonScripts");
        DummyCodePath = Path.Combine(PythonScriptsPath, "Dummy python code");

        if (!Directory.Exists(PythonScriptsPath))
        {
            throw new DirectoryNotFoundException(
                $"PythonScripts directory not found at: {PythonScriptsPath}. " +
                $"Searched from base directory: {baseDir}");
        }

        // Configure the Python runtime before initialization.
        if (!string.IsNullOrEmpty(pythonDll))
        {
            Runtime.PythonDLL = pythonDll;
        }

        // Set PYTHONHOME so embedded CPython can locate the standard library.
        // Without this, Python resolves sys.path entries as relative paths from
        // the .NET output directory, causing "No module named 'encodings'" errors.
        string? resolvedHome = pythonHome ?? DetectPythonHome();
        if (!string.IsNullOrEmpty(resolvedHome))
        {
            PythonEngine.PythonHome = resolvedHome;
            Console.WriteLine($"[PythonSetup] PythonHome set to: {resolvedHome}");
        }
        else
        {
            Console.WriteLine("[PythonSetup] WARNING: Could not detect PYTHONHOME. " +
                "If initialization fails with 'No module named encodings', " +
                "set the PYTHONNET_PYHOME environment variable to your Python installation path.");
        }

        PythonEngine.Initialize();

        // Add our script directories to Python's sys.path so modules can be imported.
        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.append(PythonScriptsPath);
            sys.path.append(DummyCodePath);
        }

        _initialized = true;
        Console.WriteLine($"[PythonSetup] Runtime initialized. Python {PythonEngine.Version}");
        Console.WriteLine($"[PythonSetup] Scripts path: {PythonScriptsPath}");
        Console.WriteLine($"[PythonSetup] Dummy code path: {DummyCodePath}");
    }

    /// <summary>
    /// Shut down the Python.NET runtime. Call once at application exit.
    /// </summary>
    public static void Shutdown()
    {
        if (!_initialized) return;
        PythonEngine.Shutdown();
        _initialized = false;
        Console.WriteLine("[PythonSetup] Runtime shut down.");
    }

    /// <summary>
    /// Attempt to detect the Python installation directory (PYTHONHOME) by querying
    /// a Python interpreter on PATH. This is needed so that embedded CPython can
    /// locate its standard library (encodings, codecs, etc.).
    /// </summary>
    /// <returns>The Python home path, or null if detection fails.</returns>
    private static string? DetectPythonHome()
    {
        // 1. Check if PYTHONHOME is already set in the environment.
        string? envHome = Environment.GetEnvironmentVariable("PYTHONHOME");
        if (!string.IsNullOrEmpty(envHome) && Directory.Exists(envHome))
            return envHome;

        // 2. Ask an external Python interpreter for sys.prefix.
        //    This works even when embedding fails, because we're spawning a
        //    separate process that has its own correctly-configured runtime.
        string[] candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "python", "python3" }
            : new[] { "python3", "python" };

        foreach (string exe in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-c \"import sys; print(sys.prefix)\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process == null) continue;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output) && Directory.Exists(output))
                {
                    Console.WriteLine($"[PythonSetup] Auto-detected PythonHome via '{exe}': {output}");
                    return output;
                }
            }
            catch
            {
                // This candidate isn't available on PATH — try the next one.
            }
        }

        return null;
    }

    /// <summary>
    /// Walk up the directory tree to find the repository root (the directory
    /// containing "404Repo").
    /// </summary>
    private static string FindRepoRoot(string startPath)
    {
        string? dir = startPath;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "404Repo")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fallback: assume we're running from within the repo.
        // Try relative path from current working directory.
        string cwd = Directory.GetCurrentDirectory();
        dir = cwd;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "404Repo")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Cannot locate repository root (directory containing '404Repo'). " +
            $"Started search from: {startPath}");
    }
}
