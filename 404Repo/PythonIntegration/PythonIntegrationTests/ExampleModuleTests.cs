using Python.Runtime;

namespace PythonIntegrationTests;

/// <summary>
/// Integration tests for the example.py Python module.
///
/// These tests verify that basic Python data types (strings, ints, floats,
/// dicts, lists) marshal correctly across the Python.NET boundary.
/// </summary>
public static class ExampleModuleTests
{
    public static TestResults Run()
    {
        var results = new TestResults("example.py");

        using (Py.GIL())
        {
            dynamic example = Py.Import("example");

            // Test: greet() returns correct greeting string
            results.RunTest("greet returns greeting string", () =>
            {
                string greeting = example.greet("BioMetrix");
                Assert.AreEqual("Hello, BioMetrix!", greeting);
            });

            // Test: add() performs integer addition
            results.RunTest("add returns correct sum", () =>
            {
                int sum = example.add(10, 20);
                Assert.AreEqual(30, sum);
            });

            // Test: add() handles negative numbers
            results.RunTest("add handles negative numbers", () =>
            {
                int sum = example.add(-5, 3);
                Assert.AreEqual(-2, sum);
            });

            // Test: multiply() performs float multiplication
            results.RunTest("multiply returns correct product", () =>
            {
                double product = example.multiply(3.0, 4.0);
                Assert.AreApproximatelyEqual(12.0, product, 0.001);
            });

            // Test: reverse_string() reverses input
            results.RunTest("reverse_string reverses correctly", () =>
            {
                string reversed = example.reverse_string("DICOM");
                Assert.AreEqual("MOCID", reversed);
            });

            // Test: reverse_string() handles empty string
            results.RunTest("reverse_string handles empty string", () =>
            {
                string reversed = example.reverse_string("");
                Assert.AreEqual("", reversed);
            });

            // Test: make_patient_record() returns dict with expected keys
            results.RunTest("make_patient_record returns valid dict", () =>
            {
                dynamic record = example.make_patient_record("PAT001", "Test Patient", 45);
                string patientId = record["patient_id"].ToString();
                string name = record["name"].ToString();
                int age = (int)record["age"];
                string status = record["status"].ToString();

                Assert.AreEqual("PAT001", patientId);
                Assert.AreEqual("Test Patient", name);
                Assert.AreEqual(45, age);
                Assert.AreEqual("active", status);
            });

            // Test: fibonacci() returns correct sequence
            results.RunTest("fibonacci returns correct sequence", () =>
            {
                dynamic fibList = example.fibonacci(8);
                var expected = new[] { 0, 1, 1, 2, 3, 5, 8, 13 };
                int length = (int)fibList.__len__();
                Assert.AreEqual(expected.Length, length);
                for (int i = 0; i < expected.Length; i++)
                {
                    int val = (int)fibList[i];
                    Assert.AreEqual(expected[i], val, $"fibonacci[{i}]");
                }
            });

            // Test: fibonacci(0) returns empty list
            results.RunTest("fibonacci(0) returns empty list", () =>
            {
                dynamic fibList = example.fibonacci(0);
                int length = (int)fibList.__len__();
                Assert.AreEqual(0, length);
            });

            // Test: get_module_info() returns expected metadata
            results.RunTest("get_module_info returns metadata", () =>
            {
                dynamic info = example.get_module_info();
                string moduleName = info["module_name"].ToString();
                string framework = info["framework"].ToString();
                Assert.AreEqual("example", moduleName);
                Assert.AreEqual("pythonnet", framework);
            });
        }

        return results;
    }
}
