using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Python.Runtime;

namespace _403DesktopApp
{
    /// <summary>
    /// Manages the Python.NET runtime lifecycle for the desktop application.
    /// Adapted from PythonIntegrationTests/PythonSetup.cs.
    ///
    /// Initialization order for pythonnet 3.0+:
    ///   1. Set Runtime.PythonDLL
    ///   2. Set PYTHONHOME environment variable
    ///   3. Set PythonEngine.PythonHome
    ///   4. Call PythonEngine.Initialize()
    /// </summary>
    public static class PythonSetup
    {
        private static bool _initialized;

        /// <summary>
        /// Path to the PythonScripts directory.
        /// </summary>
        public static string PythonScriptsPath { get; private set; } = string.Empty;

        /// <summary>
        /// Path to the Scan Filter Code directory containing the scan_filter package.
        /// </summary>
        public static string ScanFilterCodePath { get; private set; } = string.Empty;

        /// <summary>
        /// Path to the FinalCode directory containing the cardiac_gating package.
        /// </summary>
        public static string FinalCodePath { get; private set; } = string.Empty;

        /// <summary>
        /// Whether the Python runtime has been initialized.
        /// </summary>
        public static bool IsInitialized => _initialized;

        /// <summary>
        /// Initialize the Python.NET runtime and configure sys.path.
        /// This method is idempotent.
        /// </summary>
        public static void Initialize(string? pythonDll = null, string? pythonHome = null)
        {
            if (_initialized) return;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string repoRoot = FindRepoRoot(baseDir);

            PythonScriptsPath = Path.Combine(repoRoot, "404Repo", "PythonScripts");
            ScanFilterCodePath = Path.Combine(PythonScriptsPath, "Scan Filter Code");
            FinalCodePath = Path.Combine(repoRoot, "404Repo", "FinalCode");

            if (!Directory.Exists(PythonScriptsPath))
            {
                throw new DirectoryNotFoundException(
                    $"PythonScripts directory not found at: {PythonScriptsPath}. " +
                    $"Searched from base directory: {baseDir}");
            }

            var detected = DetectPythonInfo();
            string? resolvedHome = pythonHome ?? detected.Home;
            string? resolvedDll = pythonDll ?? detected.Dll;

            // Step 1: Set Runtime.PythonDLL
            if (!string.IsNullOrEmpty(resolvedDll))
            {
                Runtime.PythonDLL = resolvedDll;
                System.Diagnostics.Debug.WriteLine($"[PythonSetup] PythonDLL set to: {resolvedDll}");
            }

            // Step 2: Set PYTHONHOME environment variable
            if (!string.IsNullOrEmpty(resolvedHome))
            {
                Environment.SetEnvironmentVariable("PYTHONHOME", resolvedHome, EnvironmentVariableTarget.Process);
                System.Diagnostics.Debug.WriteLine($"[PythonSetup] PYTHONHOME env var set to: {resolvedHome}");

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
                    }
                }

                // Step 3: Set PythonEngine.PythonHome
                PythonEngine.PythonHome = resolvedHome;
                System.Diagnostics.Debug.WriteLine($"[PythonSetup] PythonEngine.PythonHome set to: {resolvedHome}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[PythonSetup] WARNING: Could not detect PYTHONHOME.");
            }

            // Step 4: Initialize the Python runtime
            PythonEngine.Initialize();

            // Add script directories to sys.path
            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                sys.path.append(PythonScriptsPath);

                if (Directory.Exists(ScanFilterCodePath))
                {
                    sys.path.append(ScanFilterCodePath);
                }

                if (Directory.Exists(FinalCodePath))
                {
                    sys.path.append(FinalCodePath);
                }
            }

            // Release the GIL so background threads can acquire it via Py.GIL().
            // Without this, Task.Run calls that use Py.GIL() will deadlock because
            // the main thread holds the GIL after PythonEngine.Initialize().
            PythonEngine.BeginAllowThreads();

            _initialized = true;
            System.Diagnostics.Debug.WriteLine($"[PythonSetup] Runtime initialized. Python {PythonEngine.Version}");
            System.Diagnostics.Debug.WriteLine($"[PythonSetup] Scripts path: {PythonScriptsPath}");
            System.Diagnostics.Debug.WriteLine($"[PythonSetup] Scan Filter Code path: {ScanFilterCodePath}");
            System.Diagnostics.Debug.WriteLine($"[PythonSetup] FinalCode path: {FinalCodePath}");
        }

        /// <summary>
        /// Shut down the Python.NET runtime.
        /// </summary>
        public static void Shutdown()
        {
            if (!_initialized) return;
            PythonEngine.Shutdown();
            _initialized = false;
            System.Diagnostics.Debug.WriteLine("[PythonSetup] Runtime shut down.");
        }

        private static (string? Home, string? Dll) DetectPythonInfo()
        {
            string? envHome = Environment.GetEnvironmentVariable("PYTHONHOME");
            if (!string.IsNullOrEmpty(envHome) && Directory.Exists(envHome))
            {
                string? dll = FindPythonDllInHome(envHome);
                return (envHome, dll);
            }

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
                        Arguments = "-c \"import sys; print(sys.prefix); print(sys.version_info.major); print(sys.version_info.minor)\"",
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

                    if (!Directory.Exists(prefix)) continue;

                    System.Diagnostics.Debug.WriteLine($"[PythonSetup] Auto-detected via '{exe}': Python {major}.{minor} at {prefix}");

                    string? dllPath = null;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        string dllName = $"python{major}{minor}.dll";
                        string fullPath = Path.Combine(prefix, dllName);
                        dllPath = File.Exists(fullPath) ? fullPath : dllName;
                    }
                    else
                    {
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
                    // Candidate not available on PATH
                }
            }

            return (null, null);
        }

        private static string? FindPythonDllInHome(string home)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    foreach (string file in Directory.GetFiles(home, "python*.dll"))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (name.Length >= 8 && name.StartsWith("python") &&
                            char.IsDigit(name[6]) && char.IsDigit(name[^1]))
                        {
                            return file;
                        }
                    }
                }
                catch { }
            }
            else
            {
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
                    catch { }
                }
            }

            return null;
        }

        private static string FindRepoRoot(string startPath)
        {
            string? dir = startPath;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "404Repo")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

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
}
