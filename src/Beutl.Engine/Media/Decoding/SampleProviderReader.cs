using System.Runtime.InteropServices;

using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

using NAudio.Wave;

namespace Beutl.Media.Decoding;

/// <summary>
/// Bridges an NAudio <see cref="ISampleProvider"/> to Beutl's <see cref="Pcm{T}"/> decode contract,
/// honouring the <see cref="MediaReader.ReadAudio"/> short-read semantics. Intended for built-in and
/// plugin decoder backends that obtain PCM through NAudio.
/// </summary>
public static class SampleProviderReader
{
    /// <summary>
    /// Reads up to <paramref name="length"/> stereo frames from <paramref name="provider"/> into a fresh
    /// <see cref="Pcm{T}"/> of <see cref="Stereo32BitFloat"/>.
    /// <para>
    /// The returned buffer's <see cref="IPcm.NumSamples"/> is the number of frames actually decoded:
    /// it equals <paramref name="length"/> for a full read, is less than <paramref name="length"/> near
    /// end-of-stream (a short read), and is <c>0</c> when the provider is exhausted. This method never
    /// returns <see langword="null"/>; end-of-stream is signalled only by <see cref="IPcm.NumSamples"/>.
    /// </para>
    /// </summary>
    /// <param name="provider">A stereo (<c>WaveFormat.Channels == 2</c>) sample provider.</param>
    /// <param name="sampleRate">The sample rate stamped on the returned <see cref="Pcm{T}"/>.</param>
    /// <param name="length">Number of frames requested; <c>&lt;= 0</c> yields an empty buffer.</param>
    /// <returns>A non-null <see cref="Ref{IPcm}"/> wrapping the decoded samples.</returns>
    public static Ref<IPcm> ReadStereo(ISampleProvider provider, int sampleRate, int length)
    {
        if (length <= 0)
            return Ref<IPcm>.Create(new Pcm<Stereo32BitFloat>(sampleRate, 0));

        // ToStereo() yields 2 floats per frame, so the provider's element count maps to frames via /2.
        float[] buffer = new float[length * 2];
        int frames = provider.Read(buffer, 0, buffer.Length) / 2;
        if (frames <= 0)
            return Ref<IPcm>.Create(new Pcm<Stereo32BitFloat>(sampleRate, 0));

        var pcm = new Pcm<Stereo32BitFloat>(sampleRate, frames);
        buffer.AsSpan(0, frames * 2).CopyTo(MemoryMarshal.Cast<Stereo32BitFloat, float>(pcm.DataSpan));
        return Ref<IPcm>.Create(pcm);
    }
}
