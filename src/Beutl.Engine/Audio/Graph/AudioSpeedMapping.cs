using Beutl.Animation;
using Beutl.Engine;
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
    private FiniteEndKey? _finiteEndKey;
    private double _finiteEndValue;

    private readonly record struct FiniteEndKey(IAnimation<float> Animation, int Version, int SampleRate,
        double InputEnd, TimeSpan OwnerStart, bool GlobalClock);

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
    public float StaticSpeed => Normalize(Speed?.CurrentValue ?? 100f) / 100f;

    public void Configure(int sampleRate)
    {
        _integrator.EnsureCache(Speed?.Animation);
        _integrator.SampleRate = sampleRate;
    }

    public void AcknowledgeChanges() => IsInvalidated = false;
    private float Normalize(float percent) => _normalize?.Invoke(percent) ?? percent;

    public TimeSpan MapOutputTimeToSource(TimeSpan time)
    {
        if (Speed?.Animation is not { } animation)
        {
            float percent = Speed?.CurrentValue ?? 100f;
            double factor = Normalize(percent) / 100d;
            return TimeSpan.FromSeconds(time.TotalSeconds * factor);
        }

        var ownerStart = Speed.GetOwnerObject()?.TimeRange.Start ?? TimeSpan.Zero;
        return animation.UseGlobalClock
            ? _integrator.Integrate(time + ownerStart, animation) - _integrator.Integrate(ownerStart, animation)
            : _integrator.Integrate(time, animation);
    }

    public double? GetFiniteSourceEndSample(double? inputEnd, int sampleRate)
    {
        if (inputEnd is not { } end || !TryGetBoundedMinimumSpeedFactor(out double minimumSpeed))
            return null;
        if (end <= 0)
            return 0;
        if (Speed?.Animation is not { } animation)
            return end / minimumSpeed;

        Configure(sampleRate);
        var ownerStart = Speed.GetOwnerObject()?.TimeRange.Start ?? TimeSpan.Zero;
        var key = new FiniteEndKey(animation, _integrator.CacheVersion, sampleRate, end,
            ownerStart, animation.UseGlobalClock);
        if (_finiteEndKey == key)
            return _finiteEndValue;
        TimeSpan origin = animation.UseGlobalClock
            ? _integrator.Integrate(ownerStart, animation)
            : TimeSpan.Zero;
        double low = 0;
        double high = end / minimumSpeed;
        for (int i = 0; i < 64 && high - low > 1e-4; i++)
        {
            double middle = (low + high) / 2;
            var time = TimeSpan.FromSeconds(middle / sampleRate);
            if (animation.UseGlobalClock)
                time += ownerStart;
            double mapped = (_integrator.Integrate(time, animation) - origin).TotalSeconds * sampleRate;
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
        IAnimation<float>? speedAnimation = Speed?.Animation;
        if (speedAnimation is null)
        {
            minimumSpeed = Normalize(Speed?.CurrentValue ?? 100f) / 100d;
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
            float percent = Speed?.CurrentValue ?? 100f;
            drainSpeed = Normalize(percent) / 100d;
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

        var animation = Speed!.Animation!;
        var ownerStart = Speed.GetOwnerObject()?.TimeRange.Start ?? TimeSpan.Zero;
        Int128 samplePosition = (Int128)context.TimeRange.Start.Ticks * context.SampleRate;
        long startInSamples = roundSampleClock
            ? checked((long)((samplePosition + (samplePosition < 0
                ? -TimeSpan.TicksPerSecond / 2
                : TimeSpan.TicksPerSecond / 2)) / TimeSpan.TicksPerSecond))
            : AudioMath.TimeToSampleIndex(context.TimeRange.Start, context.SampleRate);
        for (int i = 0; i < speeds.Length; i++)
        {
            float percent = animation.GetAnimatedValue(
                ownerStart + TimeSpan.FromSeconds((startInSamples + i) / (double)context.SampleRate));
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
