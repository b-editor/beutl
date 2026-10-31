using Beutl.Engine;

namespace Beutl.Audio.Graph.Nodes;

/// <summary>Changes playback speed while preserving the input pitch.</summary>
public sealed partial class TimeStretchNode : AudioNode, IAudioTimeMappingNode
{
    private readonly AudioSpeedMapping _mapping = new(Effects.TimeStretchParameters.Normalize, Effects.TimeStretchParameters.MinSpeed / 100d);
    private readonly SpeedMappedProcessing<TimeStretchProcessor> _processing;

    public TimeStretchNode()
    {
        _processing = new(this, _mapping, (sampleRate, channelCount) => new TimeStretchProcessor(sampleRate, channelCount, this), roundSampleClock: true);
    }

    public int ChannelCount { get; set; } = 2;

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
        => RecordProcessedOutput(_processing.Process(context, ChannelCount, draining: false));

    public override AudioBuffer Flush(AudioProcessContext context)
        => RecordProcessedOutput(_processing.Process(context, ChannelCount, draining: true));

    protected override void Dispose(bool disposing)
    {
        _mapping.Dispose();
        _processing.Release();
        base.Dispose(disposing);
    }
}
