using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Graph;

// Shared time mapping for speed-based nodes; each node supplies its own speed policy and DSP.
internal sealed class AudioSpeedMapping : IDisposable
{
    private readonly SpeedIntegrator _integrator;
    private readonly Func<float, float>? _normalize;
    private readonly double? _minimumSpeedFallback;
    private bool _hasLastAnimatedSpeed;
    private double _lastAnimatedSpeed;
    private int _lastSampleRate;
    private float? _expressionPercent;
    private FiniteEndKey? _finiteEndKey;
    private double _finiteEndValue;

    private readonly record struct FiniteEndKey(KeyFrameAnimation<float> Animation, int Version, int SampleRate,
        double InputEnd, MediaTime OwnerStart, bool GlobalClock);

    public AudioSpeedMapping(Func<float, float>? normalize = null, double? minimumSpeedFallback = null)
    {
        _normalize = normalize;
        _minimumSpeedFallback = minimumSpeedFallback;
        _integrator = new SpeedIntegrator(0, () =>
        {
            IsInvalidated = true;
            _hasLastAnimatedSpeed = false;
        });
        _integrator.SpeedTransform = normalize;
    }

    public IProperty<float>? Speed { get; set; }
    public bool IsInvalidated { get; private set; }
    public float StaticSpeed => Normalize(CurrentPercent) / 100f;

    // Keyframes vary the speed within a window only while no expression sits on top of them.
    public bool IsAnimated => Animation is not null;

    private bool HasExpression => Speed is IExpressionProperty { HasExpression: true };

    private KeyFrameAnimation<float>? KeyFrames => HasExpression ? null : Speed?.GetKeyFrameAnimation();

    // Keyframes that can only give one speed play as that speed does when it is fixed.
    private KeyFrameAnimation<float>? Animation
        => KeyFrames is { } animation && !TryGetConstantPercent(animation, out _) ? animation : null;

    // The speed of a window that does not vary within it, the one an expression gave at its start if there is one.
    private float CurrentPercent
    {
        get
        {
            if (HasExpression && _expressionPercent is { } expression) return expression;
            if (KeyFrames is { } animation && TryGetConstantPercent(animation, out float constant)) return constant;
            return Speed?.Value ?? 100f;
        }
    }

    public void Configure(int sampleRate)
    {
        _integrator.EnsureCache(KeyFrames);
        _integrator.SampleRate = sampleRate;
    }

    // Speeds of zero or below are left to the animated path.
    private bool TryGetConstantPercent(KeyFrameAnimation<float> animation, out float percent)
    {
        percent = default;
        if (animation.KeyFrames.Count == 0 || animation.KeyFrames[0] is not KeyFrame<float> first)
            return false;

        // Each keyframe's value is reached, so most animations are ruled out at the first one that differs.
        float speed = Normalize(first.Value.Value);
        for (int i = 1; i < animation.KeyFrames.Count; i++)
        {
            if (animation.KeyFrames[i] is not KeyFrame<float> next || Normalize(next.Value.Value) != speed)
                return false;
        }

        // An easing can overshoot the keyframes around it, and one of no known range can give anything.
        if (!(speed > 0)
            || !animation.TryGetOutputRange(out float minimum, out float maximum)
            || Normalize(minimum) != speed
            || Normalize(maximum) != speed)
        {
            return false;
        }

        percent = first.Value.Value;
        return true;
    }

    // An expression runs a script, so it is evaluated once for each window, at its start, and the window plays at
    // that speed as at a fixed one. A change between windows re-anchors the source as a fixed speed's edit does.
    public void BeginWindow(AudioProcessContext context)
    {
        _expressionPercent = HasExpression
            ? context.AnimationSampler.SampleAt(Speed!, context.TimeRange.Start, context.SampleRate)
            : null;
    }

    private MediaTime GetOwnerStart() => AnimationSampler.GetOwnerStart(Speed!);

    public void AcknowledgeChanges() => IsInvalidated = false;
    private float Normalize(float percent) => _normalize?.Invoke(percent) ?? percent;

    public MediaTime MapOutputTimeToSource(MediaTime time)
    {
        if (Animation is not { } animation)
        {
            double factor = Normalize(CurrentPercent) / 100d;
            return MediaTime.FromSeconds(time.ToSeconds() * factor);
        }

        var ownerStart = GetOwnerStart();
        return animation.UseGlobalClock.Value
            ? _integrator.Integrate(time + ownerStart, animation) - _integrator.Integrate(ownerStart, animation)
            : _integrator.Integrate(time, animation);
    }

