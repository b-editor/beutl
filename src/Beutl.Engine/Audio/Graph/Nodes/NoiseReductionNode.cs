using System.Buffers;
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
    private const int Channels = 2;
    private const int MaxReadFrames = 16384;

    // Audio around a restart that seeds the noise floor. At most half of it precedes the restart, so a
    // seek warms the analysis up on audio the output never plays.
    private const double SeedSeconds = 1;

    private readonly Func<long, NoiseReductionSettings> _settingsAt;
    private SpectralNoiseReducer? _reducer;
    private AudioSourceStream? _stream;
    private List<AudioNode>? _upstreamSnapshot;
    private int _sampleRate;
    private TimeSpan? _lastTimeRangeEnd;
    private AnimationSampler? _sampler;
    private NoiseReductionSettings _staticSettings;
    private bool _animated;

    // Live input stops at the terminal, or where a drain began. The upstream's held tail follows until
    // _drainEnd (null when the upstream reports an unbounded tail), and silence after that.
    private long? _terminal;
    private long? _liveEnd;
    private long? _drainEnd;

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
        var output = new AudioBuffer(context.SampleRate, Channels, count);

        // An empty chunk carries no audio, and advancing the end here would mask a discontinuity.
        if (count == 0)
            return output;

        try
        {
            if (Prepare(context, draining))
            {
                SpectralNoiseReducer reducer = _reducer!;
                FillInput(reducer.GetRequiredInputEnd(reducer.OutputPosition + count), context);
                reducer.Read(output.GetChannelData(0), output.GetChannelData(1), _settingsAt);
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
        if (_reducer is null || _sampleRate != sampleRate || upstreamChanged)
        {
            _reducer = new SpectralNoiseReducer(sampleRate);
            _stream = new AudioSourceStream(sampleRate, Channels, static time => time);
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

            // The source is exhausted where the last block ended, so only the upstream's held tail
            // may follow. The upstream has already run past that boundary and cannot be rewound
            // without losing its state, such as a delay line's echoes. The live input read over the
            // tail's span stands in for the tail, and anything read beyond it becomes silence.
            long boundary = _reducer.OutputPosition;
            if (_liveEnd is not { } liveUntil || liveUntil > boundary)
            {
                int latency = GetMaxInputLatency(_sampleRate, drain: true);
                long tailEnd = latency == int.MaxValue ? long.MaxValue : boundary + latency;
                if (tailEnd < _reducer.InputEnd)
                {
                    _reducer.EndInputAt(tailEnd);
                    _liveEnd = boundary;
                    _drainEnd = _reducer.InputEnd;
                }
                else if (_liveEnd is null)
                {
                    // Everything read so far lies within the tail; the upstream drains the rest.
                    _liveEnd = _reducer.InputEnd;
                    _drainEnd = latency == int.MaxValue ? null : tailEnd;
                }
            }

            return true;
        }

        long? terminal = context.ProcessEndTime is { } processEnd ? ToTerminalSample(processEnd) : null;

        // An edit that moves the terminal invalidates input read past the earlier of the two ends.
        bool terminalMoved = _liveEnd is { } liveEnd
            ? terminal != liveEnd
            : terminal is { } value && value < _reducer.InputEnd;
        if (!contiguous || terminalMoved)
        {
            // Truncate like SourceNode and ResampleNode, so a start between samples maps to the
            // sample the upstream delivers there.
            long outputStart = AudioMath.TimeToSampleIndex(context.TimeRange.Start, _sampleRate);
            Restart(context, outputStart, terminal);
        }
        else if (_inputStale)
        {
            // A changed upstream gain or source would leave the old floor wrong until it aged out.
            Restart(context, _reducer.OutputPosition, terminal);
        }
        else
        {
            _terminal = terminal;
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
        long origin = FloorToHop(Math.Max(earliest, outputStart - seed / 2), reducer.HopSize);

        reducer.Reset(origin, outputStart);
        _stream!.Begin(outputStart / (double)_sampleRate, origin / (double)_sampleRate, forceReanchor: true);
        _terminal = terminal;
        _liveEnd = null;
        _drainEnd = null;

        long seedEnd = origin + seed;
        FillInput(seedEnd, context);
        reducer.Seed(seedEnd);
    }

    // Live input up to the terminal, then the upstream's held tail, then silence.
    private void FillInput(long target, AudioProcessContext context)
    {
        SpectralNoiseReducer reducer = _reducer!;
        while (reducer.InputEnd < target)
        {
            long position = reducer.InputEnd;
            int frames = (int)Math.Min(target - position, MaxReadFrames);
            if (_liveEnd is null)
            {
                if ((_terminal is not { } terminal || position < terminal)
                    && ReadUpstream(frames, context, draining: false, _terminal) > 0)
                {
                    continue;
                }

                EndLiveInput(position);
            }

            if (_drainEnd is not { } drainEnd || position < drainEnd)
            {
                if (ReadUpstream(frames, context, draining: true, _drainEnd) > 0)
                    continue;

                _drainEnd = position;
            }

            reducer.WritePadding((int)(target - position));
        }
    }

    private void EndLiveInput(long position)
    {
        _liveEnd = position;
        int latency = GetMaxInputLatency(_sampleRate, drain: true);
        _drainEnd = latency == int.MaxValue ? null : position + latency;
    }

    private int ReadUpstream(int frames, AudioProcessContext context, bool draining, long? end)
    {
        int length = frames * Channels;
        float[] buffer = ArrayPool<float>.Shared.Rent(length);
        try
        {
            int samples = _stream!.Read(buffer.AsSpan(0, length), Inputs[0], context, draining, end);
            _reducer!.Write(buffer.AsSpan(0, samples));
            return samples / Channels;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    private void CaptureSettings(AudioProcessContext context)
    {
        _sampler = context.AnimationSampler;
        _processedControls = ReadControls();
        _animated = Reduction.Animation != null
                    || Sensitivity.Animation != null
                    || Smoothing.Animation != null
                    || Adaptation.Animation != null;
        _staticSettings = NoiseReductionParameters.Normalize(
            Reduction.CurrentValue,
            Sensitivity.CurrentValue,
            Smoothing.CurrentValue,
            Adaptation.CurrentValue);
    }

    // Animated controls are compared by animation; static ones by value.
    private Controls ReadControls()
    {
        return new Controls(
            Reduction.Animation, Reduction.Animation is null ? Reduction.CurrentValue : 0,
            Sensitivity.Animation, Sensitivity.Animation is null ? Sensitivity.CurrentValue : 0,
            Smoothing.Animation, Smoothing.Animation is null ? Smoothing.CurrentValue : 0,
            Adaptation.Animation, Adaptation.Animation is null ? Adaptation.CurrentValue : 0);
    }

    // Animated parameters are evaluated once per frame, at the frame's center.
    private NoiseReductionSettings GetSettings(long position)
    {
        if (!_animated)
            return _staticSettings;

        var time = TimeSpan.FromSeconds(position / (double)_sampleRate);
        return NoiseReductionParameters.Normalize(
            Sample(Reduction, time),
            Sample(Sensitivity, time),
            Sample(Smoothing, time),
            Sample(Adaptation, time));
    }

    private float Sample(IProperty<float> property, TimeSpan time)
    {
        if (property.Animation is null || _sampler is null)
            return property.CurrentValue;

        Span<float> value = stackalloc float[1];
        _sampler.SampleBuffer(property, new TimeRange(time, TimeSpan.Zero), _sampleRate, value);
        return value[0];
    }

    // A fractional end still contains the sample it cuts into.
    private long ToTerminalSample(TimeSpan time)
    {
        double value = time.TotalSeconds * _sampleRate;
        double rounded = Math.Round(value);
        return (long)(Math.Abs(value - rounded) < 1e-6 ? rounded : Math.Ceiling(value));
    }

    private static long FloorToHop(long position, int hop)
    {
        long remainder = position % hop;
        return position - (remainder < 0 ? remainder + hop : remainder);
    }

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
        _stream = null;
        _upstreamSnapshot = null;
        _sampler = null;
        base.Dispose(disposing);
    }
}
