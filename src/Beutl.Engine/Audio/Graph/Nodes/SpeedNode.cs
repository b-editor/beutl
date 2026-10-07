using System.Buffers;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

public sealed partial class SpeedNode : AudioNode
{
    // Processor for audio speed processing
    private SpeedProcessor? _processor;
    private int _lastSampleRate;
    private List<AudioNode>? _upstreamSnapshot;
    private bool _mappingInvalidated;
    private double _lastAnimatedSpeed;
    private bool _hasLastAnimatedSpeed;

    private readonly SpeedIntegrator _integrator;

    public SpeedNode()
    {
        _integrator = new SpeedIntegrator(0, () =>
        {
            _mappingInvalidated = true;
            _hasLastAnimatedSpeed = false;
        });
    }

    public IProperty<float>? Speed { get; set; }

    public override AudioBuffer Process(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: false));

    public override AudioBuffer Flush(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: true));

    /// <summary>
    /// Converts upstream latency from source-domain samples to the output-domain samples that a caller
    /// must reserve to drain it through this speed mapping. Static speed uses its current value;
    /// keyframed speed and animations that expose a conservative output range use their slowest value.
    /// </summary>
    public override int GetTotalLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        int upstreamLatency = GetMaxInputLatency(sampleRate, drain: false);
        if (upstreamLatency == 0)
            return 0;
        if (upstreamLatency == int.MaxValue)
            return int.MaxValue;

        if (!TryGetBoundedMinimumSpeedFactor(out double minimumSpeed))
        {
            // Saturate when no positive finite speed bounds the drain duration.
            return int.MaxValue;
        }

        double scaled = Math.Ceiling(upstreamLatency / minimumSpeed);
        return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
    }

    public override int GetDrainLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        int upstreamLatency = GetMaxInputLatency(sampleRate, drain: true);
        if (upstreamLatency == 0 || upstreamLatency == int.MaxValue)
            return upstreamLatency;

        if (!TryGetDrainSpeedFactor(sampleRate, out double drainSpeed))
        {
            return int.MaxValue;
        }

        double scaled = Math.Ceiling(upstreamLatency / drainSpeed);
        return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
    }

    internal bool TryGetDrainSpeedFactor(int sampleRate, out double drainSpeed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        if (Speed?.Animation is not null)
        {
            // An animation whose output range cannot be proven bounded remains unbounded even when
            // its last sampled value happened to be finite.
            if (!TryGetBoundedMinimumSpeedFactor(out double minimumSpeed))
            {
                drainSpeed = default;
                return false;
            }

            drainSpeed = _hasLastAnimatedSpeed && _lastSampleRate == sampleRate
                ? _lastAnimatedSpeed
                : minimumSpeed;
        }
        else
        {
            drainSpeed = (Speed?.CurrentValue ?? 100f) / 100d;
        }

        return double.IsFinite(drainSpeed) && drainSpeed > 0;
    }

    // Only a positive finite minimum speed bounds how long the upstream tail takes to drain.
    private bool TryGetBoundedMinimumSpeedFactor(out double minimumSpeed)
    {
        return TryGetMinimumSpeedFactor(out minimumSpeed)
            && double.IsFinite(minimumSpeed)
            && minimumSpeed > 0;
    }

    private bool TryGetMinimumSpeedFactor(out double minimumSpeed)
    {
        IAnimation<float>? speedAnimation = Speed?.Animation;
        if (speedAnimation is null)
        {
            minimumSpeed = (Speed?.CurrentValue ?? 100f) / 100d;
            return true;
        }

        if (!speedAnimation.TryGetOutputRange(out float minimumPercent, out float maximumPercent)
            || !float.IsFinite(minimumPercent)
            || !float.IsFinite(maximumPercent)
            || minimumPercent > maximumPercent)
        {
            minimumSpeed = default;
            return false;
        }

        minimumSpeed = minimumPercent / 100d;
        return true;
    }

    private AudioBuffer ProcessCore(AudioProcessContext context, bool draining)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Inputs.Count != 1)
            throw new InvalidOperationException("Variable speed node requires exactly one input.");

        // Calculate the expected output sample count based on the context's time range
        var expectedOutputSampleCount = context.GetSampleCount();

        var animation = Speed?.Animation;
        _integrator.EnsureCache(animation);
        _integrator.SampleRate = context.SampleRate;

        // Recreate the processor when the upstream changes, so the resampler does not carry filter
        // history (or a stale read cursor) from a disconnected source into the new stream. Comparing
        // Inputs[0] alone is not enough: the graph reuses one ResampleNode keyed by sample rate, so
        // Inputs[0] can be unchanged while the source feeding it is recreated. Snapshot the whole
        // transitive upstream and compare by identity. Capture unconditionally so the first chunk seeds
        // the snapshot and a contiguous second chunk is not mistaken for a swap.
        bool upstreamChanged = UpstreamChangedAndCapture();
        if (_processor == null || _lastSampleRate != context.SampleRate || upstreamChanged)
        {
            _processor = new SpeedProcessor(context.SampleRate, 2, this);
            _lastSampleRate = context.SampleRate;
        }

        bool forceReanchor = _mappingInvalidated && !draining;
        AudioBuffer result = animation == null
            ? ProcessStaticSpeed(context, expectedOutputSampleCount, draining, forceReanchor)
            : ProcessAnimatedSpeed(context, expectedOutputSampleCount, draining, forceReanchor);
        if (!draining)
            _mappingInvalidated = false;
        return result;
    }

    private AudioBuffer ProcessStaticSpeed(
        AudioProcessContext context,
        int expectedOutputSampleCount,
        bool draining,
        bool forceReanchor)
    {
        float speed = (Speed?.CurrentValue ?? 100f) / 100f;
        // If speed is 1.0, use normal processing
        if (Math.Abs(speed - 1.0f) < float.Epsilon
            && (!draining || _processor!.CanPassThroughDrain))
        {
            AudioBuffer result = draining
                ? Inputs[0].Flush(context)
                : Inputs[0].Process(context);
            _processor!.TrackPassthrough(context, expectedOutputSampleCount);
            return result;
        }

        // The processor streams the source continuously, deriving the read range itself so the
        // resampler is never re-seeked mid-stream.
        return _processor!.ProcessBuffer(
            context,
            speed,
            expectedOutputSampleCount,
            draining,
            forceReanchor);
    }

    private AudioBuffer ProcessAnimatedSpeed(
        AudioProcessContext context,
        int expectedOutputSampleCount,
        bool draining,
        bool forceReanchor)
    {
        var animation = Speed?.Animation!;

        // ClipNode 通過後の context.TimeRange.Start は要素ローカル時刻。
        // SpeedIntegrator.Integrate(t) は「時刻 0 から t までの累積積分」を返すため、
        // UseGlobalClock=true でグローバル時刻を渡す場合は要素開始前の積分 Integrate(ownerStart)
        // を差し引いて「要素開始からの累積」へ揃える必要がある。
        // per-sample 評価で使う GetAnimatedValue は常にグローバル時刻入力を前提とするため、
        // owner.TimeRange.Start を一律加算してグローバル時刻へ変換する。
        var ownerStart = Speed?.GetOwnerObject()?.TimeRange.Start ?? TimeSpan.Zero;
        TimeSpan sourceStartTime;
        if (animation.UseGlobalClock)
        {
            sourceStartTime = _integrator.Integrate(context.TimeRange.Start + ownerStart, animation)
                            - _integrator.Integrate(ownerStart, animation);
        }
        else
        {
            sourceStartTime = _integrator.Integrate(context.TimeRange.Start, animation);
        }

        // Per-sample speed buffer, sized to expectedOutputSampleCount and allocated every render —
        // rent from ArrayPool to avoid hot-path GC pressure. ProcessBufferWithVariableSpeed consumes
        // the span synchronously without retaining it, so the array is safe to return afterwards.
        var startInSamples = AudioMath.TimeToSampleIndex(context.TimeRange.Start, context.SampleRate);
        double[] speedsArray = ArrayPool<double>.Shared.Rent(expectedOutputSampleCount);
        try
        {
            // The rented array can be larger than requested, so always slice before passing it.
            Span<double> speeds = speedsArray.AsSpan(0, expectedOutputSampleCount);
            // A drain represents retained upstream samples after the clip ended; future animation
            // values must not change the mapping of that tail.
            if (draining && _hasLastAnimatedSpeed)
            {
                speeds.Fill(_lastAnimatedSpeed);
            }
            else
            {
                for (int i = 0; i < expectedOutputSampleCount; i++)
                {
                    speeds[i] = animation.GetAnimatedValue(
                        ownerStart + TimeSpan.FromSeconds((startInSamples + i) / (double)context.SampleRate)) / 100.0;
                }

                if (!draining && expectedOutputSampleCount > 0)
                {
                    _lastAnimatedSpeed = speeds[^1];
                    _hasLastAnimatedSpeed = true;
                }
            }

            // sourceStartTime only seeds the read cursor on the first chunk / after a seek; context
            // supplies the sampler and original time range for the per-read sub-contexts.
            return _processor!.ProcessBufferWithVariableSpeed(
                context,
                speeds,
                expectedOutputSampleCount,
                sourceStartTime.TotalSeconds,
                draining,
                forceReanchor);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(speedsArray);
        }
    }

    // Snapshots the transitive upstream nodes (depth-first, deduplicated) and reports whether they
    // differ from the previous snapshot by identity. Audio graphs are tiny and this runs once per
    // chunk, so the walk is negligible.
    private bool UpstreamChangedAndCapture()
    {
        var current = new List<AudioNode>();
        CollectUpstream(this, current, new HashSet<AudioNode>());

        bool changed = _upstreamSnapshot is null || _upstreamSnapshot.Count != current.Count;
        if (!changed)
        {
            for (int i = 0; i < current.Count; i++)
            {
                if (!ReferenceEquals(_upstreamSnapshot![i], current[i]))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
            _upstreamSnapshot = current;

        return changed;
    }

    private static void CollectUpstream(AudioNode node, List<AudioNode> acc, HashSet<AudioNode> visited)
    {
        foreach (var input in node.Inputs)
        {
            // Dedupe so a diamond upstream cannot blow up the walk or perturb the snapshot.
            if (!visited.Add(input))
                continue;

            acc.Add(input);
            CollectUpstream(input, acc, visited);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _integrator.Dispose();
        _processor = null;
        _upstreamSnapshot = null;
        _hasLastAnimatedSpeed = false;
        base.Dispose(disposing);
    }
}
