using Python.Runtime;

namespace PythonIntegrationTests;

/// <summary>
/// Integration tests for the image_viewer.py Python module
/// (located in "Dummy python code" directory).
///
/// These tests verify image file inspection, validation, byte loading,
/// and dimension reading work correctly through the Python.NET bridge.
/// </summary>
public static class ImageViewerTests
{
    public static TestResults Run()
    {
        var results = new TestResults("image_viewer.py");

        // The sample_picture.png should be in the Dummy python code directory.
        string sampleImage = Path.Combine(PythonSetup.DummyCodePath, "sample_picture.png");

        using (Py.GIL())
        {
            dynamic imageViewer;
            try
            {
                imageViewer = Py.Import("image_viewer");
            }
            catch (PythonException ex)
            {
                results.RunTest("import image_viewer", () =>
                {
                    throw new Exception(
                        $"Cannot import image_viewer module. Ensure 'Dummy python code' directory " +
                        $"is on sys.path. Error: {ex.Message}");
                });
                return results;
            }

            results.RunTest("import image_viewer", () =>
            {
                Assert.IsNotNull(imageViewer, "ImageViewer module should not be null");
            });

            // Test: get_image_info() on existing file
            results.RunTest("get_image_info returns metadata for existing file", () =>
            {
                dynamic info = imageViewer.get_image_info(sampleImage);
                bool exists = (bool)info["exists"];
                string filename = info["filename"].ToString();
                string extension = info["extension"].ToString();
                int sizeBytes = (int)info["size_bytes"];

                Assert.IsTrue(exists, "File should exist");
                Assert.AreEqual("sample_picture.png", filename);
                Assert.AreEqual(".png", extension);
                Assert.IsTrue(sizeBytes > 0, $"File size should be positive, got {sizeBytes}");
            });

            // Test: get_image_info() on non-existent file
            results.RunTest("get_image_info handles missing file", () =>
            {
                dynamic info = imageViewer.get_image_info("/nonexistent/fake.png");
                bool exists = (bool)info["exists"];
                int sizeBytes = (int)info["size_bytes"];

                Assert.IsFalse(exists, "File should not exist");
                Assert.AreEqual(-1, sizeBytes);
            });

            // Test: load_image_bytes() returns non-null bytes for existing file
            results.RunTest("load_image_bytes returns bytes for existing file", () =>
            {
                dynamic rawBytes = imageViewer.load_image_bytes(sampleImage);
                Assert.IsNotNull(rawBytes, "Bytes should not be None");
                int length = (int)rawBytes.__len__();
                Assert.IsTrue(length > 0, $"Byte length should be positive, got {length}");
            });

            // Test: load_image_bytes() returns None for missing file
            results.RunTest("load_image_bytes returns None for missing file", () =>
            {
                dynamic rawBytes = imageViewer.load_image_bytes("/nonexistent/fake.png");
                bool isNone = rawBytes == null || ((PyObject)rawBytes).IsNone();
                Assert.IsTrue(isNone, "Should return None for missing file");
            });

            // Test: get_image_dimensions() returns valid PNG dimensions
            results.RunTest("get_image_dimensions reads PNG dimensions", () =>
            {
                dynamic dims = imageViewer.get_image_dimensions(sampleImage);
                int width = (int)dims["width"];
                int height = (int)dims["height"];
                string format = dims["format"].ToString();

                Assert.IsTrue(width > 0, $"Width should be positive, got {width}");
                Assert.IsTrue(height > 0, $"Height should be positive, got {height}");
                Assert.AreEqual("PNG", format);
            });

            // Test: get_image_dimensions() on missing file
            results.RunTest("get_image_dimensions handles missing file", () =>
            {
                dynamic dims = imageViewer.get_image_dimensions("/nonexistent/fake.png");
                string format = dims["format"].ToString();
                Assert.AreEqual("NOT_FOUND", format);
            });

            // Test: validate_image_file() confirms PNG is valid
            results.RunTest("validate_image_file confirms valid PNG", () =>
            {
                dynamic result = imageViewer.validate_image_file(sampleImage);
                bool valid = (bool)result["valid"];
                string detectedFormat = result["detected_format"].ToString();

                Assert.IsTrue(valid, "PNG file should be valid");
                Assert.AreEqual("PNG", detectedFormat);
            });

            // Test: validate_image_file() rejects missing file
            results.RunTest("validate_image_file rejects missing file", () =>
            {
                dynamic result = imageViewer.validate_image_file("/nonexistent/fake.png");
                bool valid = (bool)result["valid"];
                Assert.IsFalse(valid, "Missing file should not be valid");
            });

            // Test: validate_image_file() rejects non-image file
            results.RunTest("validate_image_file rejects non-image file", () =>
            {
                // Use a Python script as a non-image file
                string pyFile = Path.Combine(PythonSetup.PythonScriptsPath, "example.py");
                dynamic result = imageViewer.validate_image_file(pyFile);
                bool valid = (bool)result["valid"];
                Assert.IsFalse(valid, "Python file should not be a valid image");
            });

            // Test: open_image_native() handles missing file gracefully
            results.RunTest("open_image_native returns error for missing file", () =>
            {
                dynamic result = imageViewer.open_image_native("/nonexistent/fake.png");
                bool success = (bool)result["success"];
                Assert.IsFalse(success, "Should fail for missing file");
            });
        }

        return results;
    }
}
