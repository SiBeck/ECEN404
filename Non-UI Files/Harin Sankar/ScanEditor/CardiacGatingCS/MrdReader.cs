using System.Numerics; // Complex numbers for k-space.
using System.Text; // ASCII decoding for header strings.

namespace CardiacGating; // Project namespace.

// Immutable MRD header snapshot used by higher-level readers.
public sealed record MrdHeader(
    int FrequencyEncoding, // Number of FE points.
    int PhaseEncoding,     // Number of PE points.
    int N3d,               // Third spatial dimension size.
    int Slices,            // Slice count.
    int Echoes,            // Echo count.
    int Experiments,       // Experiment count.
    string DatatypeHex,    // Raw datatype nibble pair as hex.
    bool IsComplex,        // True when payload stores real/imag pairs.
    string Comments,       // Fixed-size comments block.
    string Parameters)     // Trailing parameter text.
{
    public int FrameCount => Math.Max(1, N3d * Slices * Echoes * Experiments); // Non-spatial frame count.
}

/// <summary>
/// Reads MR Solutions MRD binary files and extracts k-space frames.
/// </summary>
public static class MrdFileReader
{
    // Low-level file parse returning raw k-space frames + parsed header.
    public static (Complex[][,] Frames, MrdHeader Header) Read(string filePath)
    {
        if (!File.Exists(filePath)) // Validate input path before opening stream.
            throw new FileNotFoundException($"MRD file not found: {filePath}");

        using var stream = new FileStream( // Open file stream for binary reading.
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            65536); // Larger buffer for throughput.

        using var reader = new BinaryReader( // Binary primitive reader.
            stream,
            Encoding.ASCII,
            leaveOpen: false);

        int nfe = reader.ReadInt32();    // FE dimension.
        int npe = reader.ReadInt32();    // PE dimension.
        int n3d = reader.ReadInt32();    // 3D dimension.
        int nslice = reader.ReadInt32(); // Slice dimension.

        stream.Seek(2, SeekOrigin.Current); // Skip reserved bytes.

        short dtRaw = reader.ReadInt16();                      // Raw datatype word.
        string dtHex = ((ushort)dtRaw).ToString("X").PadLeft(2, '0'); // Two-nibble datatype representation.
        bool isComplex = dtHex[0] == '1';                      // Leading nibble encodes complex payload flag.
        char dtCode = dtHex[1];                                // Trailing nibble encodes primitive type.

        stream.Seek(132, SeekOrigin.Current); // Skip reserved block.

        int nechoes = reader.ReadInt32(); // Echo dimension.
        int nexps = reader.ReadInt32();   // Experiment dimension.

        stream.Seek(256, SeekOrigin.Begin); // Jump to comments block start.
        byte[] commBytes = reader.ReadBytes(256); // Read fixed comments block.
        string comments = Encoding.ASCII.GetString(commBytes).TrimEnd('\0').Trim(); // Remove null padding.

        long numPoints = (long)nfe * npe * n3d * nslice * nechoes * nexps; // Scalar payload count.
        Complex[] flat = ReadFlatData(reader, dtCode, numPoints, isComplex); // Decode flat payload.

        long filenameEnd = stream.Position + 120; // Optional filename slot end position.
        if (filenameEnd <= stream.Length)         // Only skip if slot exists.
            stream.Seek(120, SeekOrigin.Current);

        long remaining = stream.Length - stream.Position; // Remaining tail bytes.
        string parameters = remaining > 0
            ? Encoding.ASCII.GetString(reader.ReadBytes((int)Math.Min(remaining, int.MaxValue))).TrimEnd('\0') // Tail text.
            : string.Empty; // Empty when no tail remains.

        var header = new MrdHeader( // Build immutable header record.
            nfe,
            npe,
            n3d,
            nslice,
            nechoes,
            nexps,
            dtHex,
            isComplex,
            comments,
            parameters);

        int safe3d = Math.Max(1, n3d);           // Prevent zero in product/decomposition.
        int safeSlice = Math.Max(1, nslice);      // Prevent zero in product/decomposition.
        int safeEchoes = Math.Max(1, nechoes);    // Prevent zero in product/decomposition.
        int safeExps = Math.Max(1, nexps);        // Prevent zero in product/decomposition.
        int trailing = safe3d * safeSlice * safeEchoes * safeExps; // Number of output frames.

        var frames = new Complex[trailing][,]; // Allocate output frame array.

        for (int t = 0; t < trailing; t++) // Convert each trailing-index slice into one 2D frame.
        {
            int tmp = t;                                // Working index for decomposition.
            int i3d = tmp % safe3d; tmp /= safe3d;     // Decode 3D index (fastest varying).
            int islice = tmp % safeSlice; tmp /= safeSlice; // Decode slice index.
            int iecho = tmp % safeEchoes; tmp /= safeEchoes; // Decode echo index.
            int iexp = tmp;                             // Remaining quotient is experiment index.

            var frame = new Complex[nfe, npe]; // Allocate FE x PE matrix.

            for (int ife = 0; ife < nfe; ife++) // FE loop.
            for (int ipe = 0; ipe < npe; ipe++) // PE loop.
            {
                long idx = ife
                         + (long)nfe * (ipe
                         + (long)npe * (i3d
                         + (long)n3d * (islice
                         + (long)nslice * (iecho
                         + (long)nechoes * iexp)))); // Flattened index in MRD column-major layout.

                frame[ife, ipe] = flat[idx]; // Copy scalar into 2D matrix position.
            }

            frames[t] = frame; // Store extracted frame.
        }

        return (frames, header); // Return all frames + header metadata.
    }

