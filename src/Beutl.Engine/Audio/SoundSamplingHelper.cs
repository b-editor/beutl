using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

namespace Beutl.Audio;

internal static class SoundSamplingHelper
{
    public static bool TryReadMonoFloat(
        SoundSource.Resource source,
        TimeSpan start,
        TimeSpan length,
        out float[] samples,
        out int sampleRate)
    {
        samples = [];
        sampleRate = 0;

        if (source.IsDisposed || source.MediaReader == null)
        {
            return false;
        }

        if (length <= TimeSpan.Zero)
        {
            sampleRate = source.SampleRate;
            return true;
        }

        if (!source.Read(start, length, out Ref<IPcm>? soundRef))
        {
            return false;
        }

        using (soundRef)
        {
            IPcm pcm = soundRef.Value;
            sampleRate = pcm.SampleRate;

            using Pcm<Monaural32BitFloat> mono = pcm is Pcm<Monaural32BitFloat> direct
                ? direct.Clone()
                : pcm.Convert<Monaural32BitFloat>();

            int n = mono.NumSamples;
            samples = new float[n];
            ReadOnlySpan<Monaural32BitFloat> src = mono.DataSpan;
            for (int i = 0; i < n; i++)
            {
                samples[i] = src[i].Value;
            }
        }

        return true;
    }

    public static void DownsampleMinMax(
        ReadOnlySpan<float> samples,
        Span<float> mins,
        Span<float> maxs)
    {
        int barCount = mins.Length;
        if (maxs.Length != barCount)
        {
            throw new ArgumentException("mins and maxs must have the same length.");
        }

        if (samples.Length == 0 || barCount == 0)
        {
            mins.Clear();
            maxs.Clear();
            return;
        }

        for (int bar = 0; bar < barCount; bar++)
        {
            int start = (int)((long)bar * samples.Length / barCount);
            int end = (int)((long)(bar + 1) * samples.Length / barCount);
            if (end <= start) end = Math.Min(start + 1, samples.Length);

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int i = start; i < end; i++)
            {
                float v = samples[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (float.IsPositiveInfinity(min)) min = 0f;
            if (float.IsNegativeInfinity(max)) max = 0f;

            mins[bar] = min;
            maxs[bar] = max;
        }
    }

    public static void ExtractWindow(
        ReadOnlySpan<float> samples,
        int centerIndex,
        Span<float> destination)
    {
        int n = destination.Length;
        int start = centerIndex - n / 2;
        for (int i = 0; i < n; i++)
        {
            int srcIdx = start + i;
            destination[i] = (uint)srcIdx < (uint)samples.Length ? samples[srcIdx] : 0f;
        }
    }
}
