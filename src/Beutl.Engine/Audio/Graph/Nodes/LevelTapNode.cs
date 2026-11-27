using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

// A pass-through that remembers what its branch played. A SoundGroup places one on each child branch so the
// composer can detect ducking even when the child is mixed by the group instead of being a top-level entry.
internal sealed class LevelTapNode : AudioNode
{
    // A lookahead effect can pull the same output block in many small reads, and a later block needs the reads
    // that reached past the previous block's end. The cap keeps a pathological block from growing without bound.
    private const int MaxSegments = 64;

    private readonly List<PeakSegment> _segments = [];
    private readonly List<AudioBuffer> _pool = [];
    private TimeRange _lastBlock;
    private bool _hasBlock;

    // The row of the sound this tap reads; the composer maps it to the layer the sound belongs to.
    public required int ZIndex { get; init; }

    // The context this tap sees is local to the clip above it; adding this gives scene time. It is the group's
    // start on the mixer path, and the pass-through clip's start on a branch outside the group's range.
    public required MediaTime TimeOffset { get; init; }

    public IReadOnlyList<PeakSegment> Segments => _segments;

    internal readonly record struct PeakSegment(MediaTime Start, int SampleRate, AudioBuffer Buffer, int Count);

    // Called by the composer before the owning entry is processed for a new output block.
    internal void BeginBlock(TimeRange block)
    {
        // Segments are valid only for forward progress. A replay of the same block (or any move backwards) starts
        // a new pass, so data recorded for those times must not be max-merged with the old pass.
        bool discontinuity = _hasBlock
            && (block.Start <= _lastBlock.Start
                || block.Start > _lastBlock.End + AudioProcessContext.TimestampQuantizationTolerance);
        if (discontinuity)
        {
            ClearSegments();
        }
        else
        {
            // A segment that ends before this block cannot contribute to it. Later segments may still hold the
            // read-ahead that covers the start of this block.
            for (int i = _segments.Count - 1; i >= 0; i--)
            {
                PeakSegment segment = _segments[i];
                MediaTime end = segment.Start + MediaTime.FromSamples(segment.Count, segment.SampleRate);
                if (end <= block.Start)
                {
                    _segments.RemoveAt(i);
                    Return(segment.Buffer);
                }
            }
        }

        _lastBlock = block;
        _hasBlock = true;
    }

    public override AudioBuffer Process(AudioProcessContext context)
        => PassThrough(Inputs[0].Process(context), context);

    public override AudioBuffer Flush(AudioProcessContext context)
        => PassThrough(Inputs[0].Flush(context), context);

    private AudioBuffer PassThrough(AudioBuffer input, AudioProcessContext context)
    {
        try
        {
            Record(input, context);
            return RecordProcessedOutput(input);
        }
        catch
        {
            input.Dispose();
            throw;
        }
    }

    private void Record(AudioBuffer input, AudioProcessContext context)
    {
        int count = input.SampleCount;
        if (count == 0) return;

        AudioBuffer peaks = Rent(context.SampleRate, count);
        Span<float> values = peaks.GetChannelData(0)[..count];
        values.Clear();
        for (int channel = 0; channel < input.ChannelCount; channel++)
        {
            ReadOnlySpan<float> samples = input.GetChannelData(channel);
            for (int i = 0; i < count; i++)
            {
                float magnitude = MathF.Abs(samples[i]);
                if (float.IsFinite(magnitude) && magnitude > values[i])
                    values[i] = magnitude;
            }
        }

        MediaTime start = context.TimeRange.Start + TimeOffset;
        // A read that re-covers time from this pass replaces the older data instead of max-merging with it.
        for (int i = _segments.Count - 1; i >= 0; i--)
        {
            if (_segments[i].Start >= start)
            {
                Return(_segments[i].Buffer);
                _segments.RemoveAt(i);
            }
        }

        _segments.Add(new PeakSegment(start, context.SampleRate, peaks, count));
        if (_segments.Count > MaxSegments)
        {
            PeakSegment oldest = _segments[0];
            _segments.RemoveAt(0);
            Return(oldest.Buffer);
        }
    }

    private AudioBuffer Rent(int sampleRate, int count)
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            AudioBuffer buffer = _pool[i];
            if (buffer.SampleRate == sampleRate && buffer.SampleCount >= count)
            {
                _pool.RemoveAt(i);
                return buffer;
            }
        }

        return new AudioBuffer(sampleRate, 1, count);
    }

    private void Return(AudioBuffer buffer)
    {
        if (_pool.Count < MaxSegments)
            _pool.Add(buffer);
        else
            buffer.Dispose();
    }

    private void ClearSegments()
    {
        foreach (PeakSegment segment in _segments)
            Return(segment.Buffer);
        _segments.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (PeakSegment segment in _segments)
                segment.Buffer.Dispose();
            _segments.Clear();
            foreach (AudioBuffer buffer in _pool)
                buffer.Dispose();
            _pool.Clear();
        }

        base.Dispose(disposing);
    }
}
