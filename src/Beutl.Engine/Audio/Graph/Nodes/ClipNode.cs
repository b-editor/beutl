using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

// タイムライン上の時間空間をローカル時間空間に変換
public class ClipNode : AudioNode
{
    // Clip-local end of the last window this node actually processed. Equals Duration when the clip ran
    // to its own end, but is earlier when a parent trims it (a SoundGroup window stopping before the
    // child's Duration); Flush drains from here so the cached effects stay contiguous either way.
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

            // When this window reaches the clip's true end, the effect chain still holds its tail in
            // delay lines. Drain it (Flush feeds silence past the clip end, so a trimmed source never
            // bleeds) and append it into the trailing pad the window already reserves, recovering audio
            // that inline processing would otherwise drop off the tail. The drain is contiguous with the
            // main slice in clip-local time, so the cached effect state does not reset.
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

    // A nested ClipNode (one feeding another clip's graph — e.g. a SoundGroup child mixed under a group
    // clip) is flushed by its parent in the PARENT's time domain. Process remaps an incoming timeline
    // window to clip-local before pulling the input, so the flush path must reconstruct the same
    // clip-local frame or the child's cached effects (a lookahead limiter) see a discontinuity, Reset(),
    // and drop the very tail being drained. Drain from the clip-local end of the last processed window —
    // Duration when the clip ran to its own end, earlier when a parent trimmed it — so the cached chain
    // stays contiguous; the parent's start is intentionally dropped, which is also why an intervening
    // ShiftNode needs no flush override of its own.
    public override AudioBuffer Flush(AudioProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var drainContext = new AudioProcessContext(
            new TimeRange(_lastProcessedLocalEnd ?? Duration, context.TimeRange.Duration),
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);

        var result = Inputs[0].Flush(drainContext);

        // A parent may flush this same clip across multiple blocks when its tail capacity is below the
        // child's latency; advancing here keeps the next drain contiguous instead of replaying from the
        // old local end and tripping the cached effects' discontinuity guard (as AppendFlushedTail does).
        _lastProcessedLocalEnd = drainContext.TimeRange.End;
        return result;
    }

    // Drains the input chain's residual latency into newBuffer starting at writeOffset, bounded by the
    // remaining capacity. The drain context starts clip-local at Duration — exactly where the main
    // slice ended on the terminal window — so the cached effect chain sees a contiguous stream.
    private void AppendFlushedTail(AudioProcessContext context, AudioBuffer newBuffer, int writeOffset)
    {
        // Known limitation: when the compose window ends exactly at the clip boundary the window is
        // already full (capacity == 0) and the tail has nowhere to go; the next window legitimately
        // excludes the clip (TimeRange.Intersects is half-open), so a few ms of tail is lost on that
        // alignment. The only fixes — range-expanding the Composer or emitting an oversized terminal
        // buffer — are out of scope (the former spuriously resets the limiter; the latter breaks the
        // output-length contract). Tracked as a follow-up.
        int capacity = newBuffer.SampleCount - writeOffset;
        if (capacity <= 0)
            return;

        int latency = Inputs[0].GetTotalLatencySamples(context.SampleRate);
        int drainCount = Math.Min(latency, capacity);
        if (drainCount <= 0)
            return;

        var drainContext = new AudioProcessContext(
            new TimeRange(Duration, TimeSpan.FromSeconds((double)drainCount / context.SampleRate)),
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);

        using var tail = Inputs[0].Flush(drainContext);
        int copyCount = Math.Min(tail.SampleCount, drainCount);
        if (copyCount > 0)
        {
            tail.CopyTo(newBuffer, writeOffset, copyCount);
        }

        // This drain advanced the upstream chain to the end of the drain block. Record it so that if a
        // parent later flushes this same clip to recover the rest of a partially-drained tail (capacity
        // was below the reported latency), Flush continues from here instead of stepping back to Duration
        // and tripping the cached effects' discontinuity guard.
        _lastProcessedLocalEnd = drainContext.TimeRange.End;
    }
}
