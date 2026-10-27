using Beutl.Audio.Graph;

namespace Beutl.Audio;

/// <summary>Windowed, normalized frequency magnitudes using the engine's audio FFT.</summary>
public static class AudioSpectrum
{
    public static void Calculate(ReadOnlySpan<float> samples, Span<float> magnitudes)
    {
        if (!Fft.IsPowerOfTwo(samples.Length) || magnitudes.Length != samples.Length / 2)
            throw new ArgumentException("The input must have a power-of-two length and the output half that length.");
        float[] real = samples.ToArray();
        float[] imaginary = new float[real.Length];
        Fft.ApplyHann(real);
        Fft.Forward(real, imaginary);
        Fft.Magnitudes(real, imaginary, magnitudes);
        for (int i = 0; i < magnitudes.Length; i++)
            magnitudes[i] *= 4f / real.Length;
    }
}
