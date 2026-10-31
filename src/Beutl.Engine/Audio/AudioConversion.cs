namespace Beutl.Audio;

// Converts between planar audio buffers and the interleaved layouts of devices, encoders and the IPC.
public static class AudioConversion
{
    public static void Interleave(AudioBuffer source, Span<float> destination)
    {
        ThrowIfMismatched(source, destination.Length, nameof(destination));
        for (int channel = 0; channel < source.ChannelCount; channel++)
        {
            Span<float> samples = source.GetChannelData(channel);
            for (int i = 0; i < samples.Length; i++)
                destination[i * source.ChannelCount + channel] = samples[i];
        }
    }

    public static void Interleave(AudioBuffer source, Span<short> destination)
    {
        ThrowIfMismatched(source, destination.Length, nameof(destination));
        for (int channel = 0; channel < source.ChannelCount; channel++)
        {
            Span<float> samples = source.GetChannelData(channel);
            for (int i = 0; i < samples.Length; i++)
                destination[i * source.ChannelCount + channel] = ToInt16(samples[i]);
        }
    }

    public static void Deinterleave(ReadOnlySpan<float> source, AudioBuffer destination)
    {
        ThrowIfMismatched(destination, source.Length, nameof(source));
        for (int channel = 0; channel < destination.ChannelCount; channel++)
        {
            Span<float> samples = destination.GetChannelData(channel);
            for (int i = 0; i < samples.Length; i++)
                samples[i] = source[i * destination.ChannelCount + channel];
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
