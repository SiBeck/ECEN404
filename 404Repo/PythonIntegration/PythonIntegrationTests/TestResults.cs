namespace PythonIntegrationTests;

/// <summary>
/// Collects and reports results from a test suite for a single Python module.
/// </summary>
public class TestResults
{
    public string ModuleName { get; }
    public List<TestCase> Cases { get; } = new();
    public int Passed => Cases.Count(c => c.Passed);
    public int Failed => Cases.Count(c => !c.Passed);
    public int Total => Cases.Count;

    public TestResults(string moduleName)
    {
        ModuleName = moduleName;
    }

    /// <summary>
    /// Run a single test case, capturing pass/fail and any exception message.
    /// </summary>
    public void RunTest(string name, Action testAction)
    {
        try
        {
            testAction();
            Cases.Add(new TestCase(name, true, null));
            Console.WriteLine($"  [PASS] {name}");
        }
        catch (AssertionException ex)
        {
            Cases.Add(new TestCase(name, false, ex.Message));
            Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"  [FAIL] {name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Cases.Add(new TestCase(name, false, $"[ERROR] {ex.GetType().Name}: {ex.Message}"));
            Console.WriteLine($"  [ERROR] {name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"  [ERROR] {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Print a summary of all test results for this module.
    /// </summary>
    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine($"--- {ModuleName} ---");
        Console.WriteLine($"  Total: {Total}  Passed: {Passed}  Failed: {Failed}");
        if (Failed > 0)
        {
            Console.WriteLine("  Failures:");
            foreach (var c in Cases.Where(c => !c.Passed))
                Console.WriteLine($"    - {c.Name}: {c.ErrorMessage}");
        }
    }
}

/// <summary>
/// Represents a single test case result.
/// </summary>
public record TestCase(string Name, bool Passed, string? ErrorMessage);
