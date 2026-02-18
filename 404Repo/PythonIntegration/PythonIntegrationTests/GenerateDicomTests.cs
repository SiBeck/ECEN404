using Python.Runtime;

namespace PythonIntegrationTests;

/// <summary>
/// Integration tests for the generate_dicom.py Python module.
///
/// These tests verify DICOM generation, pixel data creation, metadata
/// reading, and the structured report API work correctly through Python.NET.
///
/// NOTE: These tests require numpy and pydicom to be installed in the
/// Python environment. If those packages are missing, tests will fail
/// with an import error and be reported as such.
/// </summary>
public static class GenerateDicomTests
{
    public static TestResults Run()
    {
        var results = new TestResults("generate_dicom.py");

        using (Py.GIL())
        {
            dynamic dicomGen;
            try
            {
                dicomGen = Py.Import("generate_dicom");
            }
            catch (PythonException ex)
            {
                results.RunTest("import generate_dicom", () =>
                {
                    throw new Exception(
                        $"Cannot import generate_dicom. Ensure numpy and pydicom are installed. " +
                        $"Error: {ex.Message}");
                });
                return results;
            }

            // Test: get_available_patterns() returns the known pattern list
            results.RunTest("get_available_patterns returns pattern list", () =>
            {
                dynamic patterns = dicomGen.get_available_patterns();
                int count = (int)patterns.__len__();
                Assert.IsTrue(count >= 4, $"Expected at least 4 patterns, got {count}");

                // Convert to a C# list for easier checking
                var patternList = new List<string>();
                for (int i = 0; i < count; i++)
                    patternList.Add(patterns[i].ToString());

                Assert.IsTrue(patternList.Contains("gradient"), "Should contain 'gradient'");
                Assert.IsTrue(patternList.Contains("checkerboard"), "Should contain 'checkerboard'");
                Assert.IsTrue(patternList.Contains("circle"), "Should contain 'circle'");
                Assert.IsTrue(patternList.Contains("shapes"), "Should contain 'shapes'");
            });

            // Test: generate_pixel_data() returns bytes of expected length
            results.RunTest("generate_pixel_data returns correct byte count (grayscale)", () =>
            {
                int width = 64, height = 64;
                dynamic pixelBytes = dicomGen.generate_pixel_data("gradient", width, height);
                int length = (int)pixelBytes.__len__();
                int expected = width * height; // 1 byte per pixel (grayscale)
                Assert.AreEqual(expected, length,
                    $"Expected {expected} bytes for {width}x{height} grayscale");
            });

            // Test: generate_pixel_data() works for each pattern
            results.RunTest("generate_pixel_data works for all patterns", () =>
            {
                dynamic patterns = dicomGen.get_available_patterns();
                int count = (int)patterns.__len__();
                for (int i = 0; i < count; i++)
                {
                    string pattern = patterns[i].ToString();
                    dynamic pixelBytes = dicomGen.generate_pixel_data(pattern, 32, 32);
                    int length = (int)pixelBytes.__len__();
                    Assert.AreEqual(32 * 32, length, $"Pattern '{pattern}'");
                }
            });

            // Test: generate_color_pixel_data() returns 3 bytes per pixel
            results.RunTest("generate_color_pixel_data returns RGB bytes", () =>
            {
                int width = 32, height = 32;
                dynamic pixelBytes = dicomGen.generate_color_pixel_data(width, height);
                int length = (int)pixelBytes.__len__();
                int expected = width * height * 3; // 3 bytes per pixel (RGB)
                Assert.AreEqual(expected, length,
                    $"Expected {expected} bytes for {width}x{height} RGB");
            });

            // Test: build_dicom_and_report() creates a file and returns metadata
            string tempDcm = Path.Combine(Path.GetTempPath(), "test_integration.dcm");
            results.RunTest("build_dicom_and_report creates DICOM file", () =>
            {
                dynamic report = dicomGen.build_dicom_and_report(
                    tempDcm, width: 64, height: 64, num_frames: 1,
                    color: false, pattern: "gradient",
                    patient_name: "TestNet^CSharp", patient_id: "CSTEST001");

                bool success = (bool)report["success"];
                Assert.IsTrue(success, $"DICOM generation failed: {report["error"]}");

                int fileSizeBytes = (int)report["file_size_bytes"];
                Assert.IsTrue(fileSizeBytes > 0, $"File size should be positive, got {fileSizeBytes}");

                Assert.AreEqual(64, (int)report["width"]);
                Assert.AreEqual(64, (int)report["height"]);
                Assert.AreEqual(1, (int)report["num_frames"]);
                Assert.AreEqual("gradient", report["mode"].ToString());
                Assert.AreEqual("TestNet^CSharp", report["patient_name"].ToString());

                // Verify the file actually exists on disk
                Assert.IsTrue(File.Exists(tempDcm), "DICOM file should exist on disk");
            });

            // Test: get_dicom_metadata() reads back the file we just created
            results.RunTest("get_dicom_metadata reads generated DICOM", () =>
            {
                if (!File.Exists(tempDcm))
                {
                    throw new Exception("Prerequisite failed: DICOM file was not created");
                }

                dynamic meta = dicomGen.get_dicom_metadata(tempDcm);
                bool success = (bool)meta["success"];
                Assert.IsTrue(success, $"Metadata read failed: {meta["error"]}");

                Assert.AreEqual(64, (int)meta["rows"]);
                Assert.AreEqual(64, (int)meta["columns"]);
                Assert.AreEqual("MONOCHROME2", meta["photometric_interpretation"].ToString());
                Assert.AreEqual(8, (int)meta["bits_allocated"]);
                Assert.AreEqual("TestNet^CSharp", meta["patient_name"].ToString());
                Assert.AreEqual("CSTEST001", meta["patient_id"].ToString());

                int pixelDataLen = (int)meta["pixel_data_length"];
                Assert.AreEqual(64 * 64, pixelDataLen,
                    $"Pixel data length mismatch: expected {64 * 64}, got {pixelDataLen}");
            });

            // Test: build_dicom_and_report() with multi-frame
            string tempMultiDcm = Path.Combine(Path.GetTempPath(), "test_multi_frame.dcm");
            results.RunTest("build_dicom_and_report creates multi-frame DICOM", () =>
            {
                dynamic report = dicomGen.build_dicom_and_report(
                    tempMultiDcm, width: 32, height: 32, num_frames: 5,
                    color: false, pattern: "circle");

                bool success = (bool)report["success"];
                Assert.IsTrue(success, $"Multi-frame generation failed: {report["error"]}");
                Assert.AreEqual(5, (int)report["num_frames"]);
            });

            // Test: get_dicom_metadata() reads multi-frame correctly
            results.RunTest("get_dicom_metadata reads multi-frame DICOM", () =>
            {
                if (!File.Exists(tempMultiDcm))
                {
                    throw new Exception("Prerequisite failed: multi-frame DICOM not created");
                }

                dynamic meta = dicomGen.get_dicom_metadata(tempMultiDcm);
                Assert.IsTrue((bool)meta["success"]);
                Assert.AreEqual(5, (int)meta["num_frames"]);
                Assert.AreEqual(32, (int)meta["rows"]);
                Assert.AreEqual(32, (int)meta["columns"]);

                // 5 frames x 32x32 pixels x 1 byte = 5120 bytes
                Assert.AreEqual(5120, (int)meta["pixel_data_length"]);
            });

            // Test: build_dicom_and_report() with color
            string tempColorDcm = Path.Combine(Path.GetTempPath(), "test_color.dcm");
            results.RunTest("build_dicom_and_report creates color DICOM", () =>
            {
                dynamic report = dicomGen.build_dicom_and_report(
                    tempColorDcm, width: 32, height: 32, num_frames: 1,
                    color: true);

                bool success = (bool)report["success"];
                Assert.IsTrue(success, $"Color generation failed: {report["error"]}");
                Assert.AreEqual("RGB", report["mode"].ToString());
            });

            // Test: get_dicom_metadata() confirms color DICOM attributes
            results.RunTest("get_dicom_metadata reads color DICOM", () =>
            {
                if (!File.Exists(tempColorDcm))
                {
                    throw new Exception("Prerequisite failed: color DICOM not created");
                }

                dynamic meta = dicomGen.get_dicom_metadata(tempColorDcm);
                Assert.IsTrue((bool)meta["success"]);
                Assert.AreEqual("RGB", meta["photometric_interpretation"].ToString());
                // 32x32x3 bytes for RGB
                Assert.AreEqual(32 * 32 * 3, (int)meta["pixel_data_length"]);
            });

            // Test: get_dicom_metadata() on non-existent file
            results.RunTest("get_dicom_metadata handles missing file", () =>
            {
                dynamic meta = dicomGen.get_dicom_metadata("/nonexistent/fake.dcm");
                bool success = (bool)meta["success"];
                Assert.IsFalse(success, "Should fail for missing file");
            });

            // Cleanup temp files
            TryDelete(tempDcm);
            TryDelete(tempMultiDcm);
            TryDelete(tempColorDcm);
        }

        return results;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
