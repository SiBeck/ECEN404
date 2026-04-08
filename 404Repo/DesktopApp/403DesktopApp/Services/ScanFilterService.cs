using System.IO;
using CardiacGatingMRI;

namespace _403DesktopApp
{
    public class ScanFilterResult
    {
        public bool Success { get; set; }
        public int TotalLines { get; set; }
        public int CleanBaseLines { get; set; }
        public int ReplacedLines { get; set; }
        public int UnfixableLines { get; set; }
        public string OutputDir { get; set; } = "";
        public string PngPath { get; set; } = "";
        public string DicomPath { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public class ScanFilterService
    {
        /// <summary>
        /// Run the scan filter pipeline using native C# cardiac gating.
        /// Replaces the previous Python-based implementation.
        /// </summary>
        public Task<ScanFilterResult> RunFilterAsync(
            string csvPath,
            string[] mrdPaths,
            string outputDir,
            string[]? lineTimesPaths = null,
            int baseScanIndex = 0,
            double sensitivity = 0.25)
        {
            return Task.Run(() =>
            {
                try
                {
                    // Validate inputs
                    if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
                        return new ScanFilterResult
                        {
                            Success = false,
                            Error = $"Cardiogram CSV not found: {csvPath}"
                        };

                    if (mrdPaths == null || mrdPaths.Length == 0)
                        return new ScanFilterResult
                        {
                            Success = false,
                            Error = "No MRD scan files provided."
                        };

                    foreach (var path in mrdPaths)
                    {
                        if (!File.Exists(path))
                            return new ScanFilterResult
                            {
                                Success = false,
                                Error = $"MRD file not found: {path}"
                            };
                    }

                    if (baseScanIndex < 0 || baseScanIndex >= mrdPaths.Length)
                        return new ScanFilterResult
                        {
                            Success = false,
                            Error = $"Base scan index {baseScanIndex} is out of range [0, {mrdPaths.Length - 1}]."
                        };

                    // Load scans
                    var reader = new MrdReader();
                    var scans = new List<ScanRecord>();

                    for (int i = 0; i < mrdPaths.Length; i++)
                    {
                        var mrd = reader.Read(mrdPaths[i]);
                        double[] lineTimes;

                        if (lineTimesPaths != null && i < lineTimesPaths.Length
                            && !string.IsNullOrEmpty(lineTimesPaths[i])
                            && File.Exists(lineTimesPaths[i]))
                        {
                            lineTimes = LoadLineTimes(lineTimesPaths[i], mrd.Npe);
                        }
                        else
                        {
                            // Assign evenly-spaced timestamps: 8 ms per PE line
                            double scanStart = i * mrd.Npe * 8.0;
                            lineTimes = Enumerable.Range(0, mrd.Npe)
                                .Select(pe => scanStart + pe * 8.0)
                                .ToArray();
                        }

                        scans.Add(new ScanRecord(
                            mrd, lineTimes, Path.GetFileNameWithoutExtension(mrdPaths[i])));
                    }

                    // Load cardiogram
                    var cardiogram = new CardiogramReader().Read(csvPath);

                    // Run retrospective cardiac gating
                    var engine = new GatingEngine(new MotionClassifier(sensitivity));
                    var gatingResult = engine.Gate(scans, cardiogram, baseScanIndex: baseScanIndex);

                    // Reconstruct image and save outputs
                    Directory.CreateDirectory(outputDir);
                    var magnitude = ImageReconstructor.Reconstruct(gatingResult.GatedKSpace);

                    string pngPath = Path.Combine(outputDir, "gated.png");
                    string dcmPath = Path.Combine(outputDir, "gated.dcm");

                    ImageReconstructor.SavePng(magnitude, pngPath);
                    new DicomWriter().Write(magnitude, dcmPath);

                    return new ScanFilterResult
                    {
                        Success = true,
                        TotalLines = gatingResult.TotalLines,
                        CleanBaseLines = gatingResult.CleanBaseLines,
                        ReplacedLines = gatingResult.ReplacedLines,
                        UnfixableLines = gatingResult.UnfixableLines,
                        OutputDir = outputDir,
                        PngPath = pngPath,
                        DicomPath = dcmPath,
                    };
                }
                catch (Exception ex)
                {
                    return new ScanFilterResult
                    {
                        Success = false,
                        Error = ex.Message,
                    };
                }
            });
        }

        private static double[] LoadLineTimes(string csvPath, int npe)
        {
            var rows = File.ReadLines(csvPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                .ToArray();

            double[] result = new double[npe];
            int filled = 0;
            foreach (var row in rows)
            {
                if (filled >= npe) break;
                var parts = row.Split(',', StringSplitOptions.TrimEntries);
                string timePart = parts.Length >= 2 ? parts[1] : parts[0];
                if (!double.TryParse(timePart,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double t))
                    continue;
                result[filled++] = t;
            }
            return result;
        }
    }
}
