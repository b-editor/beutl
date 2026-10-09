using System.Buffers;
using Beutl.Engine;

namespace Beutl.Audio.Graph.Nodes;

/// <summary>Changes playback speed while preserving the input pitch.</summary>
public sealed partial class TimeStretchNode : AudioNode, IAudioTimeMappingNode
{
    private readonly AudioSpeedMapping _mapping = new(Effects.TimeStretchParameters.Normalize, Effects.TimeStretchParameters.MinSpeed / 100d);
    private TimeStretchProcessor? _processor;
    private int _lastSampleRate;
    private List<AudioNode>? _upstreamSnapshot;

    public IProperty<float>? Speed
    {
        get => _mapping.Speed;
        set => _mapping.Speed = value;
    }

    internal override double? GetFiniteSourceEndSample(int sampleRate)
        => _mapping.GetFiniteSourceEndSample(base.GetFiniteSourceEndSample(sampleRate), sampleRate);

    public override int GetTotalLatencySamples(int sampleRate)
        => _mapping.GetTotalLatencySamples(sampleRate, GetMaxInputLatency(sampleRate, drain: false));

    public override int GetDrainLatencySamples(int sampleRate)
        => _mapping.GetDrainLatencySamples(sampleRate, GetMaxInputLatency(sampleRate, drain: true));

    bool IAudioTimeMappingNode.TryGetDrainSpeedFactor(int sampleRate, out double speed)
        => _mapping.TryGetDrainSpeedFactor(sampleRate, out speed);

    public override AudioBuffer Process(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: false));

    public override AudioBuffer Flush(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: true));

    private AudioBuffer ProcessCore(AudioProcessContext context, bool draining)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Inputs.Count != 1)
            throw new InvalidOperationException("TimeStretchNode requires exactly one input.");

        int expectedOut = context.GetSampleCount();
        _mapping.Configure(context.SampleRate);
        bool upstreamChanged = AudioSourceStream.UpstreamChangedAndCapture(this, ref _upstreamSnapshot);
        if (_processor is null || _lastSampleRate != context.SampleRate || upstreamChanged)
        {
            _processor = new TimeStretchProcessor(context.SampleRate, 2, this);
            _lastSampleRate = context.SampleRate;
        }
        bool forceReanchor = _mapping.IsInvalidated && !draining;
        AudioBuffer result = Speed?.Animation is null
            ? ProcessStaticSpeed(context, expectedOut, draining, forceReanchor)
            : ProcessAnimatedSpeed(context, expectedOut, draining, forceReanchor);
        if (!draining)
            _mapping.AcknowledgeChanges();
        return result;
    }

    private AudioBuffer ProcessStaticSpeed(AudioProcessContext context, int expectedOut, bool draining, bool forceReanchor)
    {
        float speed = _mapping.StaticSpeed;
        if (Math.Abs(speed - 1f) < float.Epsilon && (!draining || _processor!.CanPassThroughDrain))
        {
            AudioBuffer result = draining ? Inputs[0].Flush(context) : Inputs[0].Process(context);
            _processor!.TrackPassthrough(context, expectedOut);
            return result;
        }
        return _processor!.ProcessBuffer(context, speed, expectedOut, draining, forceReanchor);
    }

    private AudioBuffer ProcessAnimatedSpeed(AudioProcessContext context, int expectedOut, bool draining, bool forceReanchor)
    {
        double sourceStart = _mapping.MapOutputTimeToSource(context.TimeRange.Start).TotalSeconds;
        double[] rented = ArrayPool<double>.Shared.Rent(expectedOut);
        try
        {
            Span<double> speeds = rented.AsSpan(0, expectedOut);
            _mapping.FillSpeedCurve(context, speeds, draining, roundSampleClock: true);
            return _processor!.ProcessBufferWithVariableSpeed(context, speeds, expectedOut, sourceStart, draining, forceReanchor);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(rented);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _mapping.Dispose();
        _processor = null;
        _upstreamSnapshot = null;
        base.Dispose(disposing);
    }
}
