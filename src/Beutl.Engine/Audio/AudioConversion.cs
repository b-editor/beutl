namespace Beutl.Audio;

// Converts between the engine's stereo buffers and the interleaved layouts of devices, encoders and the IPC.
public static class AudioConversion
{
    public static void Interleave(AudioBuffer source, Span<float> destination)
    {
        ThrowIfMismatched(source, destination.Length, nameof(destination));
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
        ThrowIfMismatched(source, destination.Length, nameof(destination));
        Span<float> left = source.GetChannelData(0);
        Span<float> right = source.GetChannelData(1);
        for (int i = 0; i < left.Length; i++)
        {
            destination[2 * i] = ToInt16(left[i]);
            destination[(2 * i) + 1] = ToInt16(right[i]);
        }
    }

    public static void Deinterleave(ReadOnlySpan<float> source, AudioBuffer destination)
    {
        ThrowIfMismatched(destination, source.Length, nameof(source));
        Span<float> left = destination.GetChannelData(0);
        Span<float> right = destination.GetChannelData(1);
        for (int i = 0; i < left.Length; i++)
        {
            left[i] = source[2 * i];
            right[i] = source[(2 * i) + 1];
        }
    }

    private static void ThrowIfMismatched(AudioBuffer buffer, int interleavedLength, string interleavedName)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.ChannelCount != 2)
            throw new ArgumentException("Only stereo buffers can be interleaved.", nameof(buffer));
        if (interleavedLength < buffer.SampleCount * 2)
            throw new ArgumentException("The interleaved span is shorter than the buffer's samples.", interleavedName);
    }

    // Clipped, because casting a value past full scale wraps it to the opposite sign.
    private static short ToInt16(float value)
        => (short)Math.Clamp(MathF.Round(value * short.MaxValue, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);
}
