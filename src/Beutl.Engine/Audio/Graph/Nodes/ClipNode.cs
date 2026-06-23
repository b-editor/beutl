using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

// タイムライン上の時間空間をローカル時間空間に変換
public class ClipNode : AudioNode
{
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
    }
}
