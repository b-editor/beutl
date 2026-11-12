using Beutl.Animation;
using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

/// <summary>
/// Reduces steady background noise such as hiss, hum, and room tone. The node reads its input ahead of
/// the output instead of reporting the spectral analysis delay as latency, so the processed audio stays
/// aligned with the timeline.
/// </summary>
public sealed class NoiseReductionNode : AudioNode
{
    // The input format is supplied when the graph is built, without pulling upstream audio.
    public int ChannelCount { get; set; } = 2;

    // Audio around a restart that seeds the noise floor. At most half of it precedes the restart, so a
    // seek warms the analysis up on audio the output never plays.
    private const double SeedSeconds = 1;

    private readonly Func<long, NoiseReductionSettings> _settingsAt;
    private SpectralNoiseReducer? _reducer;
    private ReadAheadFeed? _feed;
    private List<AudioNode>? _upstreamSnapshot;
    private int _sampleRate;
    private MediaTime? _lastTimeRangeEnd;
    private AnimationSampler? _sampler;
    private NoiseReductionSettings _staticSettings;
    private bool _animated;

    // Set when a graph update reuses this node: reused upstream nodes may now produce different
    // audio, so the input read ahead of the output and the noise floor learned from it are renewed.
    private bool _inputStale;
    private Controls _processedControls;

    public NoiseReductionNode()
    {
        _settingsAt = GetSettings;
    }

    public required IProperty<float> Reduction { get; init; }

    public required IProperty<float> Sensitivity { get; init; }

    public required IProperty<float> Smoothing { get; init; }

