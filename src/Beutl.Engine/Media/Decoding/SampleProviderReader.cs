using System.Runtime.InteropServices;

using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

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
    public static Ref<IPcm> ReadStereo(ISampleProvider provider, int sampleRate, int length)
    {
        if (length <= 0)
            return Ref<IPcm>.Create(new Pcm<Stereo32BitFloat>(sampleRate, 0));

        // ToStereo() yields 2 floats per frame, so the provider's element count maps to frames via /2.
        float[] buffer = new float[length * 2];
        int frames = provider.Read(buffer.AsSpan()) / 2;
        if (frames <= 0)
            return Ref<IPcm>.Create(new Pcm<Stereo32BitFloat>(sampleRate, 0));

        var pcm = new Pcm<Stereo32BitFloat>(sampleRate, frames);
        buffer.AsSpan(0, frames * 2).CopyTo(MemoryMarshal.Cast<Stereo32BitFloat, float>(pcm.DataSpan));
        return Ref<IPcm>.Create(pcm);
    }
}
