using Beutl.Audio.Effects;

namespace Beutl.Audio.Graph;

// The weights a ResamplingNode spreads over neighbouring samples to find a value between them. Every kernel passes
// through the samples themselves, so an output that falls on an input sample is that sample.
internal sealed class ResamplingKernel
{
    // Entries of a sinc table per sample of distance, read with linear interpolation (about -100 dB of error).
    private const int TableResolution = 512;

    private static readonly ResamplingKernel s_hold = new(ResamplingMethod.Hold, 0, 0, null);
    private static readonly ResamplingKernel s_linear = new(ResamplingMethod.Linear, 0, 1, null);
    private static readonly ResamplingKernel s_catmullRom = new(ResamplingMethod.CatmullRom, -1, 2, null);
    private static readonly ResamplingKernel s_lagrange = new(ResamplingMethod.Lagrange, -1, 2, null);

    // Kaiser windows. 16 samples stay flat to 80% of the lower rate's Nyquist frequency and reach about -53 dB past
    // 120% of it; 64 samples stay flat to 90% and reach about -97 dB past 120%.
    private static readonly Lazy<ResamplingKernel> s_sinc16 = new(() => CreateSinc(ResamplingMethod.Sinc16, 8, 5));
    private static readonly Lazy<ResamplingKernel> s_sinc64 = new(() => CreateSinc(ResamplingMethod.Sinc64, 32, 9));

    private readonly float[]? _table;

    private ResamplingKernel(ResamplingMethod method, int first, int last, float[]? table)
    {
        Method = method;
        First = first;
        Last = last;
        _table = table;
    }

    public ResamplingMethod Method { get; }

    // The samples a value depends on, as offsets from the one at or before its position.
    public int First { get; }

    public int Last { get; }

    public int Taps => Last - First + 1;

    // A sinc kernel narrows to a lower rate by stretching over more samples of the input.
    public bool IsSinc => _table != null;

    // How many samples from its center a sinc kernel reaches zero.
    public int Radius => Last;

    // An unknown method, from a newer version or a hand-edited file, holds like the default.
    public static ResamplingKernel For(ResamplingMethod method) => method switch
    {
        ResamplingMethod.Linear => s_linear,
        ResamplingMethod.CatmullRom => s_catmullRom,
        ResamplingMethod.Lagrange => s_lagrange,
        ResamplingMethod.Sinc16 => s_sinc16.Value,
        ResamplingMethod.Sinc64 => s_sinc64.Value,
        _ => s_hold,
    };

    // The weights of the samples First..Last for a position a fraction (0 <= fraction < 1) past the sample at offset
    // 0. They add up to one, so a constant passes through unchanged.
    public void GetWeights(double fraction, Span<float> weights)
    {
        double f = fraction;
        switch (Method)
        {
            case ResamplingMethod.Hold:
                weights[0] = 1;
                break;
            case ResamplingMethod.Linear:
                weights[0] = (float)(1 - f);
                weights[1] = (float)f;
                break;
            case ResamplingMethod.CatmullRom:
                weights[0] = (float)(-f * (1 - f) * (1 - f) / 2);
                weights[1] = (float)((3 * f * f * f - 5 * f * f + 2) / 2);
                weights[2] = (float)((-3 * f * f * f + 4 * f * f + f) / 2);
                weights[3] = (float)(-f * f * (1 - f) / 2);
                break;
            case ResamplingMethod.Lagrange:
                weights[0] = (float)(-f * (f - 1) * (f - 2) / 6);
                weights[1] = (float)((f + 1) * (f - 1) * (f - 2) / 2);
                weights[2] = (float)(-(f + 1) * f * (f - 2) / 2);
                weights[3] = (float)((f + 1) * f * (f - 1) / 6);
                break;
            default:
                double sum = 0;
                for (int i = 0; i < weights.Length; i++)
                {
                    float weight = Value(Math.Abs(f - (First + i)));
                    weights[i] = weight;
                    sum += weight;
                }

                // The window leaves the sum a little off one between samples, which would ripple a constant.
                float scale = (float)(1 / sum);
                for (int i = 0; i < weights.Length; i++)
                    weights[i] *= scale;
                break;
        }
    }

    // The windowed sinc at a distance in samples, zero from the radius on.
    public float Value(double distance)
    {
        float[] table = _table!;
        double index = distance * TableResolution;
        if (index >= table.Length - 1)
            return 0;

        int whole = (int)index;
        float near = table[whole];
        return near + (table[whole + 1] - near) * (float)(index - whole);
    }

    private static ResamplingKernel CreateSinc(ResamplingMethod method, int radius, double beta)
    {
        var table = new float[radius * TableResolution + 1];
        double norm = BesselI0(beta);
        for (int i = 0; i < table.Length; i++)
        {
            // Exactly one at the center and zero at every other sample, so the kernel passes through the samples.
            if (i % TableResolution == 0)
            {
                table[i] = i == 0 ? 1 : 0;
                continue;
            }

            double distance = (double)i / TableResolution;
            double x = distance / radius;
            double window = BesselI0(beta * Math.Sqrt(1 - x * x)) / norm;
            table[i] = (float)(Math.Sin(Math.PI * distance) / (Math.PI * distance) * window);
        }

        return new ResamplingKernel(method, 1 - radius, radius, table);
    }

    private static double BesselI0(double x)
    {
        double sum = 1;
        double term = 1;
        double quarter = x * x / 4;
        for (int k = 1; k < 100 && term > sum * 1e-17; k++)
        {
            term *= quarter / ((double)k * k);
            sum += term;
        }

        return sum;
    }
}
