using System.Buffers;

using Beutl.Audio;

using NAudio.Wave;

namespace Beutl.Media.Decoding;

/// <summary>
/// Bridges an NAudio <see cref="ISampleProvider"/> to the <see cref="MediaReader.ReadAudio"/> decode contract.
/// </summary>
public static class SampleProviderReader
{
    /// <summary>
    /// Reads up to <paramref name="length"/> frames from a stereo provider, with the short-read semantics of <see cref="MediaReader.ReadAudio"/>.
    /// </summary>
    public static AudioBuffer ReadStereo(ISampleProvider provider, int sampleRate, int length)
        => Read(provider, sampleRate, 2, length);

    /// <summary>
    /// Reads up to <paramref name="length"/> frames in the provider's native channel order.
    /// Incomplete trailing frames are discarded; short reads and EOF retain the channel count.
    /// </summary>
    public static AudioBuffer Read(ISampleProvider provider, int sampleRate, int channelCount, int length)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
        if (length <= 0)
            return new AudioBuffer(sampleRate, channelCount, 0);

        int valueCount = checked(length * channelCount);
        float[] buffer = ArrayPool<float>.Shared.Rent(valueCount);
        try
        {
            int frames = provider.Read(buffer.AsSpan(0, valueCount)) / channelCount;
            if (frames <= 0)
                return new AudioBuffer(sampleRate, channelCount, 0);

            var audio = new AudioBuffer(sampleRate, channelCount, frames);
            AudioConversion.Deinterleave(buffer.AsSpan(0, frames * channelCount), audio);
            return audio;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }
}
