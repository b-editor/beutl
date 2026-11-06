using Beutl.Animation;

namespace Beutl.Media;

/// <summary>
/// A shared class that provides time integration and cache management for speed animations.
/// Can be used for both graphics (sampleRate=60) and audio (sampleRate=44100, etc.).
/// </summary>
public sealed class SpeedIntegrator : IDisposable
{
    // Where recent queries stopped within a second, so that going on from one samples only what is new. There are a
    // few because audio alternates between the playhead, the end of the clip and the start of the speed's owner.
    private readonly (long Sample, double Sum, long LastUse)[] _prefixes = new (long, double, long)[4];
    private long _prefixUses;
    private Dictionary<int, double>? _integralCache;
    private int _greatestCachedSecond = -1;
    private KeyFrameAnimation<float>? _trackedAnimation;
    private int _sampleRate;
    private readonly Action? _invalidateCallback;
    private Func<float, float>? _speedTransform;
    internal int CacheVersion { get; private set; }

    public SpeedIntegrator(int sampleRate, Action? invalidateCallback = null)
    {
        _sampleRate = sampleRate;
        _invalidateCallback = invalidateCallback;
    }

    public int SampleRate
    {
        get => _sampleRate;
        set
        {
            if (_sampleRate != value)
            {
                _sampleRate = value;
                Invalidate();
            }
        }
    }

    // A bounded audio time mapping must integrate the same clamped values that its DSP uses.
    internal Func<float, float>? SpeedTransform
    {
        get => _speedTransform;
        set
        {
            if (_speedTransform != value)
            {
                _speedTransform = value;
                Invalidate();
            }
        }
    }

    /// <summary>
    /// Starts tracking the animation and initializes the cache.
    /// If the animation changes, clears the cache and re-registers the event handler.
    /// </summary>
    public void EnsureCache(KeyFrameAnimation<float>? animation)
    {
        _integralCache ??= new Dictionary<int, double>();

        if (!ReferenceEquals(_trackedAnimation, animation))
        {
            Invalidate();

            if (_trackedAnimation != null)
                _trackedAnimation.Edited -= OnAnimationEdited;

            if (animation != null)
                animation.Edited += OnAnimationEdited;

            _trackedAnimation = animation;
        }
    }

    /// <summary>
    /// Returns the largest cache entry at or below the specified second.
    /// </summary>
    public (int Key, double Value) TryGetCache(int targetSec)
    {
        if (_integralCache == null) return (-1, 0);
        if (_integralCache.TryGetValue(targetSec, out double exact)) return (targetSec, exact);

        for (int sec = Math.Min(targetSec, _greatestCachedSecond); sec >= 0; sec--)
        {
            if (_integralCache.TryGetValue(sec, out double result))
            {
                return (sec, result);
            }
        }

        return (-1, 0);
    }

    /// <summary>
    /// Clears the cache.
    /// </summary>
    public void Invalidate()
    {
        CacheVersion++;
        _integralCache?.Clear();
        _greatestCachedSecond = -1;
        Array.Clear(_prefixes);
        _invalidateCallback?.Invoke();
    }

    /// <summary>
    /// Unsubscribes from events and clears the cache.
    /// </summary>
    public void Dispose()
    {
        if (_trackedAnimation != null)
        {
            _trackedAnimation.Edited -= OnAnimationEdited;
            _trackedAnimation = null;
        }
        _integralCache = null;
    }

