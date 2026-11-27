namespace Beutl.Audio.Graph;

internal sealed class AudioDelayLine(int sampleRate, int channelCount, int delaySamples)
{
    private readonly float[] _history = new float[checked(channelCount * delaySamples)];
    private int _position;

    public int SampleRate { get; } = sampleRate;
    public int ChannelCount { get; } = channelCount;
    public int DelaySamples { get; } = delaySamples;
    public int PendingSamples { get; private set; }

    public void MixChannel(int channel, ReadOnlySpan<float> input, Span<float> output, float gain)
    {
        Span<float> history = _history.AsSpan(channel * DelaySamples, DelaySamples);
        int position = _position;
        for (int i = 0; i < output.Length; i++)
        {
            output[i] += history[position] * gain;
            history[position] = i < input.Length ? input[i] : 0f;
            if (++position == history.Length)
                position = 0;
        }
    }

    public void Advance(int sampleCount, int trailingSilence)
    {
        _position = (int)(((long)_position + sampleCount) % DelaySamples);
        PendingSamples = trailingSilence < sampleCount
            ? Math.Max(0, DelaySamples - trailingSilence)
            : Math.Max(0, PendingSamples - sampleCount);
    }

    // A raw composer can discover a slower sound after its first block. Insert the new delay
    // ahead of the queued samples so increasing the target does not discard their tail.
    public AudioDelayLine Grow(int delaySamples)
    {
        int extra = delaySamples - DelaySamples;
        var result = new AudioDelayLine(SampleRate, ChannelCount, delaySamples);
        if (PendingSamples > 0)
        {
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                ReadOnlySpan<float> history = _history.AsSpan(channel * DelaySamples, DelaySamples);
                Span<float> destination = result._history.AsSpan(channel * delaySamples + extra, DelaySamples);
                history[_position..].CopyTo(destination);
                history[.._position].CopyTo(destination[(DelaySamples - _position)..]);
            }
            result.PendingSamples = PendingSamples + extra;
        }
        return result;
    }
}
