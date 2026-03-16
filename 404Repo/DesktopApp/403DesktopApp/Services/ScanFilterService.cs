using System.IO;
using Python.Runtime;

namespace _403DesktopApp
{
    public class ScanFilterResult
    {
        public bool Success { get; set; }
        public int AcceptedFrames { get; set; }
        public int RejectedFrames { get; set; }
        public int StableCycles { get; set; }
        public double AlignmentOffsetMs { get; set; }
        public string OutputDir { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public class ScanFilterService
    {
        private bool _pythonInitialized;

        /// <summary>
        /// Ensure the Python runtime is initialized. Idempotent.
        /// </summary>
        public void EnsurePythonInitialized()
        {
            if (_pythonInitialized) return;
            PythonSetup.Initialize();
            _pythonInitialized = true;
        }

        /// <summary>
        /// Run the scan filter pipeline asynchronously on a background thread.
        /// </summary>
        public Task<ScanFilterResult> RunFilterAsync(
            string csvPath,
            string dicomFolder,
            string outputDir,
            string? configPath = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    using (Py.GIL())
                    {
                        dynamic bridge = Py.Import("scan_filter_bridge");

                        // Validate inputs first
                        dynamic validation = bridge.validate_inputs(csvPath, dicomFolder);
                        bool isValid = (bool)validation["valid"];
                        if (!isValid)
                        {
                            string valError = (string)validation["error"];
                            return new ScanFilterResult
                            {
                                Success = false,
                                Error = $"Input validation failed: {valError}"
                            };
                        }

                        // Run the filter pipeline
                        dynamic result = bridge.run_scan_filter(
                            csvPath, dicomFolder, outputDir, configPath);

                        return new ScanFilterResult
                        {
                            Success = (bool)result["success"],
                            AcceptedFrames = (int)result["accepted_frames"],
                            RejectedFrames = (int)result["rejected_frames"],
                            StableCycles = (int)result["stable_cycles"],
                            AlignmentOffsetMs = (double)result["alignment_offset_ms"],
                            OutputDir = (string)result["output_dir"],
                            Error = (string)result["error"],
                        };
                    }
                }
                catch (PythonException ex)
                {
                    return new ScanFilterResult
                    {
                        Success = false,
                        Error = $"Python error: {ex.Message}"
                    };
                }
                catch (Exception ex)
                {
                    return new ScanFilterResult
                    {
                        Success = false,
                        Error = $"Error: {ex.Message}"
                    };
                }
            });
        }
    }
}