    public double? GetFiniteSourceEndSample(double? inputEnd, int sampleRate)
    {
        // An expression's speed is known for one window only, which bounds no end, and is asked for before the
        // first window is evaluated.
        if (HasExpression)
            return null;
        if (inputEnd is not { } end || !TryGetBoundedMinimumSpeedFactor(out double minimumSpeed))
            return null;
        if (end <= 0)
            return 0;
        if (Animation is not { } animation)
            return end / minimumSpeed;

        Configure(sampleRate);
        var ownerStart = GetOwnerStart();
        var key = new FiniteEndKey(animation, _integrator.CacheVersion, sampleRate, end,
            ownerStart, animation.UseGlobalClock.Value);
        if (_finiteEndKey == key)
            return _finiteEndValue;
        MediaTime origin = animation.UseGlobalClock.Value
            ? _integrator.Integrate(ownerStart, animation)
            : MediaTime.Zero;
        double low = 0;
        double high = end / minimumSpeed;
        for (int i = 0; i < 64 && high - low > 1e-4; i++)
        {
            double middle = (low + high) / 2;
            var time = MediaTime.FromSeconds(middle / sampleRate);
            if (animation.UseGlobalClock.Value)
                time += ownerStart;
            double mapped = (_integrator.Integrate(time, animation) - origin).ToSeconds() * sampleRate;
            if (mapped < end)
                low = middle;
            else
                high = middle;
        }
        _finiteEndKey = key;
        _finiteEndValue = high;
        return high;
    }

    private bool TryGetBoundedMinimumSpeedFactor(out double minimumSpeed)
    {
        return TryGetMinimumSpeedFactor(out minimumSpeed)
            && double.IsFinite(minimumSpeed)
            && minimumSpeed > 0;
    }

    private bool TryGetMinimumSpeedFactor(out double minimumSpeed)
    {
        KeyFrameAnimation<float>? speedAnimation = Animation;
        if (speedAnimation is null)
        {
            minimumSpeed = Normalize(CurrentPercent) / 100d;
            return true;
        }

        if (!speedAnimation.TryGetOutputRange(out float minimumPercent, out float maximumPercent)
            || !float.IsFinite(minimumPercent)
            || !float.IsFinite(maximumPercent)
            || minimumPercent > maximumPercent)
        {
            minimumSpeed = _minimumSpeedFallback ?? default;
            return _minimumSpeedFallback.HasValue;
        }

        minimumSpeed = Normalize(minimumPercent) / 100d;
        return true;
    }

    public int GetTotalLatencySamples(int sampleRate, int upstreamLatency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

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

    public int GetDrainLatencySamples(int sampleRate, int upstreamLatency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        if (upstreamLatency == 0 || upstreamLatency == int.MaxValue)
            return upstreamLatency;

        if (!TryGetDrainSpeedFactor(sampleRate, out double drainSpeed))
        {
            return int.MaxValue;
        }

        double scaled = Math.Ceiling(upstreamLatency / drainSpeed);
        return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
    }

    public bool TryGetDrainSpeedFactor(int sampleRate, out double drainSpeed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        if (Animation is not null)
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
            drainSpeed = Normalize(CurrentPercent) / 100d;
        }

        return double.IsFinite(drainSpeed) && drainSpeed > 0;
    }

    public void FillSpeedCurve(AudioProcessContext context, Span<double> speeds, bool draining, bool roundSampleClock)
    {
        if (draining && _hasLastAnimatedSpeed)
        {
            speeds.Fill(_lastAnimatedSpeed);
            return;
        }

        var animation = KeyFrames!;
        var ownerStart = GetOwnerStart();
        long startInSamples = context.TimeRange.Start.ToSamples(
            context.SampleRate, roundSampleClock ? MidpointRounding.AwayFromZero : MidpointRounding.ToZero);
        for (int i = 0; i < speeds.Length; i++)
        {
            float percent = animation.GetAnimatedValue(
                ownerStart + MediaTime.FromSamples(startInSamples + i, context.SampleRate));
            speeds[i] = Normalize(percent) / 100d;
        }
        if (!draining && speeds.Length > 0)
        {
            _lastAnimatedSpeed = speeds[^1];
            _lastSampleRate = context.SampleRate;
            _hasLastAnimatedSpeed = true;
        }
    }

    public void Dispose() => _integrator.Dispose();
}

// Used by tail accounting to convert sample budgets through any speed-based node.
internal interface IAudioTimeMappingNode
{
    bool TryGetDrainSpeedFactor(int sampleRate, out double speed);
}
