namespace Beutl.Editor.Components.AudioVisualizerTab.Utilities;

// Direct-form II transposed biquad. Used for the K-weighting filters that
// BS.1770 specifies for loudness measurement.
internal sealed class Biquad
{
    public float B0 { get; private set; } = 1f;
    public float B1 { get; private set; }
    public float B2 { get; private set; }
    public float A1 { get; private set; }
    public float A2 { get; private set; }

    private float _z1;
    private float _z2;

    public void SetCoefficients(double b0, double b1, double b2, double a1, double a2)
    {
        B0 = (float)b0;
        B1 = (float)b1;
        B2 = (float)b2;
        A1 = (float)a1;
        A2 = (float)a2;
    }

    public void Reset()
    {
        _z1 = 0f;
        _z2 = 0f;
    }

    public float ProcessOne(float x)
    {
        float y = B0 * x + _z1;
        _z1 = B1 * x - A1 * y + _z2;
        _z2 = B2 * x - A2 * y;
        return y;
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        float b0 = B0, b1 = B1, b2 = B2, a1 = A1, a2 = A2;
        float z1 = _z1, z2 = _z2;
        for (int i = 0; i < input.Length; i++)
        {
            float x = input[i];
            float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            output[i] = y;
        }
        _z1 = z1;
        _z2 = z2;
    }
}
