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
    /// "libpython3.11.so" on Linux). If null, auto-detection will be attempted.
    /// </param>
    public static void Initialize(string? pythonDll = null)
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
        else
        {
            // Auto-detect the Python shared library if not explicitly provided.
            string? detected = DetectPythonDll();
            if (!string.IsNullOrEmpty(detected))
            {
                Console.WriteLine($"[PythonSetup] Auto-detected Python DLL: {detected}");
                Runtime.PythonDLL = detected;
            }
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
    /// Attempt to locate the Python shared library by querying the Python interpreter
    /// on PATH. Falls back to scanning common installation directories on Windows.
    /// Returns null if detection fails.
    /// </summary>
    private static string? DetectPythonDll()
    {
        // Try asking the Python interpreter directly — works on both Windows and Linux.
        string? fromInterpreter = DetectViaInterpreter();
        if (fromInterpreter != null)
            return fromInterpreter;

        // Fallback: scan common Windows install locations.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ScanWindowsPythonPaths();

        // Fallback: scan common Linux shared library paths.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ScanLinuxPythonPaths();

        return null;
    }

    /// <summary>
    /// Run "python -c ..." to get the prefix and version, then construct the DLL path.
    /// </summary>
    private static string? DetectViaInterpreter()
    {
        // Try "python" first, then "python3" (Linux often only has python3).
        foreach (string cmd in new[] { "python", "python3" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = "-c \"import sys; print(sys.prefix); print(sys.version_info.major); print(sys.version_info.minor)\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var proc = Process.Start(psi);
                if (proc == null) continue;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                if (proc.ExitCode != 0) continue;

                string[] lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 3) continue;

                string prefix = lines[0].Trim();
                string major = lines[1].Trim();
                string minor = lines[2].Trim();

                string dllPath;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // e.g. C:\Python311\python311.dll
                    dllPath = Path.Combine(prefix, $"python{major}{minor}.dll");
                }
                else
                {
                    // e.g. /usr/lib/libpython3.11.so
                    // The shared lib may be in prefix/lib or the system lib dirs.
                    dllPath = Path.Combine(prefix, "lib", $"libpython{major}.{minor}.so");
                    if (!File.Exists(dllPath))
                        dllPath = $"/usr/lib/x86_64-linux-gnu/libpython{major}.{minor}.so";
                    if (!File.Exists(dllPath))
                        dllPath = $"/usr/lib/libpython{major}.{minor}.so";
                }

                if (File.Exists(dllPath))
                    return dllPath;
            }
            catch
            {
                // Command not found or not executable — try next.
            }
        }

        return null;
    }

    /// <summary>
    /// Scan common Windows Python installation directories for the DLL.
    /// Checks the Microsoft Store, standard installer, and user-local paths.
    /// </summary>
    private static string? ScanWindowsPythonPaths()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Search directories where Python is commonly installed on Windows.
        var searchRoots = new List<string>
        {
            Path.Combine(localAppData, "Programs", "Python"),   // Standard installer
            @"C:\Python",                                         // Legacy
            @"C:\Program Files\Python",                           // All-users install
            @"C:\Program Files (x86)\Python",
        };

        // Also check PATH entries directly.
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (string dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = dir.Trim();
                if (trimmed.Contains("Python", StringComparison.OrdinalIgnoreCase))
                    searchRoots.Add(trimmed);
            }
        }

        // Supported versions to search for (newest first).
        string[] versions = { "313", "312", "311", "310", "39", "38" };

        foreach (string root in searchRoots)
        {
            foreach (string ver in versions)
            {
                // Direct match: root\python311.dll
                string direct = Path.Combine(root, $"python{ver}.dll");
                if (File.Exists(direct))
                    return direct;

                // Subdirectory: root\Python311\python311.dll
                string subDir = Path.Combine(root, $"Python{ver}", $"python{ver}.dll");
                if (File.Exists(subDir))
                    return subDir;
            }
        }

        return null;
    }

    /// <summary>
    /// Scan common Linux paths for the Python shared library.
    /// </summary>
    private static string? ScanLinuxPythonPaths()
    {
        string[] versions = { "3.13", "3.12", "3.11", "3.10", "3.9", "3.8" };
        string[] libDirs =
        {
            "/usr/lib",
            "/usr/lib/x86_64-linux-gnu",
            "/usr/lib64",
            "/usr/local/lib",
        };

        foreach (string ver in versions)
        {
            foreach (string libDir in libDirs)
            {
                string path = Path.Combine(libDir, $"libpython{ver}.so");
                if (File.Exists(path))
                    return path;
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
