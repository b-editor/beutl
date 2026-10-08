namespace Beutl.Audio.Graph.Nodes;

public class ShiftNode : AudioNode
{
    public TimeSpan Shift { get; set; } = TimeSpan.Zero;

    internal override double? GetFiniteSourceEndSample(int sampleRate)
        => base.GetFiniteSourceEndSample(sampleRate) - Shift.TotalSeconds * sampleRate;

    public override AudioBuffer Process(AudioProcessContext context)
    {
        return RecordProcessedOutput(Inputs[0].Process(CreateShiftedContext(context)));
    }

    public override AudioBuffer Flush(AudioProcessContext context)
    {
        return RecordProcessedOutput(Inputs[0].Flush(CreateShiftedContext(context)));
    }

    private AudioProcessContext CreateShiftedContext(AudioProcessContext context)
    {
        return new AudioProcessContext(
            context.TimeRange.AddStart(Shift),
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);
    }
}
