using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

// タイムライン上の時間空間をローカル時間空間に変換
public class ClipNode : AudioNode
{
    // Flush from the clip-local end of the last processed window, including parent-trimmed clips.
    private TimeSpan? _lastProcessedLocalEnd;

    public TimeSpan Start { get; set; } = TimeSpan.Zero;

    public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    public override AudioBuffer Process(AudioProcessContext context)
    {
        var range = new TimeRange(Start, Duration);
        TimeRange newRange;
        if (context.TimeRange.Intersects(range))
        {
            newRange = context.TimeRange.Intersect(range);
        }
        else
        {
            // throw new Exception("Unknown time range.");
            // 本来なら時間範囲外のノードは処理されないはずだが...
            return new AudioBuffer(
                context.SampleRate,
                2,
                context.GetSampleCount());
        }

        TimeSpan padBefore = newRange.Start - context.TimeRange.Start;

        var clippedContext = new AudioProcessContext(
            newRange.SubtractStart(Start),
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);
        _lastProcessedLocalEnd = newRange.End - Start;
        using var buffer = Inputs[0].Process(clippedContext);
        var newBuffer = new AudioBuffer(
            context.SampleRate,
            buffer.ChannelCount,
            context.GetSampleCount());
        try
        {
            // padBefore (truncated) and buffer.SampleCount (from Math.Ceiling) can each drift by ±1,
            // so clamp the copy to newBuffer's capacity; the overflow is out-of-range padding.
            // padBefore is clip-relative and bounded by the clip duration, so it cannot overflow int.
            int offset = (int)(padBefore.TotalSeconds * context.SampleRate);
            if (offset < 0) offset = 0;
            int copyCount = Math.Min(buffer.SampleCount, newBuffer.SampleCount - offset);
            if (copyCount > 0)
            {
                buffer.CopyTo(newBuffer, offset, copyCount);
            }

            // Drain held latency into the trailing pad while keeping the effect chain contiguous.
            if (newRange.End == range.End)
            {
                AppendFlushedTail(context, newBuffer, offset + copyCount);
            }

            return newBuffer;
        }
        catch
        {
            // Dispose the output the caller never received rather than leak it.
            newBuffer.Dispose();
            throw;
        }
    }

    // Rebuild the drain context in clip-local time so nested cached effects remain contiguous.
    public override AudioBuffer Flush(AudioProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var drainContext = new AudioProcessContext(
            new TimeRange(_lastProcessedLocalEnd ?? Duration, context.TimeRange.Duration),
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);

        var result = Inputs[0].Flush(drainContext);

        // Advance the local end so a later partial drain continues from this block.
        _lastProcessedLocalEnd = drainContext.TimeRange.End;
        return result;
    }

    // Drain residual latency into the remaining capacity from the clip-local terminal position.
    private void AppendFlushedTail(AudioProcessContext context, AudioBuffer newBuffer, int writeOffset)
    {
        // A full terminal window has no capacity for tail data; recovery then requires a later window.
        int capacity = newBuffer.SampleCount - writeOffset;
        if (capacity <= 0)
            return;

        int latency = Inputs[0].GetTotalLatencySamples(context.SampleRate);
        int drainCount = Math.Min(latency, capacity);
        if (drainCount <= 0)
            return;

        var drainContext = new AudioProcessContext(
            new TimeRange(
                Duration,
                AudioProcessContext.GetDurationForSampleCount(drainCount, context.SampleRate)),
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);

        using var tail = Inputs[0].Flush(drainContext);
        int copyCount = Math.Min(tail.SampleCount, drainCount);
        if (copyCount > 0)
        {
            tail.CopyTo(newBuffer, writeOffset, copyCount);
        }

        // Preserve the advanced position so a later partial drain continues contiguously.
        _lastProcessedLocalEnd = drainContext.TimeRange.End;
    }
}
