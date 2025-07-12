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
            throw new Exception("Unknown time range.");
        }

        TimeSpan padBefore = newRange.Start - context.TimeRange.Start;

        var clippedContext = new AudioProcessContext(
            newRange.SubtractStart(Start),
            context.SampleRate,
            context.AnimationSampler);
        using var buffer = Inputs[0].Process(clippedContext);
        var newBuffer = new AudioBuffer(
            context.SampleRate,
            buffer.ChannelCount,
            (int)(context.TimeRange.Duration.TotalSeconds * context.SampleRate));
        buffer.CopyTo(newBuffer, (int)(padBefore.TotalSeconds * context.SampleRate));
        return newBuffer;
    }
}
