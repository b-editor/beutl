using System.Buffers;
using Beutl.Animation;

namespace Beutl.Audio.Graph.Nodes;

internal interface ISpeedMappedProcessor
{
    bool CanPassThroughDrain { get; }

    void TrackPassthrough(AudioProcessContext context, int sampleCount);

    AudioBuffer ProcessBuffer(AudioProcessContext context, float speed, int expectedOut, bool draining, bool forceReanchor);

    AudioBuffer ProcessBufferWithVariableSpeed(
        AudioProcessContext context,
        ReadOnlySpan<double> speedCurve,
        int expectedOut,
        double sourceStartSeconds,
        bool draining,
        bool forceReanchor);
}

// The processing SpeedNode and TimeStretchNode share; they differ only in the speed mapping, the
// processor and whether the speed curve rounds the sample clock.
internal sealed class SpeedMappedProcessing<TProcessor>(
    AudioNode owner,
    AudioSpeedMapping mapping,
    Func<int, int, TProcessor> createProcessor,
    bool roundSampleClock)
    where TProcessor : class, ISpeedMappedProcessor
{
    private TProcessor? _processor;
    private int _lastSampleRate;
    private int _lastChannelCount;
    private List<AudioNode>? _upstreamSnapshot;

    public AudioBuffer Process(AudioProcessContext context, int channelCount, bool draining)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (owner.Inputs.Count != 1)
            throw new InvalidOperationException($"{owner.GetType().Name} requires exactly one input.");

        int expectedOut = context.GetSampleCount();
        mapping.Configure(context.SampleRate);
        bool upstreamChanged = AudioSourceStream.UpstreamChangedAndCapture(owner, ref _upstreamSnapshot);
        if (_processor is null || _lastSampleRate != context.SampleRate || _lastChannelCount != channelCount || upstreamChanged)
        {
            _processor = createProcessor(context.SampleRate, channelCount);
            _lastChannelCount = channelCount;
            _lastSampleRate = context.SampleRate;
        }
        bool forceReanchor = mapping.IsInvalidated && !draining;
        AudioBuffer result = mapping.Speed?.GetKeyFrameAnimation() is null
            ? ProcessStaticSpeed(_processor, context, expectedOut, draining, forceReanchor)
            : ProcessAnimatedSpeed(_processor, context, expectedOut, draining, forceReanchor);
        if (!draining)
            mapping.AcknowledgeChanges();
        return result;
    }

    private AudioBuffer ProcessStaticSpeed(TProcessor processor, AudioProcessContext context, int expectedOut, bool draining, bool forceReanchor)
    {
        float speed = mapping.StaticSpeed;
        if (Math.Abs(speed - 1f) < float.Epsilon && (!draining || processor.CanPassThroughDrain))
        {
            AudioBuffer result = draining ? owner.Inputs[0].Flush(context) : owner.Inputs[0].Process(context);
            processor.TrackPassthrough(context, expectedOut);
            return result;
        }
        return processor.ProcessBuffer(context, speed, expectedOut, draining, forceReanchor);
    }

    private AudioBuffer ProcessAnimatedSpeed(TProcessor processor, AudioProcessContext context, int expectedOut, bool draining, bool forceReanchor)
    {
        double sourceStart = mapping.MapOutputTimeToSource(context.TimeRange.Start).ToSeconds();
        double[] rented = ArrayPool<double>.Shared.Rent(expectedOut);
        try
        {
            Span<double> speeds = rented.AsSpan(0, expectedOut);
            mapping.FillSpeedCurve(context, speeds, draining, roundSampleClock);
            return processor.ProcessBufferWithVariableSpeed(context, speeds, expectedOut, sourceStart, draining, forceReanchor);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(rented);
        }
    }

    public void Release()
    {
        _processor = null;
        _upstreamSnapshot = null;
    }
}
