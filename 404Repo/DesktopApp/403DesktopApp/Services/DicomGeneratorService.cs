using System;
using System.Collections.Generic;
using System.IO;
using Python.Runtime;

namespace _403DesktopApp
{
    /// <summary>
    /// Calls the Python generate_dicom module via Python.NET to produce
    /// sample DICOM files that can be opened by the Image Viewer.
    ///
    /// Prerequisites on the host machine:
    ///   - Python 3.9+ installed and on PATH (or set PythonDll explicitly)
    ///   - pip packages: pydicom, numpy
    /// </summary>
    public class DicomGeneratorService
    {
        private static bool _engineInitialized;
        private static readonly object _lock = new();

        /// <summary>
        /// Initialize the Python engine. Safe to call multiple times;
        /// only the first call performs actual initialization.
        /// </summary>
        /// <param name="pythonDll">
        /// Optional path to the Python shared library (e.g. "python311.dll" or
        /// "/usr/lib/libpython3.11.so"). If null, Python.NET auto-detects.
        /// </param>
        public static void InitializePython(string? pythonDll = null)
        {
            lock (_lock)
            {
                if (_engineInitialized) return;

                if (!string.IsNullOrEmpty(pythonDll))
                {
                    Runtime.PythonDLL = pythonDll;
                }

                PythonEngine.Initialize();
                _engineInitialized = true;
            }
        }

        /// <summary>
        /// Shut down the Python engine. Call once on application exit.
        /// </summary>
        public static void ShutdownPython()
        {
            lock (_lock)
            {
                if (!_engineInitialized) return;
                PythonEngine.Shutdown();
                _engineInitialized = false;
            }
        }

        /// <summary>
        /// Generate a single grayscale DICOM file.
        /// </summary>
        public string GenerateGrayscale(string outputDir, int size = 256)
        {
            return CallGenerator("generate_grayscale", outputDir, size);
        }

        /// <summary>
        /// Generate a single RGB DICOM file.
        /// </summary>
        public string GenerateRgb(string outputDir, int size = 256)
        {
            return CallGenerator("generate_rgb", outputDir, size);
        }

        /// <summary>
        /// Generate a single multi-frame DICOM file.
        /// </summary>
        public string GenerateMultiframe(string outputDir, int size = 256, int numFrames = 10)
        {
            EnsureInitialized();

            using (Py.GIL())
            {
                AddScriptDirectory();
                dynamic module = Py.Import("generate_dicom");
                PyObject result = module.generate_multiframe(outputDir, "sample_multiframe.dcm", size, numFrames);
                return result.ToString()!;
            }
        }

        /// <summary>
        /// Generate all three sample DICOM files (grayscale, RGB, multi-frame).
        /// Returns a list of absolute paths to the created files.
        /// </summary>
        public List<string> GenerateAll(string outputDir)
        {
            EnsureInitialized();

            using (Py.GIL())
            {
                AddScriptDirectory();
                dynamic module = Py.Import("generate_dicom");
                PyObject result = module.generate_all(outputDir);

                var paths = new List<string>();
                foreach (PyObject item in result.GetIterator())
                {
                    paths.Add(item.ToString()!);
                    item.Dispose();
                }
                return paths;
            }
        }

        private string CallGenerator(string functionName, string outputDir, int size)
        {
            EnsureInitialized();

            using (Py.GIL())
            {
                AddScriptDirectory();
                dynamic module = Py.Import("generate_dicom");
                PyObject result = module.InvokeMethod(functionName,
                    new PyObject[] { new PyString(outputDir) },
                    null);
                return result.ToString()!;
            }
        }

        private static void EnsureInitialized()
        {
            if (!_engineInitialized)
            {
                InitializePython();
            }
        }

        /// <summary>
        /// Add the Scripts directory (next to the running executable) to
        /// Python's sys.path so that "import generate_dicom" works.
        /// </summary>
        private static void AddScriptDirectory()
        {
            string scriptDir = GetScriptDirectory();

            dynamic sys = Py.Import("sys");
            // Check if already on path to avoid duplicates
            bool found = false;
            foreach (PyObject p in sys.path.GetIterator())
            {
                if (p.ToString() == scriptDir)
                {
                    found = true;
                    p.Dispose();
                    break;
                }
                p.Dispose();
            }

            if (!found)
            {
                sys.path.append(scriptDir);
            }
        }

        private static string GetScriptDirectory()
        {
            // 1. Look next to the running executable (build output copies the script here)
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string candidate = Path.Combine(exeDir, "Scripts");
            if (File.Exists(Path.Combine(candidate, "generate_dicom.py")))
                return candidate;

            // 2. Fallback: repo-root /scripts (for development / debugging)
            string? repoScripts = FindRepoScriptsDir(exeDir);
            if (repoScripts != null)
                return repoScripts;

            throw new FileNotFoundException(
                "Could not locate generate_dicom.py. " +
                "Ensure the Scripts folder is next to the executable or the repo scripts/ directory is accessible.");
        }

        private static string? FindRepoScriptsDir(string startDir)
        {
            // Walk up from exe dir looking for a "scripts" folder containing generate_dicom.py
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "scripts");
                if (File.Exists(Path.Combine(candidate, "generate_dicom.py")))
                    return candidate;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
