using System.Numerics;
using System.Text;

namespace CardiacGatingMRI;

public sealed class MrdReader
{
    public MrdData Read(string path)
    {
        using var f = new BinaryReader(
            File.Open(path, FileMode.Open, FileAccess.Read),
            Encoding.ASCII,
            false);

        int nFe = f.ReadInt32();
        int nPe = f.ReadInt32();
        int n3d = f.ReadInt32();
        int nSlice = f.ReadInt32();

        f.BaseStream.Seek(2, SeekOrigin.Current);

        short datTypeRaw = f.ReadInt16();
        string datTypeHex = datTypeRaw.ToString("X").PadLeft(2, '0');

        f.BaseStream.Seek(132, SeekOrigin.Current);

        int nEchoes = f.ReadInt32();
        int nExps = f.ReadInt32();

        long cl = f.BaseStream.Position;
        f.BaseStream.Seek(256 - cl, SeekOrigin.Current);

        byte[] commBytes = f.ReadBytes(256);
        string description = Encoding.ASCII.GetString(commBytes).TrimEnd('\0');

        long numPts = (long)nFe * nPe * n3d * nSlice * nEchoes * nExps;
        bool isComplex = datTypeHex[0] == '1';
        char dtypeCode = datTypeHex[1];

        int bytesPerReal = dtypeCode switch
        {
            '0' => 1,
            '1' => 1,
            '2' or '3' => 2,
            '4' => 4,
            '5' => 4,
            '6' => 8,
            _ => throw new InvalidDataException($"Unknown dattype nibble: {dtypeCode}")
        };

        long multiplier = isComplex ? 2L : 1L;
        long totalElements = numPts * multiplier;
        long totalBytes = totalElements * bytesPerReal;

        byte[] rawBytes = f.ReadBytes((int)totalBytes);
        if (rawBytes.Length != totalBytes)
            throw new InvalidDataException($"Expected {totalBytes} data bytes, got {rawBytes.Length}");

        double[] flat = ConvertToDoubles(rawBytes, dtypeCode, (int)totalElements);

        var kspace = new Complex[nFe, nPe];

        if (isComplex)
        {
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

        return new MrdData(
            kspace,
            nFe,
            nPe,
            description,
            Path.GetFileNameWithoutExtension(path));
    }

    private static double[] ConvertToDoubles(byte[] raw, char code, int count)
    {
        var result = new double[count];

        switch (code)
        {
            case '0':
                for (int i = 0; i < count; i++) result[i] = raw[i];
                break;
            case '1':
                for (int i = 0; i < count; i++) result[i] = (sbyte)raw[i];
                break;
            case '2':
            case '3':
                for (int i = 0; i < count; i++) result[i] = BitConverter.ToInt16(raw, i * 2);
                break;
            case '4':
                for (int i = 0; i < count; i++) result[i] = BitConverter.ToInt32(raw, i * 4);
                break;
            case '5':
                for (int i = 0; i < count; i++) result[i] = BitConverter.ToSingle(raw, i * 4);
                break;
            case '6':
                for (int i = 0; i < count; i++) result[i] = BitConverter.ToDouble(raw, i * 8);
                break;
        }

        return result;
    }
}

public sealed class MrdData
{
    public Complex[,] KSpace { get; }
    public int Nfe { get; }
    public int Npe { get; }
    public string Description { get; }
    public string FileName { get; }

    public MrdData(Complex[,] kspace, int nfe, int npe, string description, string fileName)
    {
        KSpace = kspace;
        Nfe = nfe;
        Npe = npe;
        Description = description;
        FileName = fileName;
    }

    public Complex[] GetLine(int peIndex)
    {
        var line = new Complex[Nfe];
        for (int fe = 0; fe < Nfe; fe++)
            line[fe] = KSpace[fe, peIndex];
        return line;
    }

    public void SetLine(int peIndex, Complex[] line)
    {
        if (line.Length != Nfe)
            throw new ArgumentException($"Line length {line.Length} != Nfe {Nfe}");

        for (int fe = 0; fe < Nfe; fe++)
            KSpace[fe, peIndex] = line[fe];
    }

    public MrdData Clone()
    {
        var copy = (Complex[,])KSpace.Clone();
        return new MrdData(copy, Nfe, Npe, Description, FileName);
    }
}