    /// <summary>
    /// Integrates the speed animation and returns the transformed time corresponding to the specified time.
    /// </summary>
    public MediaTime Integrate(MediaTime timeSpan, KeyFrameAnimation<float> animation)
    {
        int targetSec = (int)timeSpan.ToSeconds();
        (int cachedSec, double cachedSum) = TryGetCache(targetSec);

        double sum = cachedSum;
        int startSec = cachedSec < 0 ? 0 : cachedSec;

        // Integrate speed in 1-second intervals from startSec to targetSec
        for (int sec = startSec; sec < targetSec; sec++)
        {
            if (animation is KeyFrameAnimation<float> keyframes && keyframes.KeyFrames.Count > 0)
            {
                var first = (KeyFrame<float>)keyframes.KeyFrames[0];
                var last = (KeyFrame<float>)keyframes.KeyFrames[^1];
                int constantEnd = sec >= Math.Ceiling(last.KeyTime.Value.ToSeconds())
                    ? targetSec
                    : Math.Min(targetSec, (int)Math.Floor(first.KeyTime.Value.ToSeconds()));
                if (constantEnd > sec)
                {
                    float constant = sec >= Math.Ceiling(last.KeyTime.Value.ToSeconds()) ? last.Value.Value : first.Value.Value;
                    constant = _speedTransform?.Invoke(constant) ?? constant;
                    sum += constant / 100d * (constantEnd - sec);
                    _integralCache![constantEnd] = sum;
                    _greatestCachedSecond = Math.Max(_greatestCachedSecond, constantEnd);
                    sec = constantEnd - 1;
                    continue;
                }
            }
            long start = (long)sec * _sampleRate;
            sum = SumSamples(animation, start, start + _sampleRate, sum, out _);
            _integralCache![sec + 1] = sum;
            _greatestCachedSecond = Math.Max(_greatestCachedSecond, sec + 1);
        }

        // Integrate the remainder from the target second to the exact target time. Sample-domain math
        // uses long to avoid Int32 overflow on long timelines; targetSec stays int (seconds).
        long targetInSamples = (long)(timeSpan.ToSeconds() * _sampleRate);
        long secStartInSamples = (long)targetSec * _sampleRate;

        sum = SumSamples(animation, secStartInSamples, targetInSamples, sum, out int nearest);
        RememberPrefix(nearest, targetInSamples, sum);

        // Interpolate from the last sample to the exact time (fractional sample handling)
        double fractionalSamples = (timeSpan.ToSeconds() * _sampleRate) - targetInSamples;
        if (fractionalSamples > 0)
        {
            float speed = SampleSpeed(animation, timeSpan);
            sum += (speed / 100.0) * fractionalSamples / _sampleRate;
        }

        return MediaTime.FromSeconds(sum);
    }

    // Adds the samples in order from the start of the timeline, so a sum is the same however it was reached. It goes on
    // from the latest prefix in the range when there is one, and reports that prefix's slot, or -1.
    private double SumSamples(KeyFrameAnimation<float> animation, long start, long end, double sum, out int prefix)
    {
        prefix = FindPrefix(start, end);
        if (prefix >= 0)
        {
            start = _prefixes[prefix].Sample;
            sum = _prefixes[prefix].Sum;
        }

        for (long i = start; i < end; i++)
        {
            double t = i / (double)_sampleRate;
            float speed = SampleSpeed(animation, MediaTime.FromSeconds(t));
            sum += (speed / 100.0) / _sampleRate;
        }

        return sum;
    }

    // The latest prefix in the range, or -1. An unused slot stands for no time at all, which sums to zero.
    private int FindPrefix(long first, long last)
    {
        int found = -1;
        for (int i = 0; i < _prefixes.Length; i++)
        {
            long sample = _prefixes[i].Sample;
            if (sample >= first && sample <= last && (found < 0 || sample > _prefixes[found].Sample))
                found = i;
        }

        return found;
    }

    private void RememberPrefix(int slot, long sample, double sum)
    {
        if (slot < 0)
        {
            slot = 0;
            for (int i = 1; i < _prefixes.Length; i++)
            {
                if (_prefixes[i].LastUse < _prefixes[slot].LastUse)
                    slot = i;
            }
        }

        _prefixes[slot] = (sample, sum, ++_prefixUses);
    }

    private float SampleSpeed(KeyFrameAnimation<float> animation, MediaTime time)
    {
        float speed = animation.Interpolate(time);
        return _speedTransform?.Invoke(speed) ?? speed;
    }

    private void OnAnimationEdited(object? sender, EventArgs e)
    {
        Invalidate();
    }
}
