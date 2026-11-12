using System.Numerics;

namespace Beutl.Audio.Graph;

// Values at the consecutive positions [Start, End), one array per channel, added at the end and dropped from the
// front. A position keeps its array index until an Extend call moves the values, so the arrays can be read as spans.
internal sealed class SlidingWindow : IReadAheadInput
{
    private float[][] _channels;
    private int _head;
    private int _count;

    public SlidingWindow(int channelCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
        _channels = new float[channelCount][];
        for (int ch = 0; ch < channelCount; ch++)
            _channels[ch] = new float[64];
    }

    public long Start { get; private set; }

    public long End => Start + _count;

    public float[] this[int channel] => _channels[channel];

    public int IndexOf(long position) => _head + checked((int)(position - Start));

    public void Reset(long start)
    {
        Start = start;
        _head = 0;
        _count = 0;
    }

    public void DropBefore(long position)
    {
        int drop = (int)Math.Clamp(position - Start, 0, _count);
        _head += drop;
        _count -= drop;
        Start += drop;
    }

    // Keeps the positions before the given one.
    public void TruncateAt(long position)
    {
        _count = (int)Math.Clamp(position - Start, 0, _count);
    }

    // Adds frames positions at the end for the caller to write, and returns the array index of the first.
    public int Extend(int frames)
    {
        int length = _channels[0].Length;
        if (_head + _count + frames > length)
        {
            int needed = checked(_count + frames);
            if (needed <= length / 2)
            {
                // At least half the array lies free before the values, so moving them down costs no more than
                // filling it did.
                foreach (float[] channel in _channels)
                    Array.Copy(channel, _head, channel, 0, _count);
            }
            else
            {
                int grown = (int)BitOperations.RoundUpToPowerOf2((uint)checked(needed * 2));
                for (int ch = 0; ch < _channels.Length; ch++)
                {
                    var channel = new float[grown];
                    Array.Copy(_channels[ch], _head, channel, 0, _count);
                    _channels[ch] = channel;
                }
            }

            _head = 0;
        }

        int index = _head + _count;
        _count += frames;
        return index;
    }

    // Appends interleaved frames, one value per channel each.
    public void Write(ReadOnlySpan<float> interleaved)
    {
        int channels = _channels.Length;
        int frames = interleaved.Length / channels;
        int index = Extend(frames);
        for (int ch = 0; ch < channels; ch++)
        {
            float[] data = _channels[ch];
            for (int i = 0; i < frames; i++)
                data[index + i] = interleaved[i * channels + ch];
        }
    }

    public void WriteSilence(int frames)
    {
        int index = Extend(frames);
        foreach (float[] channel in _channels)
            Array.Clear(channel, index, frames);
    }
}
