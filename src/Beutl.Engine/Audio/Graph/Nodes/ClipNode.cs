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
        // padBefore のサンプル換算は (int)切り捨て、入力 buffer.SampleCount は Math.Ceiling 由来で
        // 浮動小数点誤差によりそれぞれ独立に ±1 ズレうる。
        // newBuffer のサイズを超えないようクランプしてコピーする (超過分は範囲外の padding 想定で破棄)。
        int offset = (int)(padBefore.TotalSeconds * context.SampleRate);
        if (offset < 0) offset = 0;
        int copyCount = Math.Min(buffer.SampleCount, newBuffer.SampleCount - offset);
        if (copyCount > 0)
        {
            buffer.CopyTo(newBuffer, offset, copyCount);
        }
        return newBuffer;
    }
}
