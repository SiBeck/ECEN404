using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OfficialGatingProgram;

public static class ImageReconstructor
{
    public static float[,] Reconstruct(MrdData kspace)
    {
        int nfe = kspace.Nfe;
        int npe = kspace.Npe;

        var grid = new Complex[npe][];
        for (int pe = 0; pe < npe; pe++)
        {
            grid[pe] = new Complex[nfe];
            for (int fe = 0; fe < nfe; fe++)
                grid[pe][fe] = kspace.KSpace[fe, pe];
        }

        for (int pe = 0; pe < npe; pe++)
            Fft1D(grid[pe]);

        for (int fe = 0; fe < nfe; fe++)
        {
            var column = new Complex[npe];
            for (int pe = 0; pe < npe; pe++)
                column[pe] = grid[pe][fe];

            Fft1D(column);

            for (int pe = 0; pe < npe; pe++)
                grid[pe][fe] = column[pe];
        }

        grid = FftShift2D(grid, npe, nfe);

        var magnitude = new float[npe, nfe];
        float maxValue = 0f;
        for (int pe = 0; pe < npe; pe++)
        for (int fe = 0; fe < nfe; fe++)
        {
            float value = (float)grid[pe][fe].Magnitude;
            magnitude[pe, fe] = value;
            if (value > maxValue)
                maxValue = value;
        }

        if (maxValue > 0f)
        {
            for (int pe = 0; pe < npe; pe++)
            for (int fe = 0; fe < nfe; fe++)
                magnitude[pe, fe] /= maxValue;
        }

        return magnitude;
    }

    public static void SavePng(float[,] magnitude, string outputPath)
    {
        int rows = magnitude.GetLength(0);
        int cols = magnitude.GetLength(1);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        using var image = new Image<L8>(cols, rows);
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            byte value = (byte)Math.Clamp((int)(magnitude[row, col] * 255f), 0, 255);
            image[col, row] = new L8(value);
        }

        image.Save(outputPath);
    }

    private static void Fft1D(Complex[] x)
    {
        int n = x.Length;
        if (n <= 1)
            return;

        int nextPow2 = NextPow2(n);
        if (nextPow2 != n)
        {
            var padded = new Complex[nextPow2];
            x.CopyTo(padded, 0);
            FftPow2(padded);
            for (int i = 0; i < n; i++)
                x[i] = padded[i];
            return;
        }

        FftPow2(x);
    }

    private static void FftPow2(Complex[] x)
    {
        int n = x.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
                (x[i], x[j]) = (x[j], x[i]);
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            var wlen = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (int i = 0; i < n; i += len)
            {
                Complex w = Complex.One;
                for (int j = 0; j < len / 2; j++)
                {
                    Complex u = x[i + j];
                    Complex v = x[i + j + len / 2] * w;
                    x[i + j] = u + v;
                    x[i + j + len / 2] = u - v;
                    w *= wlen;
                }
            }
        }
    }

    private static int NextPow2(int n)
    {
        int p = 1;
        while (p < n)
            p <<= 1;
        return p;
    }

    private static Complex[][] FftShift2D(Complex[][] grid, int rows, int cols)
    {
        int halfRows = rows / 2;
        int halfCols = cols / 2;
        var shifted = new Complex[rows][];

        for (int row = 0; row < rows; row++)
            shifted[row] = new Complex[cols];

        for (int row = 0; row < rows; row++)
        {
            int newRow = (row + halfRows) % rows;
            for (int col = 0; col < cols; col++)
            {
                int newCol = (col + halfCols) % cols;
                shifted[newRow][newCol] = grid[row][col];
            }
        }

        return shifted;
    }
}
