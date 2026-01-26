namespace Beutl.Audio.Effects.Equalizer;

/// <summary>
/// Implementation of a BiQuad (second-order IIR) filter.
/// </summary>
public sealed class BiQuadFilter
{
    // Normalized filter coefficients
    private float _b0, _b1, _b2;
    private float _a1, _a2;

    // State variables (Direct Form II Transposed)
    private float _z1, _z2;

    // Current settings
    private BiQuadFilterType _type;
    private float _frequency;
    private float _q;
    private float _gainDb;
    private int _sampleRate;

    /// <summary>
    /// Calculates the filter coefficients.
    /// </summary>
    /// <param name="type">Filter type.</param>
    /// <param name="frequency">Center frequency in Hz.</param>
    /// <param name="q">Q factor (determines bandwidth).</param>
    /// <param name="gainDb">Gain in dB - used for Peak, LowShelf, and HighShelf filters.</param>
    /// <param name="sampleRate">Sample rate.</param>
    public void CalculateCoefficients(BiQuadFilterType type, float frequency, float q, float gainDb, int sampleRate)
    {
        // Skip recalculation if settings haven't changed
        if (_type == type && Math.Abs(_frequency - frequency) < 0.001f &&
            Math.Abs(_q - q) < 0.001f && Math.Abs(_gainDb - gainDb) < 0.001f &&
            _sampleRate == sampleRate)
        {
            return;
        }

        _type = type;
        _frequency = frequency;
        _q = q;
        _gainDb = gainDb;
        _sampleRate = sampleRate;

        // Clamp frequency to valid range
        frequency = Math.Clamp(frequency, 20f, sampleRate / 2f - 1f);
        q = Math.Max(q, 0.1f);

        // Normalized angular frequency
        float omega = 2f * MathF.PI * frequency / sampleRate;
        float sinOmega = MathF.Sin(omega);
        float cosOmega = MathF.Cos(omega);
        float alpha = sinOmega / (2f * q);

        // Gain coefficient (convert from dB to linear)
        float A = MathF.Pow(10f, gainDb / 40f); // sqrt(10^(dB/20))

        float b0, b1, b2, a0, a1, a2;

        switch (type)
        {
            case BiQuadFilterType.LowPass:
                b0 = (1f - cosOmega) / 2f;
                b1 = 1f - cosOmega;
                b2 = (1f - cosOmega) / 2f;
                a0 = 1f + alpha;
                a1 = -2f * cosOmega;
                a2 = 1f - alpha;
                break;

            case BiQuadFilterType.HighPass:
                b0 = (1f + cosOmega) / 2f;
                b1 = -(1f + cosOmega);
                b2 = (1f + cosOmega) / 2f;
                a0 = 1f + alpha;
                a1 = -2f * cosOmega;
                a2 = 1f - alpha;
                break;

            case BiQuadFilterType.BandPass:
                b0 = alpha;
                b1 = 0f;
                b2 = -alpha;
                a0 = 1f + alpha;
                a1 = -2f * cosOmega;
                a2 = 1f - alpha;
                break;

            case BiQuadFilterType.Notch:
                b0 = 1f;
                b1 = -2f * cosOmega;
                b2 = 1f;
                a0 = 1f + alpha;
                a1 = -2f * cosOmega;
                a2 = 1f - alpha;
                break;

            case BiQuadFilterType.Peak:
                b0 = 1f + alpha * A;
                b1 = -2f * cosOmega;
                b2 = 1f - alpha * A;
                a0 = 1f + alpha / A;
                a1 = -2f * cosOmega;
                a2 = 1f - alpha / A;
                break;

            case BiQuadFilterType.LowShelf:
                {
                    float sqrtA = MathF.Sqrt(A);
                    float sqrtA2Alpha = 2f * sqrtA * alpha;
                    b0 = A * ((A + 1f) - (A - 1f) * cosOmega + sqrtA2Alpha);
                    b1 = 2f * A * ((A - 1f) - (A + 1f) * cosOmega);
                    b2 = A * ((A + 1f) - (A - 1f) * cosOmega - sqrtA2Alpha);
                    a0 = (A + 1f) + (A - 1f) * cosOmega + sqrtA2Alpha;
                    a1 = -2f * ((A - 1f) + (A + 1f) * cosOmega);
                    a2 = (A + 1f) + (A - 1f) * cosOmega - sqrtA2Alpha;
                }
                break;

            case BiQuadFilterType.HighShelf:
                {
                    float sqrtA = MathF.Sqrt(A);
                    float sqrtA2Alpha = 2f * sqrtA * alpha;
                    b0 = A * ((A + 1f) + (A - 1f) * cosOmega + sqrtA2Alpha);
                    b1 = -2f * A * ((A - 1f) + (A + 1f) * cosOmega);
                    b2 = A * ((A + 1f) + (A - 1f) * cosOmega - sqrtA2Alpha);
                    a0 = (A + 1f) - (A - 1f) * cosOmega + sqrtA2Alpha;
                    a1 = 2f * ((A - 1f) - (A + 1f) * cosOmega);
                    a2 = (A + 1f) - (A - 1f) * cosOmega - sqrtA2Alpha;
                }
                break;

            default:
                // Pass-through
                _b0 = 1f;
                _b1 = 0f;
                _b2 = 0f;
                _a1 = 0f;
                _a2 = 0f;
                return;
        }

        // Normalize by a0
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    /// <summary>
    /// Processes a single sample.
    /// </summary>
    /// <param name="input">Input sample.</param>
    /// <returns>Filtered sample.</returns>
    public float Process(float input)
    {
        // Direct Form II Transposed
        float output = _b0 * input + _z1;
        _z1 = _b1 * input - _a1 * output + _z2;
        _z2 = _b2 * input - _a2 * output;
        return output;
    }

    /// <summary>
    /// Processes an entire buffer.
    /// </summary>
    /// <param name="input">Input buffer.</param>
    /// <param name="output">Output buffer.</param>
    public void ProcessBuffer(ReadOnlySpan<float> input, Span<float> output)
    {
        if (input.Length != output.Length)
            throw new ArgumentException("Input and output buffers must have the same length.");

        for (int i = 0; i < input.Length; i++)
        {
            output[i] = Process(input[i]);
        }
    }

    /// <summary>
    /// Resets the filter state.
    /// </summary>
    public void Reset()
    {
        _z1 = 0f;
        _z2 = 0f;
    }

    /// <summary>
    /// Creates a new instance of the filter with the same coefficients.
    /// </summary>
    /// <returns>A new BiQuadFilter instance.</returns>
    public BiQuadFilter Clone()
    {
        return new BiQuadFilter
        {
            _b0 = _b0,
            _b1 = _b1,
            _b2 = _b2,
            _a1 = _a1,
            _a2 = _a2,
            _type = _type,
            _frequency = _frequency,
            _q = _q,
            _gainDb = _gainDb,
            _sampleRate = _sampleRate
            // State variables are reset
        };
    }
}
