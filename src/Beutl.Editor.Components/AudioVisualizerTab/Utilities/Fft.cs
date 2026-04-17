using System.Numerics;

namespace Beutl.Editor.Components.AudioVisualizerTab.Utilities;

internal static class Fft
{
    public static void ApplyHannWindow(Span<float> samples)
    {
        int n = samples.Length;
        if (n < 2) return;
        double scale = 2.0 * Math.PI / (n - 1);
        for (int i = 0; i < n; i++)
        {
            float w = (float)(0.5 * (1.0 - Math.Cos(scale * i)));
            samples[i] *= w;
        }
    }

    // In-place radix-2 iterative Cooley–Tukey. Length must be a power of two.
    public static void Forward(Span<Complex> data)
    {
        int n = data.Length;
        if (n <= 1) return;
        if ((n & (n - 1)) != 0)
            throw new ArgumentException("FFT length must be a power of two.", nameof(data));

        // Bit reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j &= ~bit;
            }
            j |= bit;
            if (i < j)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }

        for (int size = 2; size <= n; size <<= 1)
        {
            int half = size >> 1;
            double angle = -2.0 * Math.PI / size;
            var wStep = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (int start = 0; start < n; start += size)
            {
                Complex w = Complex.One;
                for (int k = 0; k < half; k++)
                {
                    Complex even = data[start + k];
                    Complex odd = data[start + k + half] * w;
                    data[start + k] = even + odd;
                    data[start + k + half] = even - odd;
                    w *= wStep;
                }
            }
        }
    }

    public static float Magnitude(Complex c) => (float)c.Magnitude;

    // Converts magnitude to dBFS, clamped at `minDb` (e.g. -90) to avoid -inf.
    public static float ToDecibels(float magnitude, float reference, float minDb = -90f)
    {
        if (magnitude <= 0f || reference <= 0f) return minDb;
        float db = 20f * MathF.Log10(magnitude / reference);
        return db < minDb ? minDb : db;
    }
}
