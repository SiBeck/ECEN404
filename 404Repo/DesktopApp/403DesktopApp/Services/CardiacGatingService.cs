using System.IO;
using Python.Runtime;

namespace _403DesktopApp
{
    public class CardiacGatingResult
    {
        public bool Success { get; set; }
        public int AcceptedFrames { get; set; }
        public int RejectedFrames { get; set; }
        public int StableCycles { get; set; }
        public double AlignmentOffsetMs { get; set; }
        public string OutputDir { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public class CardiacGatingService
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
        /// Run the cardiac gating pipeline asynchronously on a background thread.
        /// </summary>
        public Task<CardiacGatingResult> RunGatingAsync(
            string csvPath,
            string mrdFile,
            string outputDir,
            string? configPath = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    using (Py.GIL())
                    {
                        dynamic bridge = Py.Import("cardiac_gating_bridge");

                        // Validate inputs first
                        dynamic validation = bridge.validate_inputs(csvPath, mrdFile);
                        bool isValid = (bool)validation["valid"];
                        if (!isValid)
                        {
                            string valError = (string)validation["error"];
                            return new CardiacGatingResult
                            {
                                Success = false,
                                Error = $"Input validation failed: {valError}"
                            };
                        }

                        // Run the cardiac gating pipeline
                        dynamic result = bridge.run_cardiac_gating(
                            csvPath, mrdFile, outputDir, configPath);

                        return new CardiacGatingResult
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
                    return new CardiacGatingResult
                    {
                        Success = false,
                        Error = $"Python error: {ex.Message}"
                    };
                }
                catch (Exception ex)
                {
                    return new CardiacGatingResult
                    {
                        Success = false,
                        Error = $"Error: {ex.Message}"
                    };
                }
            });
        }
    }
}
