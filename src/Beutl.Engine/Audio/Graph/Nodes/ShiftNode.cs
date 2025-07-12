namespace Beutl.Audio.Graph.Nodes;

public class ShiftNode : AudioNode
{
    public TimeSpan Shift { get; set; } = TimeSpan.Zero;

    public override AudioBuffer Process(AudioProcessContext context)
    {
        var shiftedContext = new AudioProcessContext(
            context.TimeRange.AddStart(Shift),
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);
        return Inputs[0].Process(shiftedContext);
    }
}
