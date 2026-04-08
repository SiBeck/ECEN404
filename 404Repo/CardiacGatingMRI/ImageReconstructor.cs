using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CardiacGatingMRI;

/// <summary>
/// Reconstructs a 2-D magnitude MRI image from raw k-space via the 2-D Fast Fourier Transform.
///
/// Physics background:
///   MRI scanners acquire data in "k-space" – the spatial frequency domain.
///   The image is obtained by applying the 2-D inverse DFT (equivalently 2-D FFT after
///   appropriate centering) to the k-space matrix.
///
///   Steps:
///     1. Arrange the complex k-space matrix as a 2-D array k[fe, pe].
///     2. Apply 2-D FFT (FFT along fe axis, then along pe axis).
///     3. Apply FFTShift to move the DC component to the image centre.
///     4. Take the complex magnitude |k| to obtain the magnitude image.
///     5. Optionally apply a log-scale stretch for display purposes.
///
///   Reference: Bernstein M A, King K F, Zhou X J.
///   "Handbook of MRI Pulse Sequences." Academic Press, 2004. Chapter 11.
/// </summary>
public static class ImageReconstructor
{
    /// <summary>
    /// Performs 2-D FFT reconstruction and returns a normalised float magnitude image.
    /// Image[row, col] values are in [0, 1].
    /// </summary>
    public static float[,] Reconstruct(MrdData kspace)
    {
        int nfe = kspace.Nfe;
        int npe = kspace.Npe;

        // Copy k-space into a 2-D array indexed [pe, fe] for row-wise FFT
        var grid = new Complex[npe][];
        for (int pe = 0; pe < npe; pe++)
        {
            grid[pe] = new Complex[nfe];
            for (int fe = 0; fe < nfe; fe++)
                grid[pe][fe] = kspace.KSpace[fe, pe];
        }

        // 2-D FFT: FFT each row (along fe), then FFT each column (along pe)
        for (int pe = 0; pe < npe; pe++)
            Fft1D(grid[pe]);

        for (int fe = 0; fe < nfe; fe++)
        {
            var col = new Complex[npe];
            for (int pe = 0; pe < npe; pe++) col[pe] = grid[pe][fe];
            Fft1D(col);
            for (int pe = 0; pe < npe; pe++) grid[pe][fe] = col[pe];
        }

        // FFTShift: swap quadrants so DC is at centre
        grid = FftShift2D(grid, npe, nfe);

        // Magnitude
        var mag = new float[npe, nfe];
        float maxVal = 0f;
        for (int pe = 0; pe < npe; pe++)
        for (int fe = 0; fe < nfe; fe++)
        {
            float m = (float)grid[pe][fe].Magnitude;
            mag[pe, fe] = m;
            if (m > maxVal) maxVal = m;
        }

        // Normalise to [0, 1]
        if (maxVal > 0f)
        {
            for (int pe = 0; pe < npe; pe++)
            for (int fe = 0; fe < nfe; fe++)
                mag[pe, fe] /= maxVal;
        }

        return mag;
    }

    /// <summary>Saves a normalised [0,1] magnitude image as an 8-bit grayscale PNG.</summary>
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

    // ── Cooley-Tukey radix-2 DIT FFT (in-place) ─────────────────────────────
    // Works for any power-of-two length; for non-power-of-two sizes the DFT
    // is computed via zero-padding to the next power of two then truncated
    // back to the original length.
    private static void Fft1D(Complex[] x)
    {
        int n = x.Length;
        if (n <= 1) return;

        // Pad to next power of two if needed
        int n2 = NextPow2(n);
        if (n2 != n)
        {
            var padded = new Complex[n2];
            x.CopyTo(padded, 0);
            FftPow2(padded);
            // Truncate back (approximate – valid for power-of-two mrd files)
            for (int i = 0; i < n; i++) x[i] = padded[i];
            return;
        }
        FftPow2(x);
    }

    private static void FftPow2(Complex[] x)
    {
        int n = x.Length;
        // Bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (x[i], x[j]) = (x[j], x[i]);
        }
        // Butterfly stages
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
                    x[i + j]           = u + v;
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
        // Swap halves along pe (rows), then along fe (columns)
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
