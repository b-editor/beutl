using System.Buffers;

namespace Beutl.Audio.Graph;

// Where a node that reads ahead of its output keeps the input it has read.
internal interface IReadAheadInput
{
    // The position after the last input written.
    long End { get; }

    void Write(ReadOnlySpan<float> interleaved);

    void WriteSilence(int frames);

    // Drops what was written from position on.
    void TruncateAt(long position);
}

// The input side of a node that reads ahead of its output rather than reporting latency: live input up to where it
// ends, then the upstream's held tail, then silence.
internal sealed class ReadAheadFeed(AudioNode owner, int sampleRate, int channels)
{
    private const int MaxReadFrames = 16384;

    private readonly AudioSourceStream _stream = new(sampleRate, channels, static time => time);

    // Live input stops at End, or where a drain began. The upstream's held tail follows until _drainEnd (null when the
    // upstream reports an unbounded tail), and silence after that.
    private long? _liveEnd;
    private long? _drainEnd;

    public long? End { get; set; }

    public long? GetInputEnd(AudioProcessContext context) => _stream.GetInputEnd(owner.Inputs[0], context);

    // An edit can move the end without replacing the node: input read past a nearer end must go, and input that stopped
    // at an earlier end has to go on.
    public bool EndMoved(long? end, IReadAheadInput input)
        => _liveEnd is { } liveEnd ? end != liveEnd : end is { } value && value < input.End;

    // Reads from readStart on, with outputStart as the position the output starts at.
    public void Restart(long outputStart, long readStart, long? end)
    {
        _stream.Begin(outputStart / (double)sampleRate, readStart / (double)sampleRate, forceReanchor: true);
        End = end;
        _liveEnd = null;
        _drainEnd = null;
    }

    // The source is exhausted at boundary, so only the upstream's held tail may follow. The upstream has already run
    // past that boundary and cannot be rewound without losing its state, such as a delay line's echoes, so the live
    // input read over the tail's span stands in for the tail, and anything read beyond it becomes silence.
    public void BeginDrain(long boundary, IReadAheadInput input)
    {
        if (_liveEnd is { } liveUntil && liveUntil <= boundary)
            return;

        int latency = owner.GetMaxInputLatency(sampleRate, drain: true);
        long tailEnd = latency == int.MaxValue ? long.MaxValue : boundary + latency;
        if (tailEnd < input.End)
        {
            input.TruncateAt(tailEnd);
            _liveEnd = boundary;
            _drainEnd = input.End;
        }
        else if (_liveEnd is null)
        {
            // Everything read so far lies within the tail; the upstream drains the rest.
            _liveEnd = input.End;
            _drainEnd = latency == int.MaxValue ? null : tailEnd;
        }
    }

    // Live input up to End, then the upstream's held tail, then silence, until the input reaches target.
    public void Fill(IReadAheadInput input, long target, AudioProcessContext context)
    {
        while (input.End < target)
        {
            long position = input.End;
            int frames = (int)Math.Min(target - position, MaxReadFrames);
            if (_liveEnd is null)
            {
                if ((End is not { } end || position < end) && Read(input, frames, context, draining: false, End) > 0)
                    continue;

                _liveEnd = position;
                int latency = owner.GetMaxInputLatency(sampleRate, drain: true);
                _drainEnd = latency == int.MaxValue ? null : position + latency;
            }

            if (_drainEnd is not { } drainEnd || position < drainEnd)
            {
                if (Read(input, frames, context, draining: true, _drainEnd) > 0)
                    continue;

                _drainEnd = position;
            }

            input.WriteSilence((int)(target - position));
        }
    }

    private int Read(IReadAheadInput input, int frames, AudioProcessContext context, bool draining, long? end)
    {
        int length = frames * channels;
        float[] buffer = ArrayPool<float>.Shared.Rent(length);
        try
        {
            int samples = _stream.Read(buffer.AsSpan(0, length), owner.Inputs[0], context, draining, end);
            input.Write(buffer.AsSpan(0, samples));
            return samples / channels;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }
}
