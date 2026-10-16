namespace Beutl.Audio;

// Converts the engine's stereo buffers to the interleaved layouts that devices and encoders take.
public static class AudioConversion
{
    public static void Interleave(AudioBuffer source, Span<float> destination)
    {
        ThrowIfCannotInterleave(source, destination.Length);
        Span<float> left = source.GetChannelData(0);
        Span<float> right = source.GetChannelData(1);
        for (int i = 0; i < left.Length; i++)
        {
            destination[2 * i] = left[i];
            destination[(2 * i) + 1] = right[i];
        }
    }

    public static void Interleave(AudioBuffer source, Span<short> destination)
    {
        ThrowIfCannotInterleave(source, destination.Length);
        Span<float> left = source.GetChannelData(0);
        Span<float> right = source.GetChannelData(1);
        for (int i = 0; i < left.Length; i++)
        {
            destination[2 * i] = ToInt16(left[i]);
            destination[(2 * i) + 1] = ToInt16(right[i]);
        }
    }

    private static void ThrowIfCannotInterleave(AudioBuffer source, int destinationLength)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.ChannelCount != 2)
            throw new ArgumentException("Only stereo buffers can be interleaved.", nameof(source));
        if (destinationLength < source.SampleCount * 2)
            throw new ArgumentException("The destination is shorter than the interleaved samples.", "destination");
    }

    // Clipped, because casting a value past full scale wraps it to the opposite sign.
    private static short ToInt16(float value)
        => (short)Math.Clamp(MathF.Round(value * short.MaxValue, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);
}
