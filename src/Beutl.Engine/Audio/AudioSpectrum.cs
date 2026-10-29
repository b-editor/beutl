using System.Buffers;
using Beutl.Audio.Graph;

namespace Beutl.Audio;

/// <summary>Windowed, normalized frequency magnitudes using the engine's audio FFT.</summary>
public static class AudioSpectrum
{
    /// <summary>Calculates the DC and positive-frequency bins, excluding Nyquist, from at least four samples.</summary>
    public static void Calculate(ReadOnlySpan<float> samples, Span<float> magnitudes)
    {
        if (samples.Length < 4 || !Fft.IsPowerOfTwo(samples.Length) || magnitudes.Length != samples.Length / 2)
            throw new ArgumentException("The input must have a power-of-two length of at least four and the output half that length.");
        // Called per frame by visualizers, so the transform's working buffers are pooled.
        float[] buffer = ArrayPool<float>.Shared.Rent(samples.Length * 2);
        try
        {
            Span<float> real = buffer.AsSpan(0, samples.Length);
            Span<float> imaginary = buffer.AsSpan(samples.Length, samples.Length);
            samples.CopyTo(real);
            imaginary.Clear();
            Fft.ApplyHann(real);
            Fft.Forward(real, imaginary);
            Fft.Magnitudes(real, imaginary, magnitudes);
        }
        finally { ArrayPool<float>.Shared.Return(buffer); }

        // The symmetric Hann window sums to (N - 1) / 2. Only the non-DC bins
        // combine positive and negative frequencies into a single-sided amplitude.
        float scale = 2f / (samples.Length - 1);
        magnitudes[0] *= scale;
        for (int i = 1; i < magnitudes.Length; i++)
            magnitudes[i] *= 2 * scale;
    }
}