    public required IProperty<float> Adaptation { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: false));

    // Called when a graph update reuses this node. An update that changed this effect's own controls
    // left the upstream audio alone, so the buffered input and the learned noise floor stay valid,
    // and relearning would mistake a sound held through the edit for noise.
    internal void InvalidateInput()
    {
        if (ReadControls() == _processedControls)
            _inputStale = true;
    }

    public override AudioBuffer Flush(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: true));

    private AudioBuffer ProcessCore(AudioProcessContext context, bool draining)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Inputs.Count != 1)
            throw new InvalidOperationException("NoiseReductionNode requires exactly one input.");

        int count = context.GetSampleCount();
        var output = new AudioBuffer(context.SampleRate, ChannelCount, count);

        // An empty chunk carries no audio, and advancing the end here would mask a discontinuity.
        if (count == 0)
            return output;

        try
        {
            if (Prepare(context, draining))
            {
                SpectralNoiseReducer reducer = _reducer!;
                _feed!.Fill(reducer, reducer.GetRequiredInputEnd(reducer.OutputPosition + count), context);
                reducer.Read(output, _settingsAt);
                _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;
            }

            return output;
        }
        catch
        {
            // A chunk that threw must not look contiguous, or the next one inherits half-read input.
            _lastTimeRangeEnd = null;
            output.Dispose();
            throw;
        }
        finally
        {
            _sampler = null;
        }
    }

    // Returns false when a drain has nothing to release.
    private bool Prepare(AudioProcessContext context, bool draining)
    {
        int sampleRate = context.SampleRate;
        bool upstreamChanged = AudioSourceStream.UpstreamChangedAndCapture(this, ref _upstreamSnapshot);
        if (_reducer is null || _sampleRate != sampleRate || _reducer.ChannelCount != ChannelCount || upstreamChanged)
        {
            _reducer = new SpectralNoiseReducer(sampleRate, ChannelCount);
            _feed = new ReadAheadFeed(this, sampleRate, ChannelCount);
            _sampleRate = sampleRate;
            _lastTimeRangeEnd = null;
        }

        CaptureSettings(context);
        bool contiguous = _lastTimeRangeEnd is { } end && context.ContinuesFrom(end);
        if (draining)
        {
            // A drain continues where the last block ended; after a seek nothing is held.
            if (!contiguous)
                return false;

            // The source is exhausted where the last block ended.
            _feed!.BeginDrain(_reducer.OutputPosition, _reducer);
            return true;
        }

        long? terminal = context.ProcessEndTime is { } processEnd ? ToTerminalSample(processEnd) : null;

        // An edit that moves the terminal invalidates input read past the earlier of the two ends.
        bool terminalMoved = _feed!.EndMoved(terminal, _reducer);
        if (!contiguous || terminalMoved)
        {
            // Truncate like SourceNode and ResampleNode, so a start between samples maps to the
            // sample the upstream delivers there.
            long outputStart = context.TimeRange.Start.ToSamples(_sampleRate, MidpointRounding.ToZero);
            Restart(context, outputStart, terminal);
        }
        else if (_inputStale)
        {
            // A changed upstream gain or source would leave the old floor wrong until it aged out.
            Restart(context, _reducer.OutputPosition, terminal);
        }
        else
        {
            _feed.End = terminal;
        }

        return true;
    }

    // Rereads the input from a pre-roll before the output, so any re-anchoring transient upstream lands
    // in audio that is never played.
    private void Restart(AudioProcessContext context, long outputStart, long? terminal)
    {
        SpectralNoiseReducer reducer = _reducer!;
        _inputStale = false;
        long seed = (long)(SeedSeconds * _sampleRate);

        // Never warm up on audio before the timeline origin; a clip's own start is its first sample.
        long earliest = Math.Min(outputStart, 0);
        long origin = SpectralNoiseReducer.FloorToHop(Math.Max(earliest, outputStart - seed / 2), reducer.HopSize);

        reducer.Reset(origin, outputStart);
        _feed!.Restart(outputStart, origin, terminal);

        long seedEnd = origin + seed;
        _feed.Fill(reducer, seedEnd, context);
        reducer.Seed(seedEnd);
    }

    private void CaptureSettings(AudioProcessContext context)
    {
        _sampler = context.AnimationSampler;
        _processedControls = ReadControls();
        _animated = AnimationSampler.IsAnimated(Reduction)
                    || AnimationSampler.IsAnimated(Sensitivity)
                    || AnimationSampler.IsAnimated(Smoothing)
                    || AnimationSampler.IsAnimated(Adaptation);
        _staticSettings = NoiseReductionParameters.Normalize(
            Reduction.Value,
            Sensitivity.Value,
            Smoothing.Value,
            Adaptation.Value);
    }

    // Animated controls are compared by their keyframes or expression; static ones by value.
    private Controls ReadControls()
    {
        return new Controls(
            GetAnimation(Reduction), GetStaticValue(Reduction),
            GetAnimation(Sensitivity), GetStaticValue(Sensitivity),
            GetAnimation(Smoothing), GetStaticValue(Smoothing),
            GetAnimation(Adaptation), GetStaticValue(Adaptation));

        static object? GetAnimation(IProperty<float> property) => (property as IExpressionProperty)?.Expression;

        static float GetStaticValue(IProperty<float> property) => AnimationSampler.IsAnimated(property) ? 0 : property.Value;
    }

    // Animated parameters are evaluated once per frame, at the frame's center.
    private NoiseReductionSettings GetSettings(long position)
    {
        if (!_animated)
            return _staticSettings;

        var time = MediaTime.FromSeconds(position / (double)_sampleRate);
        return NoiseReductionParameters.Normalize(
            _sampler!.SampleAt(Reduction, time, _sampleRate),
            _sampler!.SampleAt(Sensitivity, time, _sampleRate),
            _sampler!.SampleAt(Smoothing, time, _sampleRate),
            _sampler!.SampleAt(Adaptation, time, _sampleRate));
    }

    private long ToTerminalSample(MediaTime time)
        => AudioSourceStream.ToEndSample(time.ToSeconds() * _sampleRate);

    private readonly record struct Controls(
        object? ReductionAnimation,
        float Reduction,
        object? SensitivityAnimation,
        float Sensitivity,
        object? SmoothingAnimation,
        float Smoothing,
        object? AdaptationAnimation,
        float Adaptation);

    protected override void Dispose(bool disposing)
    {
        _reducer = null;
        _feed = null;
        _upstreamSnapshot = null;
        _sampler = null;
        base.Dispose(disposing);
    }
}
