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
///
/// Initialization order matters for pythonnet 3.0+:
///   1. Set Runtime.PythonDLL (so the native library is loadable)
///   2. Set PYTHONHOME environment variable (belt-and-suspenders fallback)
///   3. Set PythonEngine.PythonHome (calls Py_SetPythonHome via loaded DLL)
///   4. Call PythonEngine.Initialize()
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
    /// Optional path to the Python shared library (e.g., "python39.dll" on Windows,
    /// "libpython3.11.so" on Linux). If null, auto-detection is attempted.
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

        // Auto-detect Python home and DLL if not provided.
        // Detection queries an external Python interpreter on PATH.
        var detected = DetectPythonInfo();
        string? resolvedHome = pythonHome ?? detected.Home;
        string? resolvedDll = pythonDll ?? detected.Dll;

        // Fallback: if we have the DLL path but not the home, derive the home
        // from the DLL location. On Windows, pythonXY.dll lives in the Python
        // installation root (e.g., C:\...\Python312\python312.dll).
        // On Linux, libpythonX.Y.so may be in a lib/ subdirectory.
        if (string.IsNullOrEmpty(resolvedHome) && !string.IsNullOrEmpty(resolvedDll))
        {
            string? dllDir = Path.GetDirectoryName(Path.GetFullPath(resolvedDll));
            if (dllDir != null)
            {
                // On Linux, the .so is often in <prefix>/lib/, so go up one level.
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                    Path.GetFileName(dllDir).Equals("lib", StringComparison.OrdinalIgnoreCase))
                {
                    dllDir = Path.GetDirectoryName(dllDir);
                }

                // Validate: a real Python home contains a Lib (Windows) or lib/pythonX.Y (Linux) directory
                if (dllDir != null && (
                    Directory.Exists(Path.Combine(dllDir, "Lib", "encodings")) ||
                    Directory.Exists(Path.Combine(dllDir, "lib")) ||
                    Directory.Exists(dllDir)))
                {
                    resolvedHome = dllDir;
                    Console.WriteLine($"[PythonSetup] Derived PythonHome from DLL path: {resolvedHome}");
                }
            }
        }


        // --- Step 1: Set Runtime.PythonDLL FIRST ---
        // The PythonHome setter calls TryUsingDll() internally, which requires
        // the native Python library to already be locatable. Without this,
        // the Py_SetPythonHome call inside the PythonHome setter fails silently.
        if (!string.IsNullOrEmpty(resolvedDll))
        {
            Runtime.PythonDLL = resolvedDll;
            Console.WriteLine($"[PythonSetup] PythonDLL set to: {resolvedDll}");
        }

        // --- Step 2: Set PYTHONHOME environment variable ---
        // This is a belt-and-suspenders fallback. CPython reads this env var
        // directly during Py_Initialize() to construct absolute paths for
        // sys.path (Lib/, DLLs/, etc.) instead of using relative paths from
        // the .NET output directory. Without this, embedded Python fails with
        // "No module named 'encodings'" because .\lib resolves to bin/Debug/net8.0/lib.
        if (!string.IsNullOrEmpty(resolvedHome))
        {
            Environment.SetEnvironmentVariable("PYTHONHOME", resolvedHome, EnvironmentVariableTarget.Process);
            Console.WriteLine($"[PythonSetup] PYTHONHOME env var set to: {resolvedHome}");

            // Also ensure the Python installation is on PATH so that dependent
            // native DLLs (e.g., vcruntime140.dll) can be found by the loader.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                string libraryBin = Path.Combine(resolvedHome, "Library", "bin");
                if (!currentPath.Contains(resolvedHome, StringComparison.OrdinalIgnoreCase))
                {
                    string additions = resolvedHome;
                    if (Directory.Exists(libraryBin))
                        additions += ";" + libraryBin;
                    Environment.SetEnvironmentVariable("PATH", additions + ";" + currentPath,
                        EnvironmentVariableTarget.Process);
                    Console.WriteLine($"[PythonSetup] Added Python directories to PATH");
                }
            }

            // --- Step 3: Set PythonEngine.PythonHome ---
            // Now that Runtime.PythonDLL is set, this setter can successfully call
            // Py_SetPythonHome via the loaded native library.
            PythonEngine.PythonHome = resolvedHome;
            Console.WriteLine($"[PythonSetup] PythonEngine.PythonHome set to: {resolvedHome}");
        }
        else
        {
            Console.WriteLine("[PythonSetup] WARNING: Could not detect PYTHONHOME. " +
                "If initialization fails with 'No module named encodings', " +
                "set the PYTHONNET_PYHOME environment variable to your Python installation path " +
                "(e.g., C:\\Users\\you\\AppData\\Local\\Programs\\Python\\Python312).");
        }

        // --- Step 4: Initialize the Python runtime ---
        PythonEngine.Initialize();

        // Add our script directories to Python's sys.path so modules can be imported.
        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.append(PythonScriptsPath);
            sys.path.append(DummyCodePath);

            // Add Scan Filter Code directory so the scan_filter package is importable.
            string scanFilterCodePath = Path.Combine(PythonScriptsPath, "Scan Filter Code");
            if (Directory.Exists(scanFilterCodePath))
            {
                sys.path.append(scanFilterCodePath);
                Console.WriteLine($"[PythonSetup] Scan Filter Code path: {scanFilterCodePath}");
            }
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
    /// Query an external Python interpreter to detect the installation directory
    /// (prefix), version, and shared library path. This works even when embedding
    /// fails because we spawn a separate process with its own correctly-configured runtime.
    /// </summary>
    private static (string? Home, string? Dll) DetectPythonInfo()
    {
        // 1. Check if PYTHONHOME is already set in the environment.
        string? envHome = Environment.GetEnvironmentVariable("PYTHONHOME");
        if (!string.IsNullOrEmpty(envHome) && Directory.Exists(envHome))
        {
            // Try to derive the DLL name from PYTHONHOME by scanning for pythonXY.dll / libpythonX.Y.so
            string? dll = FindPythonDllInHome(envHome);
            return (envHome, dll);
        }

        // 2. On Windows, check well-known standalone Python install locations FIRST,
        //    before falling back to PATH (which may find Anaconda instead).
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var wellKnown = FindStandalonePython();
            if (wellKnown.Home != null)
                return wellKnown;
        }

        // 3. Ask an external Python interpreter for prefix, version major, and version minor.
        //    Skip Anaconda/Miniconda if a standalone Python is preferred.
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
                    Arguments = "-c \"import sys; print(sys.prefix); print(sys.version_info.major); print(sys.version_info.minor); print(sys.executable)\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process == null) continue;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) continue;

                string[] lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 3) continue;

                string prefix = lines[0].Trim();
                string major = lines[1].Trim();
                string minor = lines[2].Trim();
                string exePath = lines.Length >= 4 ? lines[3].Trim() : "";

                if (!Directory.Exists(prefix)) continue;

                // Skip Anaconda/Miniconda installations — prefer standalone Python.
                if (prefix.Contains("anaconda", StringComparison.OrdinalIgnoreCase) ||
                    prefix.Contains("miniconda", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[PythonSetup] Skipping Anaconda/Miniconda at {prefix} via '{exe}'");
                    continue;
                }

                // Skip the Windows Store stub (WindowsApps) — it's not a real install.
                if (exePath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[PythonSetup] Skipping Windows Store stub via '{exe}'");
                    continue;
                }

                Console.WriteLine($"[PythonSetup] Auto-detected via '{exe}': Python {major}.{minor} at {prefix}");

                // Derive the shared library path.
                string? dllPath = null;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows: pythonXY.dll in the prefix directory
                    string dllName = $"python{major}{minor}.dll";
                    string fullPath = Path.Combine(prefix, dllName);
                    dllPath = File.Exists(fullPath) ? fullPath : dllName;
                }
                else
                {
                    // Linux/macOS: search common locations for libpythonX.Y.so
                    string soName = $"libpython{major}.{minor}.so";
                    string[] searchPaths = new[]
                    {
                        Path.Combine(prefix, "lib", soName),
                        Path.Combine(prefix, "lib", $"libpython{major}.{minor}.so.1.0"),
                        $"/usr/lib/x86_64-linux-gnu/{soName}",
                        $"/usr/lib64/{soName}",
                        $"/usr/lib/{soName}",
                    };
                    dllPath = searchPaths.FirstOrDefault(File.Exists) ?? soName;
                }

                return (prefix, dllPath);
            }
            catch
            {
                // This candidate isn't available on PATH — try the next one.
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Search well-known Windows installation directories for a standalone Python install.
    /// Prefers newer versions (3.12, 3.11, 3.10, ...) and skips Anaconda/Miniconda.
    /// </summary>
    private static (string? Home, string? Dll) FindStandalonePython()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Search these directories for Python installations.
        // Each entry can be either a parent (containing PythonXYZ subfolders)
        // or a direct Python home directory.
        string[] searchDirs = new[]
        {
            Path.Combine(localAppData, "Programs", "Python"),  // Default per-user install
            Path.Combine(localAppData, "Python"),               // Alternative per-user install
            @"C:\Python",                                        // Legacy installs
            @"C:\Program Files\Python",                          // All-users install
            @"C:\Program Files (x86)\Python",
        };

        // Prefer newer versions first
        int[] minors = { 12, 13, 11, 10, 9, 8 };

        foreach (string baseDir in searchDirs)
        {
            if (!Directory.Exists(baseDir))
                continue;

            foreach (int minor in minors)
            {
                // Check subdirectory layout: baseDir/Python3XX/python3XX.dll
                string subDirHome = Path.Combine(baseDir, $"Python3{minor}");
                // Check flat layout: baseDir3XX (e.g., C:\Python312)
                string flatHome = $"{baseDir}3{minor}";

                foreach (string home in new[] { subDirHome, flatHome, baseDir })
                {
                    if (!Directory.Exists(home))
                        continue;

                    string dllPath = Path.Combine(home, $"python3{minor}.dll");
                    if (File.Exists(dllPath))
                    {
                        Console.WriteLine($"[PythonSetup] Found standalone Python 3.{minor} at {home}");
                        return (home, dllPath);
                    }
                }
            }

            // Also scan baseDir directly for any pythonXY.dll
            string? foundDll = FindPythonDllInHome(baseDir);
            if (foundDll != null)
            {
                Console.WriteLine($"[PythonSetup] Found standalone Python at {baseDir} ({Path.GetFileName(foundDll)})");
                return (baseDir, foundDll);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Given a Python home directory, try to find the pythonXY.dll or libpythonX.Y.so file.
    /// </summary>
    private static string? FindPythonDllInHome(string home)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Look for pythonXY.dll in the home directory (e.g., python39.dll, python311.dll)
            try
            {
                foreach (string file in Directory.GetFiles(home, "python*.dll"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    // Match pattern: pythonXY where X and Y are digits
                    if (name.Length >= 8 && name.StartsWith("python") &&
                        char.IsDigit(name[6]) && char.IsDigit(name[^1]))
                    {
                        return file;
                    }
                }
            }
            catch { /* Permission or access error */ }
        }
        else
        {
            // Look for libpythonX.Y.so in lib/
            string libDir = Path.Combine(home, "lib");
            if (Directory.Exists(libDir))
            {
                try
                {
                    foreach (string file in Directory.GetFiles(libDir, "libpython*.so"))
                    {
                        return file;
                    }
                }
                catch { /* Permission or access error */ }
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
