using System.Numerics;
using System.Text;

namespace CardiacGatingMRI;

/// <summary>
/// Parses the custom binary .mrd format used by Bruker/MRS/Varian-style scanners.
///
/// Binary layout (all values little-endian):
///   Bytes   0 –  3 : int32  Nfe    (readout samples, i.e. columns in k-space)
///   Bytes   4 –  7 : int32  Npe    (phase-encode lines, i.e. rows)
///   Bytes   8 – 11 : int32  N3d
///   Bytes  12 – 15 : int32  Nslice
///   --- 2-byte gap ---
///   Bytes  18 – 19 : int16  dattype
///   --- 132-byte gap ---
///   Bytes 152 – 155 : int32  Nechoes
///   Bytes 156 – 159 : int32  Nexps
///   --- pad to byte 256 ---
///   Bytes 256 – 511 : char[256] description text
///   Bytes 512+      : interleaved float32 complex or real data (column-major / Fortran order)
///                     followed by 120-byte filename + remaining text params
/// </summary>
public sealed class MrdReader
{
    public MrdData Read(string path)
    {
        using var f = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read), Encoding.ASCII, false);

        // --- Header integers (little-endian int32) ---
        int nFe    = f.ReadInt32();
        int nPe    = f.ReadInt32();
        int n3d    = f.ReadInt32();
        int nSlice = f.ReadInt32();

        // 2-byte gap
        f.BaseStream.Seek(2, SeekOrigin.Current);

        short datTypeRaw = f.ReadInt16();
        string datTypeHex = datTypeRaw.ToString("X").PadLeft(2, '0');

        // 132-byte gap
        f.BaseStream.Seek(132, SeekOrigin.Current);

        int nEchoes = f.ReadInt32();
        int nExps   = f.ReadInt32();

        // Seek to byte 256
        long cl = f.BaseStream.Position;
        f.BaseStream.Seek(256 - cl, SeekOrigin.Current);

        // 256-byte description text
        byte[] commBytes = f.ReadBytes(256);
        string description = Encoding.ASCII.GetString(commBytes).TrimEnd('\0');

        // --- Data ---
        long numPts = (long)nFe * nPe * n3d * nSlice * nEchoes * nExps;
        bool isComplex   = datTypeHex[0] == '1';
        char dtypeCode   = datTypeHex[1];

        int bytesPerReal = dtypeCode switch
        {
            '0' => 1, // uchar
            '1' => 1, // schar
            '2' or '3' => 2, // int16
            '4' => 4, // int32
            '5' => 4, // float32
            '6' => 8, // float64
            _   => throw new InvalidDataException($"Unknown dattype nibble: {dtypeCode}")
        };

        long multiplier = isComplex ? 2L : 1L;
        long totalElements = numPts * multiplier;
        long totalBytes    = totalElements * bytesPerReal;

        byte[] rawBytes = f.ReadBytes((int)totalBytes);
        if (rawBytes.Length != totalBytes)
            throw new InvalidDataException($"Expected {totalBytes} data bytes, got {rawBytes.Length}");

        // Convert raw bytes → doubles (always work in float64 internally)
        double[] flat = ConvertToDoubles(rawBytes, dtypeCode, (int)totalElements);

        // Build complex[nFe, nPe] array from column-major (Fortran) order
        // For the common 2D case: dimension order = (Nfe, Npe, N3d, Nslice, Nechoes, Nexps)
        // With N3d=Nslice=Nechoes=Nexps=1, data is just (Nfe, Npe).
        var kspace = new Complex[nFe, nPe];
        if (isComplex)
        {
            // flat = [re0, im0, re1, im1, ...] in column-major (Nfe varies first)
            for (int pe = 0; pe < nPe; pe++)
            for (int fe = 0; fe < nFe; fe++)
            {
                long idx = ((long)pe * nFe + fe) * 2;
                kspace[fe, pe] = new Complex(flat[idx], flat[idx + 1]);
            }
        }
        else
        {
            for (int pe = 0; pe < nPe; pe++)
            for (int fe = 0; fe < nFe; fe++)
            {
                long idx = (long)pe * nFe + fe;
                kspace[fe, pe] = new Complex(flat[idx], 0.0);
            }
        }

        return new MrdData(kspace, nFe, nPe, description, Path.GetFileNameWithoutExtension(path));
    }

    private static double[] ConvertToDoubles(byte[] raw, char code, int count)
    {
        var result = new double[count];
        switch (code)
        {
            case '0': // uchar
                for (int i = 0; i < count; i++) result[i] = raw[i];
                break;
            case '1': // schar
                for (int i = 0; i < count; i++) result[i] = (sbyte)raw[i];
                break;
            case '2':
            case '3': // int16
                for (int i = 0; i < count; i++)
                    result[i] = BitConverter.ToInt16(raw, i * 2);
                break;
            case '4': // int32
                for (int i = 0; i < count; i++)
                    result[i] = BitConverter.ToInt32(raw, i * 4);
                break;
            case '5': // float32
                for (int i = 0; i < count; i++)
                    result[i] = BitConverter.ToSingle(raw, i * 4);
                break;
            case '6': // float64
                for (int i = 0; i < count; i++)
                    result[i] = BitConverter.ToDouble(raw, i * 8);
                break;
        }
        return result;
    }
}

/// <summary>
/// Holds the k-space matrix for a single 2-D scan loaded from an .mrd file.
/// kspace[fe, pe] – fe = frequency-encode index (column), pe = phase-encode index (row).
/// A "readout line" corresponds to a fixed pe index: all nFe samples at that pe.
/// </summary>
public sealed class MrdData
{
    public Complex[,] KSpace     { get; }
    public int         Nfe        { get; }
    public int         Npe        { get; }
    public string      Description { get; }
    public string      FileName   { get; }

    public MrdData(Complex[,] kspace, int nfe, int npe, string description, string fileName)
    {
        KSpace      = kspace;
        Nfe         = nfe;
        Npe         = npe;
        Description = description;
        FileName    = fileName;
    }

    /// <summary>Returns a copy of all nFe samples for phase-encode line <paramref name="peIndex"/> (0-based).</summary>
    public Complex[] GetLine(int peIndex)
    {
        var line = new Complex[Nfe];
        for (int fe = 0; fe < Nfe; fe++)
            line[fe] = KSpace[fe, peIndex];
        return line;
    }

    /// <summary>Writes all nFe samples of phase-encode line <paramref name="peIndex"/> into this k-space.</summary>
    public void SetLine(int peIndex, Complex[] line)
    {
        if (line.Length != Nfe)
            throw new ArgumentException($"Line length {line.Length} != Nfe {Nfe}");
        for (int fe = 0; fe < Nfe; fe++)
            KSpace[fe, peIndex] = line[fe];
    }

    /// <summary>Deep copy so we can mutate without touching the original.</summary>
    public MrdData Clone()
    {
        var copy = (Complex[,])KSpace.Clone();
        return new MrdData(copy, Nfe, Npe, Description, FileName);
    }
}
