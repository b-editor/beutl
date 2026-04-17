namespace Beutl.Audio.Graph;

internal static class Fft
{
    public static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    public static int ClampToPowerOfTwo(int value, int min = 64, int max = 16384)
    {
        if (value < min) value = min;
        if (value > max) value = max;
        int p = 1;
        while (p < value) p <<= 1;
        if (p > max) p >>= 1;
        return p;
    }

    public static void ApplyHann(Span<float> window)
    {
        int n = window.Length;
        if (n <= 1) return;
        double factor = 2.0 * Math.PI / (n - 1);
        for (int i = 0; i < n; i++)
        {
            window[i] *= (float)(0.5 * (1.0 - Math.Cos(factor * i)));
        }
    }

    public static void Forward(Span<float> real, Span<float> imag)
    {
        int n = real.Length;
        if (n != imag.Length) throw new ArgumentException("real and imag must have the same length.");
        if (!IsPowerOfTwo(n)) throw new ArgumentException("length must be a power of two.", nameof(real));
        if (n <= 1) return;

        // Bit-reversal permutation
        int j = 0;
        for (int i = 1; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        // Cooley-Tukey butterflies
        for (int size = 2; size <= n; size <<= 1)
        {
            int half = size >> 1;
            double theta = -2.0 * Math.PI / size;
            float wStepReal = (float)Math.Cos(theta);
            float wStepImag = (float)Math.Sin(theta);

            for (int start = 0; start < n; start += size)
            {
                float wr = 1f;
                float wi = 0f;
                for (int k = 0; k < half; k++)
                {
                    int evenIdx = start + k;
                    int oddIdx = evenIdx + half;

                    float tReal = wr * real[oddIdx] - wi * imag[oddIdx];
                    float tImag = wr * imag[oddIdx] + wi * real[oddIdx];

                    real[oddIdx] = real[evenIdx] - tReal;
                    imag[oddIdx] = imag[evenIdx] - tImag;
                    real[evenIdx] += tReal;
                    imag[evenIdx] += tImag;

                    float nextWr = wr * wStepReal - wi * wStepImag;
                    float nextWi = wr * wStepImag + wi * wStepReal;
                    wr = nextWr;
                    wi = nextWi;
                }
            }
        }
    }

    public static void Magnitudes(ReadOnlySpan<float> real, ReadOnlySpan<float> imag, Span<float> outMagnitudes)
    {
        int bins = outMagnitudes.Length;
        for (int i = 0; i < bins; i++)
        {
            float re = real[i];
            float im = imag[i];
            outMagnitudes[i] = MathF.Sqrt(re * re + im * im);
        }
    }

    public static float MagnitudeToDb(float magnitude, float reference)
    {
        if (magnitude <= 0f || reference <= 0f) return -160f;
        return 20f * MathF.Log10(magnitude / reference);
    }
}
