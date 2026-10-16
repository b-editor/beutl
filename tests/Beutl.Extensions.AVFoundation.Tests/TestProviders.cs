using Beutl.Audio;
using Beutl.Extensibility;

namespace Beutl.Extensions.AVFoundation.Tests;

// Linear frequency sweep (220 Hz → 2200 Hz across the clip), the same on both channels.
// The instantaneous waveform changes over time so each chunk is materially different from the
// rest — a stale-buffer regression shows up as widely separated chunks being bitwise identical.
internal sealed class SweepSampleProvider(long sampleCount, long sampleRate) : ISampleProvider
{
    public long SampleCount { get; } = sampleCount;
    public long SampleRate { get; } = sampleRate;

    private long _nextSample;

    public ValueTask<AudioBuffer?> ReadNextSamples(int length)
    {
        long offset = _nextSample;
        if (offset >= SampleCount)
            return ValueTask.FromResult<AudioBuffer?>(null);

        _nextSample += length;
        var audio = new AudioBuffer((int)SampleRate, 2, length);
        Span<float> left = audio.GetChannelData(0);
        Span<float> right = audio.GetChannelData(1);
        const float startHz = 220f;
        const float endHz = 2200f;
        double durationSeconds = SampleCount / (double)SampleRate;
        double k = (endHz - startHz) / durationSeconds;
        for (int i = 0; i < Math.Min(length, SampleCount - offset); i++)
        {
            double t = (offset + i) / (double)SampleRate;
            double phase = 2.0 * Math.PI * (startHz * t + 0.5 * k * t * t);
            float v = (float)(Math.Sin(phase) * 0.25);
            left[i] = v;
            right[i] = v;
        }
        return ValueTask.FromResult<AudioBuffer?>(audio);
    }

    public void Dispose()
    {
        // No prefetch or owned resources to drain.
    }
}
