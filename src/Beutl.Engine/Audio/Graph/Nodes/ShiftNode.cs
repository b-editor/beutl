using Beutl.Engine;

namespace Beutl.Audio.Graph.Nodes;

public class ShiftNode : AudioNode
{
    // The offset the last live read used; a drain keeps it so its range continues where that read ended.
    private MediaTime? _liveOffset;

    public MediaTime Shift { get; set; } = MediaTime.Zero;

    // A sound's offset, read for each window in place of Shift so that an expression on it takes effect.
    internal IExpressionProperty<MediaTime>? Offset { get; init; }

    // An offset under an expression is known for one window only, which bounds no end.
    internal override double? GetFiniteSourceEndSample(int sampleRate)
        => Offset is { HasExpression: true }
            ? null
            : base.GetFiniteSourceEndSample(sampleRate) - GetStaticShift().ToSeconds() * sampleRate;

    public override AudioBuffer Process(AudioProcessContext context)
    {
        return RecordProcessedOutput(Inputs[0].Process(CreateShiftedContext(context, draining: false)));
    }

    public override AudioBuffer Flush(AudioProcessContext context)
    {
        return RecordProcessedOutput(Inputs[0].Flush(CreateShiftedContext(context, draining: true)));
    }

    private MediaTime GetStaticShift() => Offset?.Value ?? Shift;

    private MediaTime GetShift(AudioProcessContext context, bool draining)
    {
        if (Offset is not { HasExpression: true } offset)
            return GetStaticShift();
        if (draining && _liveOffset is { } live)
            return live;

        // An expression runs a script, so it is evaluated once for each read rather than each sample. Behind the
        // speed this node sees the source's time, so it is evaluated at the start of the window the composer asked
        // for, or of the sound if that starts later.
        MediaTime ownerStart = (offset.GetOwnerObject() as EngineObject)?.TimeRange.Value.Start ?? MediaTime.Zero;
        MediaTime windowStart = context.OriginalTimeRange.Start;
        MediaTime shift = offset.Evaluate(new EvaluationContext(windowStart > ownerStart ? windowStart : ownerStart));
        if (!draining)
            _liveOffset = shift;
        return shift;
    }

    private AudioProcessContext CreateShiftedContext(AudioProcessContext context, bool draining)
    {
        MediaTime shift = GetShift(context, draining);
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
