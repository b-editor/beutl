namespace Beutl.Graphics.AudioVisualizers;

/// <summary>
/// The frequency range a spectrum visualizer spreads its FFT bins over: from 20 Hz, or one bin if that is wider, up
/// to the Nyquist frequency, with both ends also expressed in mels when the visualizer uses the mel scale.
/// </summary>
internal readonly record struct FrequencyAxis(float FMin, float FMax, double MelMin, double MelMax)
{
    public static FrequencyAxis Create(FrequencyScale scale, int sampleRate, int bins)
    {
        float fMax = sampleRate * 0.5f;
        float fMin = MathF.Max(20f, fMax / bins);
        double melMin = scale == FrequencyScale.Mel ? 2595.0 * Math.Log10(1 + fMin / 700.0) : 0;
        double melMax = scale == FrequencyScale.Mel ? 2595.0 * Math.Log10(1 + fMax / 700.0) : 0;
        return new FrequencyAxis(fMin, fMax, melMin, melMax);
    }

    /// <summary>The frequency at the fraction <paramref name="t"/> of the axis on <paramref name="scale"/>.</summary>
    public double FrequencyAt(double t, FrequencyScale scale)
        => scale switch
        {
            FrequencyScale.Logarithmic => FMin * Math.Pow(FMax / FMin, t),
            FrequencyScale.Mel => MelToFrequency(MelMin + (MelMax - MelMin) * t),
            _ => t * FMax,
        };

    /// <summary>Inverts the mel scale that <see cref="Create"/> expresses the axis ends in.</summary>
    public static double MelToFrequency(double mel) => 700.0 * (Math.Pow(10, mel / 2595.0) - 1);
}
