using Beutl.Engine;

namespace Beutl.Audio.Graph.Nodes;

public class ShiftNode : AudioNode
{
    private MediaTime? _lastOffset;

    public MediaTime Shift { get; set; } = MediaTime.Zero;

    // A sound's offset, read for each window in place of Shift so that an expression on it takes effect.
    internal IExpressionProperty<MediaTime>? Offset { get; init; }

    internal override double? GetFiniteSourceEndSample(int sampleRate)
        => base.GetFiniteSourceEndSample(sampleRate) - GetLastShift().ToSeconds() * sampleRate;

    public override AudioBuffer Process(AudioProcessContext context)
    {
        return RecordProcessedOutput(Inputs[0].Process(CreateShiftedContext(context)));
    }

    public override AudioBuffer Flush(AudioProcessContext context)
    {
        return RecordProcessedOutput(Inputs[0].Flush(CreateShiftedContext(context)));
    }

    private MediaTime GetLastShift() => Offset switch
    {
        null => Shift,
        { HasExpression: true } when _lastOffset is { } last => last,
        { } offset => offset.Value,
    };

    private MediaTime GetShift(AudioProcessContext context)
    {
        if (Offset is not { HasExpression: true } offset)
            return GetLastShift();

        // An expression runs a script, so it is evaluated once for each read rather than each sample. Behind the
        // speed this node sees the source's time, so it is evaluated at the start of the window the composer asked
        // for, or of the sound if that starts later.
        MediaTime ownerStart = (offset.GetOwnerObject() as EngineObject)?.TimeRange.Value.Start ?? MediaTime.Zero;
        MediaTime windowStart = context.OriginalTimeRange.Start;
        MediaTime shift = offset.Evaluate(new EvaluationContext(windowStart > ownerStart ? windowStart : ownerStart));
        _lastOffset = shift;
        return shift;
    }

    private AudioProcessContext CreateShiftedContext(AudioProcessContext context)
    {
        MediaTime shift = GetShift(context);
        return new AudioProcessContext(
            context.TimeRange.AddStart(shift),
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange)
        {
            ProcessEndTime = context.ProcessEndTime + shift
        };
    }
}
