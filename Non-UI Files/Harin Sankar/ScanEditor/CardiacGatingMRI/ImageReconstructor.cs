using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CardiacGatingMRI;

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
            var col = new Complex[npe];
            for (int pe = 0; pe < npe; pe++) col[pe] = grid[pe][fe];
            Fft1D(col);
            for (int pe = 0; pe < npe; pe++) grid[pe][fe] = col[pe];
        }

        grid = FftShift2D(grid, npe, nfe);

        var mag = new float[npe, nfe];
        float maxVal = 0f;
        for (int pe = 0; pe < npe; pe++)
        for (int fe = 0; fe < nfe; fe++)
        {
            float m = (float)grid[pe][fe].Magnitude;
            mag[pe, fe] = m;
            if (m > maxVal) maxVal = m;
        }

        if (maxVal > 0f)
        {
            for (int pe = 0; pe < npe; pe++)
            for (int fe = 0; fe < nfe; fe++)
                mag[pe, fe] /= maxVal;
        }

        return mag;
    }

    public static void SavePng(float[,] magnitude, string outputPath)
    {
        int npe = magnitude.GetLength(0);
        int nfe = magnitude.GetLength(1);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        using var img = new Image<L8>(nfe, npe);
        for (int row = 0; row < npe; row++)
        for (int col = 0; col < nfe; col++)
        {
            byte v = (byte)Math.Clamp((int)(magnitude[row, col] * 255f), 0, 255);
            img[col, row] = new L8(v);
        }
        img.Save(outputPath);
    }

    private static void Fft1D(Complex[] x)
    {
        int n = x.Length;
        if (n <= 1) return;

        int n2 = NextPow2(n);
        if (n2 != n)
        {
            var padded = new Complex[n2];
            x.CopyTo(padded, 0);
            FftPow2(padded);
            for (int i = 0; i < n; i++) x[i] = padded[i];
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
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (x[i], x[j]) = (x[j], x[i]);
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            var wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
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
        while (p < n) p <<= 1;
        return p;
    }

    private static Complex[][] FftShift2D(Complex[][] grid, int npe, int nfe)
    {
        int halfPe = npe / 2;
        int halfFe = nfe / 2;

        var shifted = new Complex[npe][];
        for (int pe = 0; pe < npe; pe++)
            shifted[pe] = new Complex[nfe];

        for (int pe = 0; pe < npe; pe++)
        {
            int newPe = (pe + halfPe) % npe;
            for (int fe = 0; fe < nfe; fe++)
            {
                int newFe = (fe + halfFe) % nfe;
                shifted[newPe][newFe] = grid[pe][fe];
            }
        }

        return shifted;
    }
}
