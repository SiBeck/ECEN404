using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Python.Runtime;

namespace _403DesktopApp
{
    /// <summary>
    /// Manages the Python.NET runtime lifecycle for the desktop application.
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

        public static string PythonScriptsPath { get; private set; } = string.Empty;
        public static string ScanFilterCodePath { get; private set; } = string.Empty;
        public static string FinalCodePath { get; private set; } = string.Empty;
        public static bool IsInitialized => _initialized;

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

            // Detect the Python installation by querying the real interpreter.
            // This returns the home dir, DLL path, AND the actual site-packages
            // paths where third-party packages (numpy, pydicom, pyyaml) live.
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

            // Add all required directories to sys.path
            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");

                // Inject the real site-packages paths detected from the external
                // Python interpreter. The embedded runtime often does NOT include
                // these, which causes "No module named numpy/yaml/pydicom" errors.
                foreach (string sp in detected.SitePackagesPaths)
                {
                    if (Directory.Exists(sp))
                    {
                        sys.path.append(sp);
                        System.Diagnostics.Debug.WriteLine($"[PythonSetup] Added site-packages: {sp}");
                    }
                }

                sys.path.append(PythonScriptsPath);

                if (Directory.Exists(ScanFilterCodePath))
                    sys.path.append(ScanFilterCodePath);

                if (Directory.Exists(FinalCodePath))
                    sys.path.append(FinalCodePath);

                // Log full sys.path for diagnostics
                System.Diagnostics.Debug.WriteLine("[PythonSetup] Final sys.path:");
                foreach (var p in sys.path)
                    System.Diagnostics.Debug.WriteLine($"  - {p}");
            }

            // Release the GIL so background threads can acquire it via Py.GIL().
            PythonEngine.BeginAllowThreads();

            _initialized = true;
            System.Diagnostics.Debug.WriteLine($"[PythonSetup] Runtime initialized. Python {PythonEngine.Version}");
        }

        public static void Shutdown()
        {
            if (!_initialized) return;
            PythonEngine.Shutdown();
            _initialized = false;
        }

        /// <summary>
        /// Query an external Python interpreter to detect the installation directory,
        /// shared library path, and — critically — the real site-packages paths where
        /// third-party packages are installed.
        /// </summary>
        private static (string? Home, string? Dll, List<string> SitePackagesPaths) DetectPythonInfo()
        {
            string? envHome = Environment.GetEnvironmentVariable("PYTHONHOME");
            if (!string.IsNullOrEmpty(envHome) && Directory.Exists(envHome))
            {
                string? dll = FindPythonDllInHome(envHome);
                var sitePaths = QuerySitePackages(null, envHome);
                return (envHome, dll, sitePaths);
            }

            string[] candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { "python", "python3" }
                : new[] { "python3", "python" };

            foreach (string exe in candidates)
            {
                try
                {
                    // Query prefix, version, executable, AND site-packages in one call.
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "-c \"" +
                            "import sys, site; " +
                            "print(sys.prefix); " +
                            "print(sys.version_info.major); " +
                            "print(sys.version_info.minor); " +
                            "print(sys.executable); " +
                            "sp = site.getsitepackages() if hasattr(site, 'getsitepackages') else []; " +
                            "usp = site.getusersitepackages() if hasattr(site, 'getusersitepackages') else ''; " +
                            "print('|'.join(sp + ([usp] if usp else [])))\"",
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
                    if (lines.Length < 4) continue;

                    string prefix = lines[0].Trim();
                    string major = lines[1].Trim();
                    string minor = lines[2].Trim();
                    string exePath = lines[3].Trim();

                    if (!Directory.Exists(prefix)) continue;

                    // Parse site-packages paths (line 5, pipe-separated)
                    var sitePackages = new List<string>();
                    if (lines.Length >= 5 && !string.IsNullOrWhiteSpace(lines[4]))
                    {
                        foreach (string sp in lines[4].Trim().Split('|', StringSplitOptions.RemoveEmptyEntries))
                        {
                            string trimmed = sp.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                                sitePackages.Add(trimmed);
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[PythonSetup] Auto-detected via '{exe}': Python {major}.{minor} at {prefix}");
                    foreach (string sp in sitePackages)
                        System.Diagnostics.Debug.WriteLine($"[PythonSetup] Detected site-packages: {sp}");

                    // Derive the shared library path
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

                    return (prefix, dllPath, sitePackages);
                }
                catch
                {
                    // Candidate not available on PATH
                }
            }

            return (null, null, new List<string>());
        }

        /// <summary>
        /// Query site-packages paths from a specific Python home by running the interpreter.
        /// </summary>
        private static List<string> QuerySitePackages(string? exe, string home)
        {
            var paths = new List<string>();
            string interpreter = exe ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(home, "python.exe")
                : Path.Combine(home, "bin", "python3"));

            if (!File.Exists(interpreter)) return paths;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = interpreter,
                    Arguments = "-c \"import site; sp = site.getsitepackages() if hasattr(site, 'getsitepackages') else []; usp = site.getusersitepackages() if hasattr(site, 'getusersitepackages') else ''; print('|'.join(sp + ([usp] if usp else [])))\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                // Clear PYTHONHOME so the subprocess uses its own defaults
                psi.Environment["PYTHONHOME"] = "";

                using var process = Process.Start(psi);
                if (process == null) return paths;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) return paths;

                foreach (string sp in output.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = sp.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        paths.Add(trimmed);
                }
            }
            catch { }

            return paths;
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
