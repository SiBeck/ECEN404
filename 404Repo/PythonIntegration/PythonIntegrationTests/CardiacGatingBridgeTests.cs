using Python.Runtime;

namespace PythonIntegrationTests;

/// <summary>
/// Integration tests for the cardiac_gating_bridge.py Python module.
///
/// These tests verify the cardiac gating bridge functions work correctly through
/// Python.NET, including input validation, end-to-end pipeline execution,
/// error handling, and type marshalling.
///
/// NOTE: These tests require numpy, pydicom, and pyyaml to be installed in the
/// Python environment. If missing, tests will fail with an import error.
/// </summary>
public static class CardiacGatingBridgeTests
{
    public static TestResults Run()
    {
        var results = new TestResults("cardiac_gating_bridge.py");

        // Track temp directories for cleanup
        var tempDirs = new List<string>();

        using (Py.GIL())
        {
            // --- Import bridge module ---
            dynamic bridge;
            try
            {
                bridge = Py.Import("cardiac_gating_bridge");
            }
            catch (PythonException ex)
            {
                results.RunTest("import cardiac_gating_bridge", () =>
                {
                    throw new Exception(
                        $"Cannot import cardiac_gating_bridge. Ensure numpy, pydicom, " +
                        $"and pyyaml are installed. Error: {ex.Message}");
                });
                return results;
            }

            results.RunTest("import cardiac_gating_bridge", () =>
            {
                Assert.IsNotNull(bridge, "Bridge module should not be null");
            });

            // --- Locate test case data ---
            // FinalCode ships with test_cases/case_01/ containing real cardiogram + MRD data
            string testCaseDir = Path.Combine(PythonSetup.FinalCodePath, "test_cases", "case_01");
            string csvPath = Path.Combine(testCaseDir, "cardiogram.csv");
            string mrdFile = Path.Combine(testCaseDir, "te014_case_01.mrd");
            bool testDataAvailable = Directory.Exists(testCaseDir)
                                     && File.Exists(csvPath)
                                     && File.Exists(mrdFile);

            results.RunTest("test case data exists", () =>
            {
                Assert.IsTrue(Directory.Exists(testCaseDir),
                    $"Test case directory should exist: {testCaseDir}");
                Assert.IsTrue(File.Exists(csvPath),
                    $"Cardiogram CSV should exist: {csvPath}");
                Assert.IsTrue(File.Exists(mrdFile),
                    $"MRD file should exist: {mrdFile}");
            });

            // --- validate_inputs tests ---

            results.RunTest("validate_inputs with valid paths", () =>
            {
                if (!testDataAvailable) throw new Exception("Prerequisite failed: test data not available");

                dynamic result = bridge.validate_inputs(csvPath, mrdFile);
                Assert.IsTrue((bool)result["valid"], "Should be valid");
                Assert.IsTrue((bool)result["csv_exists"], "CSV should exist");
                Assert.IsTrue((bool)result["mrd_exists"], "MRD file should exist");
                Assert.AreEqual("", (string)result["error"], "Error should be empty");
            });

            results.RunTest("validate_inputs with missing CSV", () =>
            {
                dynamic result = bridge.validate_inputs("/nonexistent/missing.csv", mrdFile);
                Assert.IsFalse((bool)result["valid"], "Should be invalid");
                Assert.IsFalse((bool)result["csv_exists"], "CSV should not exist");

                string error = (string)result["error"];
                Assert.IsTrue(error.Length > 0, "Error message should be non-empty");
            });

            results.RunTest("validate_inputs with missing MRD file", () =>
            {
                dynamic result = bridge.validate_inputs(csvPath, "/nonexistent/missing.mrd");
                Assert.IsFalse((bool)result["valid"], "Should be invalid");
                Assert.IsFalse((bool)result["mrd_exists"], "MRD file should not exist");

                string error = (string)result["error"];
                Assert.IsTrue(error.Length > 0, "Error message should be non-empty");
            });

            results.RunTest("validate_inputs with both missing", () =>
            {
                dynamic result = bridge.validate_inputs("/nonexistent/a.csv", "/nonexistent/b.mrd");
                Assert.IsFalse((bool)result["valid"], "Should be invalid");
                Assert.IsFalse((bool)result["csv_exists"], "CSV should not exist");
                Assert.IsFalse((bool)result["mrd_exists"], "MRD should not exist");
            });

            // --- run_cardiac_gating tests ---

            string happyOutputDir = Path.Combine(Path.GetTempPath(),
                "cardiac_gating_test_" + Guid.NewGuid().ToString("N")[..8]);
            tempDirs.Add(happyOutputDir);
            int happyAccepted = 0;

            results.RunTest("run_cardiac_gating happy path", () =>
            {
                if (!testDataAvailable) throw new Exception("Prerequisite failed: test data not available");

                dynamic result = bridge.run_cardiac_gating(csvPath, mrdFile, happyOutputDir);

                bool success = (bool)result["success"];
                Assert.IsTrue(success, $"Pipeline failed: {result["error"]}");

                happyAccepted = (int)result["accepted_frames"];
                Assert.IsTrue(happyAccepted > 0, "Should accept at least one frame");
                Assert.AreEqual("", (string)result["error"], "Error should be empty");
            });

            results.RunTest("run_cardiac_gating frame counts sum correctly", () =>
            {
                if (!testDataAvailable) throw new Exception("Prerequisite failed: test data not available");

                string sumDir = Path.Combine(Path.GetTempPath(),
                    "cardiac_gating_sum_" + Guid.NewGuid().ToString("N")[..8]);
                tempDirs.Add(sumDir);

                dynamic result = bridge.run_cardiac_gating(csvPath, mrdFile, sumDir);
                Assert.IsTrue((bool)result["success"], $"Pipeline failed: {result["error"]}");

                int accepted = (int)result["accepted_frames"];
                int rejected = (int)result["rejected_frames"];
                Assert.IsTrue(accepted + rejected > 0,
                    "Total frames should be greater than zero");
            });

            results.RunTest("run_cardiac_gating output dir exists", () =>
            {
                if (happyAccepted == 0) throw new Exception("Prerequisite failed: happy path did not accept frames");

                Assert.IsTrue(Directory.Exists(happyOutputDir),
                    $"Output directory should exist: {happyOutputDir}");
            });

            results.RunTest("run_cardiac_gating output files match accepted count", () =>
            {
                if (happyAccepted == 0) throw new Exception("Prerequisite failed: happy path did not accept frames");

                int fileCount = Directory.GetFiles(happyOutputDir, "*.dcm").Length;
                Assert.AreEqual(happyAccepted, fileCount,
                    $"Expected {happyAccepted} DICOM files, found {fileCount}");
            });

            results.RunTest("run_cardiac_gating with invalid CSV returns error", () =>
            {
                string errDir = Path.Combine(Path.GetTempPath(),
                    "cardiac_gating_err1_" + Guid.NewGuid().ToString("N")[..8]);
                tempDirs.Add(errDir);

                dynamic result = bridge.run_cardiac_gating("/nonexistent/bad.csv", mrdFile, errDir);
                Assert.IsFalse((bool)result["success"], "Should fail for missing CSV");

                string error = (string)result["error"];
                Assert.IsTrue(error.Length > 0, "Error message should be non-empty");
            });

            results.RunTest("run_cardiac_gating with invalid MRD returns error", () =>
            {
                string errDir = Path.Combine(Path.GetTempPath(),
                    "cardiac_gating_err2_" + Guid.NewGuid().ToString("N")[..8]);
                tempDirs.Add(errDir);

                dynamic result = bridge.run_cardiac_gating(csvPath, "/nonexistent/bad.mrd", errDir);
                Assert.IsFalse((bool)result["success"], "Should fail for missing MRD");

                string error = (string)result["error"];
                Assert.IsTrue(error.Length > 0, "Error message should be non-empty");
            });

            results.RunTest("run_cardiac_gating type marshalling", () =>
            {
                if (!testDataAvailable) throw new Exception("Prerequisite failed: test data not available");

                string typeDir = Path.Combine(Path.GetTempPath(),
                    "cardiac_gating_types_" + Guid.NewGuid().ToString("N")[..8]);
                tempDirs.Add(typeDir);

                dynamic result = bridge.run_cardiac_gating(csvPath, mrdFile, typeDir);

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