    // Decodes payload values and converts into complex array.
    private static Complex[] ReadFlatData(BinaryReader reader, char dtCode, long numPoints, bool isComplex)
    {
        if (numPoints == 0) // Empty payload case.
            return Array.Empty<Complex>();

        long readCount = isComplex ? numPoints * 2 : numPoints; // Complex payload uses real+imag scalars.
        double[] raw = ReadDoubles(reader, dtCode, (int)readCount); // Decode primitive values as doubles.

        var result = new Complex[numPoints]; // Allocate complex output array.

        if (isComplex) // Interpret as interleaved pairs.
        {
            for (long i = 0; i < numPoints; i++)
                result[i] = new Complex(raw[i * 2], raw[i * 2 + 1]); // [real, imag].
        }
        else // Interpret as real-only values.
        {
            for (long i = 0; i < numPoints; i++)
                result[i] = new Complex(raw[i], 0.0); // Imaginary component is zero.
        }

        return result; // Return decoded complex payload.
    }

    // Dispatches datatype code to concrete primitive reader.
    private static double[] ReadDoubles(BinaryReader r, char code, int count)
    {
        return code switch
        {
            '0' => r.ReadBytes(count).Select(b => (double)b).ToArray(), // Unsigned byte.
            '1' => ReadSBytes(r, count),                                 // Signed byte.
            '2' or '3' => ReadInt16s(r, count),                          // Int16 variants.
            '4' => ReadInt32s(r, count),                                 // Int32.
            '5' => ReadFloat32s(r, count),                               // Float32.
            '6' => ReadFloat64s(r, count),                               // Float64.
            _ => throw new InvalidDataException($"Unsupported MRD datatype nibble: {code}") // Unknown code.
        };
    }

    // Reads signed 8-bit values and promotes to double.
    private static double[] ReadSBytes(BinaryReader r, int n)
        => r.ReadBytes(n).Select(b => (double)(sbyte)b).ToArray();

    // Reads int16 values and promotes to double.
    private static double[] ReadInt16s(BinaryReader r, int n)
    {
        var a = new double[n]; // Output array.
        for (int i = 0; i < n; i++)
            a[i] = r.ReadInt16(); // Promote each int16.
        return a;
    }

    // Reads int32 values and promotes to double.
    private static double[] ReadInt32s(BinaryReader r, int n)
    {
        var a = new double[n]; // Output array.
        for (int i = 0; i < n; i++)
            a[i] = r.ReadInt32(); // Promote each int32.
        return a;
    }

    // Reads float32 values and promotes to double.
    private static double[] ReadFloat32s(BinaryReader r, int n)
    {
        var a = new double[n]; // Output array.
        for (int i = 0; i < n; i++)
            a[i] = r.ReadSingle(); // Promote each float32.
        return a;
    }

    // Reads float64 values directly.
    private static double[] ReadFloat64s(BinaryReader r, int n)
    {
        var a = new double[n]; // Output array.
        for (int i = 0; i < n; i++)
            a[i] = r.ReadDouble(); // Copy each float64.
        return a;
    }
}

/// <summary>
/// High-level MRD reader that timestamps frames and applies optional preprocessing.
/// </summary>
public sealed class MrdSolutionsReader
{
    public double FramePeriodMs { get; } // Timestamp delta between consecutive frames.
    public double StartTimeMs { get; } // Timestamp assigned to first frame.
    public bool ApplyHammingWindow { get; } // Whether to apply Hamming apodization.
    public (int Fe, int Pe)? ZeroPadShape { get; } // Optional zero-pad shape.

