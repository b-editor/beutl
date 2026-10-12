using Beutl.Extensibility;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Extensions.AVFoundation.Tests;

// Linear frequency sweep (220 Hz → 2200 Hz across the clip), Stereo 32-bit float interleaved.
// The instantaneous waveform changes over time so each chunk is materially different from the
// rest — a stale-buffer regression shows up as widely separated chunks being bitwise identical.
internal sealed class SweepSampleProvider(long sampleCount, long sampleRate) : ISampleProvider
{
    public long SampleCount { get; } = sampleCount;
    public long SampleRate { get; } = sampleRate;

    public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
    {
        var pcm = new Pcm<Stereo32BitFloat>((int)SampleRate, (int)length);
        var span = pcm.DataSpan;
        const float startHz = 220f;
        const float endHz = 2200f;
        double durationSeconds = SampleCount / (double)SampleRate;
        double k = (endHz - startHz) / durationSeconds;
        for (int i = 0; i < length; i++)
        {
            double t = (offset + i) / (double)SampleRate;
            double phase = 2.0 * Math.PI * (startHz * t + 0.5 * k * t * t);
            float v = (float)(Math.Sin(phase) * 0.25);
            span[i] = new Stereo32BitFloat(v, v);
        }
        return ValueTask.FromResult(pcm);
    }

    public void Dispose()
    {
        // Stateless test double — no prefetch or owned resources to drain.
    }
}
