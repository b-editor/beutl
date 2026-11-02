namespace Beutl.Audio;

// Converts between planar audio buffers and the interleaved layouts of devices, encoders and the IPC.
public static class AudioConversion
{
    public static void Interleave(AudioBuffer source, Span<float> destination)
    {
        ThrowIfMismatched(source, destination.Length, nameof(destination));
        Interleave(source, source.SampleCount, destination);
    }

    // The first frames of source, for a caller that has checked destination holds them.
    internal static void Interleave(AudioBuffer source, int frames, Span<float> destination)
    {
        int channels = source.ChannelCount;
        for (int channel = 0; channel < channels; channel++)
        {
            Span<float> samples = source.GetChannelData(channel)[..frames];
            for (int i = 0; i < samples.Length; i++)
                destination[i * channels + channel] = samples[i];
        }
    }

    public static void Interleave(AudioBuffer source, Span<short> destination)
    {
        ThrowIfMismatched(source, destination.Length, nameof(destination));
        int channels = source.ChannelCount;
        for (int channel = 0; channel < channels; channel++)
        {
            Span<float> samples = source.GetChannelData(channel);
            for (int i = 0; i < samples.Length; i++)
                destination[i * channels + channel] = ToInt16(samples[i]);
        }
    }

    public static void Deinterleave(ReadOnlySpan<float> source, AudioBuffer destination)
    {
        ThrowIfMismatched(destination, source.Length, nameof(source));
        int channels = destination.ChannelCount;
        for (int channel = 0; channel < channels; channel++)
        {
            Span<float> samples = destination.GetChannelData(channel);
            for (int i = 0; i < samples.Length; i++)
                samples[i] = source[i * channels + channel];
        }
    }

    private static void ThrowIfMismatched(AudioBuffer buffer, int interleavedLength, string interleavedName)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (interleavedLength < checked(buffer.SampleCount * buffer.ChannelCount))
            throw new ArgumentException("The interleaved span is shorter than the buffer's samples.", interleavedName);
    }

    // Clipped, because casting a value past full scale wraps it to the opposite sign.
    private static short ToInt16(float value)
        => (short)Math.Clamp(MathF.Round(value * short.MaxValue, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);
}