    // Constructor stores preprocessing/timestamp parameters.
    public MrdSolutionsReader(
        double framePeriodMs = 40.0,
        double startTimeMs = 0.0,
        bool applyHammingWindow = false,
        (int, int)? zeroPadShape = null)
    {
        if (framePeriodMs <= 0) // Frame period must be positive.
            throw new ArgumentOutOfRangeException(nameof(framePeriodMs), "Must be positive");

        FramePeriodMs = framePeriodMs; // Save frame delta.
        StartTimeMs = startTimeMs; // Save first timestamp.
        ApplyHammingWindow = applyHammingWindow; // Save apodization flag.
        ZeroPadShape = zeroPadShape.HasValue ? ((int Fe, int Pe)?)zeroPadShape.Value : null; // Normalize nullable tuple.
    }

    // Reads MRD file, prepares k-space frames, and returns timestamped series.
    public MRIFrameSeries Read(string filePath)
    {
        (Complex[][,] rawFrames, MrdHeader header) = MrdFileReader.Read(filePath); // Parse raw MRD file.

        if (rawFrames.Length == 0) // Empty frame payload is invalid for pipeline use.
            throw new InvalidDataException($"MRD file '{filePath}' contains no frames");

        var baseMeta = new Dictionary<string, object> // Shared metadata copied to each frame.
        {
            ["source_path"] = filePath,
            ["source_type"] = "mrd_kspace",
            ["mrd_datatype"] = header.DatatypeHex,
            ["mrd_comments"] = header.Comments,
            ["mrd_parameters"] = header.Parameters,
        };

        var frames = new List<MRIFrame>(rawFrames.Length); // Output frame list.

        for (int i = 0; i < rawFrames.Length; i++) // Convert each raw frame into MRIFrame object.
        {
            Complex[,] prepared = PrepareKspace(rawFrames[i]); // Apply optional preprocessing.

            var meta = new Dictionary<string, object>(baseMeta) // Copy shared metadata.
            {
                ["frame_index"] = i,
                ["rows"] = prepared.GetLength(0),
                ["columns"] = prepared.GetLength(1),
            };

            frames.Add(new MRIFrame(
                frameId: i,
                timestampMs: StartTimeMs + i * FramePeriodMs, // Deterministic synthetic timestamp.
                dataset: prepared,
                metadata: meta));
        }

        return new MRIFrameSeries(frames); // Wrap in immutable series object.
    }

    // Applies configured preprocessing pipeline to one frame.
    private Complex[,] PrepareKspace(Complex[,] frame)
    {
        Complex[,] data = (Complex[,])frame.Clone(); // Clone so source data is not modified.

        if (ApplyHammingWindow) // Optional apodization stage.
            data = ApplyHamming(data);

        if (ZeroPadShape.HasValue) // Optional zero-padding stage.
            data = ZeroPad(data, ZeroPadShape.Value.Fe, ZeroPadShape.Value.Pe);

        return data; // Return prepared frame matrix.
    }

    // Applies separable 2D Hamming window.
    private static Complex[,] ApplyHamming(Complex[,] data)
    {
        int rows = data.GetLength(0); // Source row count.
        int cols = data.GetLength(1); // Source column count.

        double[] wr = HammingWindow(rows); // Row coefficients.
        double[] wc = HammingWindow(cols); // Column coefficients.

        var result = new Complex[rows, cols]; // Output matrix.

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
            result[r, c] = data[r, c] * wr[r] * wc[c]; // Apply outer-product coefficient.

        return result; // Return apodized matrix.
    }

    // Generates classic Hamming coefficients for length n.
    private static double[] HammingWindow(int n)
    {
        var w = new double[n]; // Output coefficient vector.

        for (int i = 0; i < n; i++)
            w[i] = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / (n - 1)); // Hamming formula.

        return w; // Return coefficient vector.
    }

    // Zero-pads matrix to new dimensions while centering original content.
    private static Complex[,] ZeroPad(Complex[,] data, int newRows, int newCols)
    {
        int rows = data.GetLength(0); // Source rows.
        int cols = data.GetLength(1); // Source cols.

        if (newRows < rows || newCols < cols) // Zero-pad cannot shrink dimensions.
            throw new ArgumentException("Zero-pad target must be >= input dimensions");

        int pr = (newRows - rows) / 2; // Row insertion offset.
        int pc = (newCols - cols) / 2; // Column insertion offset.

        var result = new Complex[newRows, newCols]; // Padded matrix initialized to zeros.

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
            result[r + pr, c + pc] = data[r, c]; // Copy source into centered region.

        return result; // Return padded matrix.
    }
}
