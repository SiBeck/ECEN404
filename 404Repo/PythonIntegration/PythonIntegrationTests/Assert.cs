namespace PythonIntegrationTests;

/// <summary>
/// Lightweight assertion helpers for the integration tests.
///
/// These throw <see cref="AssertionException"/> on failure, which is caught
/// by <see cref="TestResults.RunTest"/> to report pass/fail without aborting
/// the entire test run.
/// </summary>
public static class Assert
{
    public static void AreEqual(object expected, object actual, string? label = null)
    {
        if (!Equals(expected, actual))
        {
            string msg = label != null
                ? $"[{label}] Expected: {expected}, Actual: {actual}"
                : $"Expected: {expected}, Actual: {actual}";
            throw new AssertionException(msg);
        }
    }

    public static void AreApproximatelyEqual(double expected, double actual, double tolerance,
        string? label = null)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            string msg = label != null
                ? $"[{label}] Expected: ~{expected} (±{tolerance}), Actual: {actual}"
                : $"Expected: ~{expected} (±{tolerance}), Actual: {actual}";
            throw new AssertionException(msg);
        }
    }

    public static void IsTrue(bool condition, string? message = null)
    {
        if (!condition)
            throw new AssertionException(message ?? "Expected condition to be true");
    }

    public static void IsFalse(bool condition, string? message = null)
    {
        if (condition)
            throw new AssertionException(message ?? "Expected condition to be false");
    }

    public static void IsNotNull(object? value, string? message = null)
    {
        if (value is null)
            throw new AssertionException(message ?? "Expected non-null value");
    }
}

/// <summary>
/// Exception thrown when an assertion fails. Distinct from system exceptions
/// so test infrastructure can distinguish test failures from runtime errors.
/// </summary>
public class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}
