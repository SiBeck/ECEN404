using Python.Runtime;

namespace PythonIntegrationTests;

/// <summary>
/// Integration tests for the scan_filter_bridge.py Python module.
///
/// These tests verify the scan filter bridge functions work correctly through
/// Python.NET, including input validation, end-to-end pipeline execution,
/// error handling, and type marshalling.
///
/// NOTE: These tests require numpy, pandas, pydicom, and pyyaml to be
/// installed in the Python environment. If missing, tests will fail
/// with an import error.
/// </summary>
public static class ScanFilterBridgeTests
{
    public static TestResults Run()
    {
        var results = new TestResults("scan_filter_bridge.py");

        // Track temp directories for cleanup
        var tempDirs = new List<string>();

        using (Py.GIL())
        {
            // --- Import bridge module ---
            dynamic bridge;
            try
            {
                bridge = Py.Import("scan_filter_bridge");
            }
            catch (PythonException ex)
            {
                results.RunTest("import scan_filter_bridge", () =>
                {
                    throw new Exception(
                        $"Cannot import scan_filter_bridge. Ensure numpy, pandas, pydicom, " +
                        $"and pyyaml are installed. Error: {ex.Message}");
                });
                return results;
            }

            results.RunTest("import scan_filter_bridge", () =>
            {
                // If we got here, import succeeded
                Assert.IsNotNull(bridge, "Bridge module should not be null");
            });

            // --- Generate synthetic test data via Python ---
            string testDataDir = Path.Combine(Path.GetTempPath(), "scanfilter_test_" + Guid.NewGuid().ToString("N")[..8]);
            tempDirs.Add(testDataDir);
            string csvPath = "";
            string dicomDir = "";
            bool testDataReady = false;

            results.RunTest("generate synthetic test data", () =>
            {
                // Ensure venv site-packages is on sys.path BEFORE importing
                // create_sample_inputs (which imports pydicom at the top level).
                // Without this, Anaconda's older pydicom gets loaded instead of
                // the venv's compatible version, causing serialization errors.
                bridge._ensure_scan_filter_on_path();

                // Add Scan Filter Code examples to path for create_sample_inputs
                dynamic sys = Py.Import("sys");
                string scanFilterCode = Path.Combine(PythonSetup.PythonScriptsPath, "Scan Filter Code");
                sys.path.append(scanFilterCode);

                dynamic createInputs = Py.Import("examples.create_sample_inputs");

                csvPath = Path.Combine(testDataDir, "cardio.csv");
                dicomDir = Path.Combine(testDataDir, "dicom_series");

                createInputs.generate_cardiogram(csvPath, beats: 18, base_rr_ms: 800.0);
                createInputs.generate_dicom_series(dicomDir, frame_count: 12, start_ms: 100.0, spacing_ms: 200.0, seed: 42);

                Assert.IsTrue(File.Exists(csvPath), "Cardiogram CSV should exist");
                Assert.IsTrue(Directory.Exists(dicomDir), "DICOM directory should exist");

                int dcmCount = Directory.GetFiles(dicomDir, "*.dcm").Length;
                Assert.AreEqual(12, dcmCount, "DICOM file count");

                testDataReady = true;
            });

            // --- validate_inputs tests ---

            results.RunTest("validate_inputs with valid paths", () =>
            {
                if (!testDataReady) throw new Exception("Prerequisite failed: test data not generated");

                dynamic result = bridge.validate_inputs(csvPath, dicomDir);
                Assert.IsTrue((bool)result["valid"], "Should be valid");
                Assert.IsTrue((bool)result["csv_exists"], "CSV should exist");
                Assert.IsTrue((bool)result["dicom_exists"], "DICOM folder should exist");
                Assert.AreEqual(12, (int)result["dicom_file_count"], "DICOM file count");
                Assert.AreEqual("", (string)result["error"], "Error should be empty");
            });

            results.RunTest("validate_inputs with missing CSV", () =>
            {
                if (!testDataReady) throw new Exception("Prerequisite failed: test data not generated");

                dynamic result = bridge.validate_inputs("/nonexistent/missing.csv", dicomDir);
                Assert.IsFalse((bool)result["valid"], "Should be invalid");
                Assert.IsFalse((bool)result["csv_exists"], "CSV should not exist");
                Assert.IsTrue((bool)result["dicom_exists"], "DICOM folder should still exist");

                string error = (string)result["error"];
                Assert.IsTrue(error.Length > 0, "Error message should be non-empty");
            });

            results.RunTest("validate_inputs with missing DICOM folder", () =>
            {
                if (!testDataReady) throw new Exception("Prerequisite failed: test data not generated");

                dynamic result = bridge.validate_inputs(csvPath, "/nonexistent/dicom_dir");
                Assert.IsFalse((bool)result["valid"], "Should be invalid");
                Assert.IsTrue((bool)result["csv_exists"], "CSV should exist");
                Assert.IsFalse((bool)result["dicom_exists"], "DICOM folder should not exist");
            });

            results.RunTest("validate_inputs with empty DICOM folder", () =>
            {
                string emptyDir = Path.Combine(testDataDir, "empty_dicoms");
                Directory.CreateDirectory(emptyDir);
                File.WriteAllText(Path.Combine(emptyDir, "readme.txt"), "no dicoms here");

                dynamic result = bridge.validate_inputs(csvPath, emptyDir);
                Assert.IsFalse((bool)result["valid"], "Should be invalid for empty folder");
                Assert.IsTrue((bool)result["dicom_exists"], "Folder exists");
                Assert.AreEqual(0, (int)result["dicom_file_count"], "No DICOM files");
            });

            // --- run_scan_filter tests ---

            string happyOutputDir = Path.Combine(testDataDir, "filtered_happy");
            tempDirs.Add(happyOutputDir);
            int happyAccepted = 0;

            results.RunTest("run_scan_filter happy path", () =>
            {
                if (!testDataReady) throw new Exception("Prerequisite failed: test data not generated");

                dynamic result = bridge.run_scan_filter(csvPath, dicomDir, happyOutputDir);

                bool success = (bool)result["success"];
                Assert.IsTrue(success, $"Pipeline failed: {result["error"]}");

                happyAccepted = (int)result["accepted_frames"];
                Assert.IsTrue(happyAccepted > 0, "Should accept at least one frame");
                Assert.AreEqual("", (string)result["error"], "Error should be empty");
            });

            results.RunTest("run_scan_filter frame counts sum correctly", () =>
            {
                if (!testDataReady) throw new Exception("Prerequisite failed: test data not generated");

                // Re-run with fresh output to get counts
                string sumDir = Path.Combine(testDataDir, "filtered_sum");
                tempDirs.Add(sumDir);

                dynamic result = bridge.run_scan_filter(csvPath, dicomDir, sumDir);
                Assert.IsTrue((bool)result["success"], $"Pipeline failed: {result["error"]}");

                int accepted = (int)result["accepted_frames"];
                int rejected = (int)result["rejected_frames"];
                Assert.AreEqual(12, accepted + rejected, "Total frames should equal input count");
            });

            results.RunTest("run_scan_filter output dir exists", () =>
            {
                if (happyAccepted == 0) throw new Exception("Prerequisite failed: happy path did not accept frames");

                Assert.IsTrue(Directory.Exists(happyOutputDir),
                    $"Output directory should exist: {happyOutputDir}");
            });

            results.RunTest("run_scan_filter output files match accepted count", () =>
            {
                if (happyAccepted == 0) throw new Exception("Prerequisite failed: happy path did not accept frames");

                int fileCount = Directory.GetFiles(happyOutputDir, "*.dcm").Length;
                Assert.AreEqual(happyAccepted, fileCount,
                    $"Expected {happyAccepted} DICOM files, found {fileCount}");
            });

            results.RunTest("run_scan_filter with invalid CSV returns error", () =>
            {
                string errDir = Path.Combine(testDataDir, "filtered_err1");
                tempDirs.Add(errDir);

                dynamic result = bridge.run_scan_filter("/nonexistent/bad.csv", dicomDir, errDir);
                Assert.IsFalse((bool)result["success"], "Should fail for missing CSV");

                string error = (string)result["error"];
                Assert.IsTrue(error.Length > 0, "Error message should be non-empty");
            });

            results.RunTest("run_scan_filter with empty folder returns error", () =>
            {
                string emptyDir = Path.Combine(testDataDir, "empty_dicoms_run");
                Directory.CreateDirectory(emptyDir);
                string errDir = Path.Combine(testDataDir, "filtered_err2");
                tempDirs.Add(errDir);

                dynamic result = bridge.run_scan_filter(csvPath, emptyDir, errDir);
                Assert.IsFalse((bool)result["success"], "Should fail for empty DICOM folder");

                string error = (string)result["error"];
                Assert.IsTrue(error.Length > 0, "Error message should be non-empty");
            });

            results.RunTest("run_scan_filter type marshalling", () =>
            {
                if (!testDataReady) throw new Exception("Prerequisite failed: test data not generated");

                string typeDir = Path.Combine(testDataDir, "filtered_types");
                tempDirs.Add(typeDir);

                dynamic result = bridge.run_scan_filter(csvPath, dicomDir, typeDir);

                // Each cast should succeed without throwing
                bool success = (bool)result["success"];
                int accepted = (int)result["accepted_frames"];
                int rejected = (int)result["rejected_frames"];
                int stableCycles = (int)result["stable_cycles"];
                double offsetMs = (double)result["alignment_offset_ms"];
                string outputDir = (string)result["output_dir"];
                string error = (string)result["error"];

                // Verify types are reasonable
                Assert.IsTrue(success, "Pipeline should succeed");
                Assert.IsTrue(accepted >= 0, "Accepted frames should be non-negative");
                Assert.IsTrue(rejected >= 0, "Rejected frames should be non-negative");
                Assert.IsTrue(stableCycles >= 0, "Stable cycles should be non-negative");
                Assert.IsNotNull(outputDir, "Output dir should not be null");
                Assert.IsNotNull(error, "Error should not be null");
            });
        }

        // Cleanup temp directories
        foreach (string dir in tempDirs)
        {
            TryDeleteDirectory(dir);
        }

        return results;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }
}
