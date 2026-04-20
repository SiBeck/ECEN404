namespace CardiacGating; // Project namespace.

// Numeric helper methods used by signal thresholding.
internal static class Statistics
{
    // Computes moving average with edge padding to keep original length.
    internal static double[] MovingAverage(double[] data, int window)
    {
        if (data.Length == 0) // No data means no output values.
            return Array.Empty<double>();

        if (window <= 1) // Window <= 1 means no smoothing.
            return (double[])data.Clone(); // Return value copy to preserve immutability semantics.

        if (window >= data.Length) // Oversized window collapses to global average.
        {
            double mean = data.Average(); // Compute one average for all points.
            return Enumerable.Repeat(mean, data.Length).ToArray(); // Fill entire output with mean.
        }

        double[] cs = new double[data.Length + 1]; // Prefix-sum array (cs[i] = sum of first i samples).

        for (int i = 0; i < data.Length; i++) // Build prefix sums.
            cs[i + 1] = cs[i] + data[i];      // Recurrence for cumulative sum.

        double[] core = new double[data.Length - window + 1]; // True moving-average region without padding.

        for (int i = 0; i < core.Length; i++) // Compute each window mean using prefix sums.
            core[i] = (cs[i + window] - cs[i]) / window; // O(1) per window.

        int padLeft = window / 2;                           // Left edge pad count.
        int padRight = data.Length - core.Length - padLeft; // Right edge pad count.
        double[] result = new double[data.Length];          // Final output matches input length.

        for (int i = 0; i < padLeft; i++) // Fill left pad with first core value.
            result[i] = core[0];

        Array.Copy(core, 0, result, padLeft, core.Length); // Copy center region.

        for (int i = 0; i < padRight; i++) // Fill right pad with last core value.
            result[padLeft + core.Length + i] = core[^1];

        return result; // Return padded moving average.
    }

    // Computes moving standard deviation using moving mean of squared residuals.
    internal static double[] MovingStd(double[] data, int window)
    {
        if (data.Length == 0) // No data means no output values.
            return Array.Empty<double>();

        double[] mean = MovingAverage(data, window); // Local mean per sample.
        double[] sq = new double[data.Length];       // Squared residuals.

        for (int i = 0; i < data.Length; i++) // Build squared residual series.
            sq[i] = (data[i] - mean[i]) * (data[i] - mean[i]);

        double[] variance = MovingAverage(sq, window); // Local variance estimate.
        double[] std = new double[data.Length];        // Standard deviation output.

        for (int i = 0; i < data.Length; i++) // Convert variance to std with floor clamp.
            std[i] = Math.Sqrt(Math.Max(variance[i], 1e-9)); // Prevent sqrt of tiny negative due to numeric drift.

        return std; // Return local standard deviation values.
    }
}
