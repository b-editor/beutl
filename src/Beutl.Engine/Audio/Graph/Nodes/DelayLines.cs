using Beutl.Audio.Effects;

namespace Beutl.Audio.Graph.Nodes;

// One delay line per channel. A node that moves its reading point uses Read, which reaches positions between samples;
// a node that reads whole samples takes the line itself from the indexer.
internal sealed class DelayLines : IDisposable
{
    private readonly CircularBuffer<float>[] _lines;

    public DelayLines(int channelCount, int maxDelaySamples)
    {
        _lines = new CircularBuffer<float>[channelCount];
        for (int ch = 0; ch < channelCount; ch++)
        {
            // The sample after the longest delay takes part in reading between samples.
            _lines[ch] = new CircularBuffer<float>(maxDelaySamples + 2);
        }
    }

    public int ChannelCount => _lines.Length;

    public CircularBuffer<float> this[int channel] => _lines[channel];

    public void Clear()
    {
        foreach (CircularBuffer<float> line in _lines)
        {
            line.Clear();
        }
    }

    public void Write(int channel, float sample) => _lines[channel].Write(sample);

    public float Read(int channel, double samplesBack)
    {
        CircularBuffer<float> line = _lines[channel];
        int whole = (int)samplesBack;
        float fraction = (float)(samplesBack - whole);
        float near = line.Read(whole);
        return fraction == 0f ? near : near + (line.Read(whole + 1) - near) * fraction;
    }

    public void Dispose()
    {
        foreach (CircularBuffer<float> line in _lines)
        {
            line.Dispose();
        }
    }
}
