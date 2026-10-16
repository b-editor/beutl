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
    {
        if (length <= 0)
            return new AudioBuffer(sampleRate, 2, 0);

        // ToStereo() yields 2 floats per frame, so the provider's element count maps to frames via /2.
        float[] buffer = new float[length * 2];
        int frames = provider.Read(buffer.AsSpan()) / 2;
        if (frames <= 0)
            return new AudioBuffer(sampleRate, 2, 0);

        var audio = new AudioBuffer(sampleRate, 2, frames);
        AudioConversion.Deinterleave(buffer.AsSpan(0, frames * 2), audio);
        return audio;
    }
}
