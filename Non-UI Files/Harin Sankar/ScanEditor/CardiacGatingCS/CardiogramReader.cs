using System.Globalization; // Invariant number parsing.

namespace CardiacGating; // Project namespace.

// Reads cardiogram CSV rows into CardioSample objects.
public sealed class CardiogramCsvReader
{
    private readonly string _timestampColumn; // Header name for timestamp field.
    private readonly string _valueColumn;     // Header name for signal field.

    // Constructor allows custom column names while keeping defaults.
    public CardiogramCsvReader(
        string timestampColumn = "timestamp_ms", // Default timestamp header.
        string valueColumn     = "signal")       // Default signal header.
    {
        _timestampColumn = timestampColumn; // Store timestamp header key.
        _valueColumn     = valueColumn;     // Store value header key.
    }

    // Parses CSV at path and returns immutable cardio series.
    public CardioSeries Read(string path)
    {
        if (!File.Exists(path)) // Fail fast if input file is missing.
            throw new FileNotFoundException($"CSV file not found: {path}");

        using var reader = new StreamReader(path); // Open text reader.

        string? headerLine = reader.ReadLine(); // Read header row.
        if (headerLine is null) // Empty file has no schema.
            throw new InvalidDataException("CSV file is missing a header row");

        string[] headers = headerLine.Split(','); // Split header columns.
        int tsIdx  = IndexOf(headers, _timestampColumn); // Find timestamp column index.
        int valIdx = IndexOf(headers, _valueColumn);     // Find signal column index.

        if (tsIdx < 0 || valIdx < 0) // Enforce required columns.
            throw new InvalidDataException(
                $"CSV must contain '{_timestampColumn}' and '{_valueColumn}' columns");

        int maxIdx = Math.Max(tsIdx, valIdx);       // Highest required column index.
        var samples = new List<CardioSample>();     // Stores successfully parsed rows.

        string? line; // Current row text.
        while ((line = reader.ReadLine()) is not null) // Iterate remaining rows.
        {
            if (string.IsNullOrWhiteSpace(line)) continue; // Skip blank lines.

            string[] parts = line.Split(','); // Split row fields.
            if (parts.Length <= maxIdx) continue; // Skip short/malformed rows.

            if (!double.TryParse( // Parse timestamp token.
                    parts[tsIdx].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double ts))
                continue; // Skip row if timestamp is invalid.

            if (!double.TryParse( // Parse signal token.
                    parts[valIdx].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double val))
                continue; // Skip row if value is invalid.

            samples.Add(new CardioSample(ts, val)); // Keep validated sample.
        }

        return new CardioSeries(samples); // Convert to immutable series wrapper.
    }

    // Finds a header index using trim + case-insensitive comparison.
    private static int IndexOf(string[] headers, string name)
        => Array.FindIndex(headers, h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
}
