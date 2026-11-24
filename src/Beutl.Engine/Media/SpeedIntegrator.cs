using Beutl.Animation;
using Beutl.Animation.Animators;
using Beutl.Animation.Easings;

namespace Beutl.Media;

/// <summary>
/// Maps time through a keyframed speed curve by integrating its speed in percent.
/// </summary>
public sealed class SpeedIntegrator : IDisposable
{
    private static readonly double s_relativeTolerance = Math.ScaleB(1d, -20);

    private readonly Action? _invalidateCallback;
    private KeyFrameAnimation<float>? _trackedAnimation;
    private KeySnapshot[] _keys = [];
    private bool _hasKeySnapshot;
    private bool _useGlobalClock;
    private Curve? _curve;
    private Func<float, float>? _speedTransform;

    internal int CacheVersion { get; private set; }

    public SpeedIntegrator(Action? invalidateCallback = null)
    {
        _invalidateCallback = invalidateCallback;
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
    /// Prepares the cache for the animation and invalidates it if the animation or its keys changed.
    /// </summary>
    public void EnsureCache(KeyFrameAnimation<float>? animation)
    {
        if (!ReferenceEquals(_trackedAnimation, animation))
        {
            // A caller with a callback can act on edits before asking for another integral.
            if (_invalidateCallback != null)
            {
                if (_trackedAnimation != null) _trackedAnimation.Edited -= OnAnimationEdited;
                if (animation != null) animation.Edited += OnAnimationEdited;
            }
            _trackedAnimation = animation;
            _keys = [];
            _hasKeySnapshot = false;
            Invalidate();
        }

        if (animation == null || (_hasKeySnapshot && KeysMatch(animation))) return;

        // Structural edits and changes to the spline's control points can leave Edited silent.
        _keys = [.. animation.KeyFrames.Select(KeySnapshot.Read)];
        _useGlobalClock = animation.UseGlobalClock.Value;
        _curve = null;
        bool invalidate = _hasKeySnapshot;
        _hasKeySnapshot = true;
        if (invalidate) Invalidate();
    }

    private bool KeysMatch(KeyFrameAnimation<float> animation)
    {
        var keys = animation.KeyFrames;
        if (_useGlobalClock != animation.UseGlobalClock.Value || _keys.Length != keys.Count) return false;

        for (int i = 0; i < _keys.Length; i++)
        {
            if (!_keys[i].Matches(KeySnapshot.Read(keys[i]))) return false;
        }

        return true;
    }

    /// <summary>
    /// Clears the cache.
    /// </summary>
    public void Invalidate()
    {
        CacheVersion++;
        _curve = null;
        _invalidateCallback?.Invoke();
    }

    /// <summary>
    /// Unsubscribes from edits and clears the cache.
    /// </summary>
    public void Dispose()
    {
        if (_invalidateCallback != null && _trackedAnimation != null)
            _trackedAnimation.Edited -= OnAnimationEdited;
        _trackedAnimation = null;
        _keys = [];
        _hasKeySnapshot = false;
        _curve = null;
    }

    /// <summary>
    /// Integrates the speed from zero to <paramref name="timeSpan"/> in the animation's clock.
    /// Playback holds at zero for negative times. Outside the keys their endpoint values hold.
    /// </summary>
    public MediaTime Integrate(MediaTime timeSpan, KeyFrameAnimation<float> animation)
    {
        EnsureCache(animation);
        return IntegratePlayback(MediaTime.Zero, timeSpan);
    }

    internal MediaTime IntegratePlayback(MediaTime from, MediaTime to)
        => IntegrateCached(new MediaTime(Math.Max(from.Flicks, 0)), new MediaTime(Math.Max(to.Flicks, 0)));

    // Reuse the checked snapshot for batches such as inverse mapping by bisection.
    internal MediaTime IntegrateCached(MediaTime from, MediaTime to)
    {
        if (from == to) return MediaTime.Zero;
        Curve curve = _curve ??= BuildCurve(_trackedAnimation!, _keys);
        double area = from < to
            ? curve.Integrate(from.Flicks, to.Flicks)
            : -curve.Integrate(to.Flicks, from.Flicks);
        return MediaTime.FromSeconds(area / MediaTime.FlicksPerSecond);
    }

    private Curve BuildCurve(KeyFrameAnimation<float> animation, KeySnapshot[] keys)
    {
        if (keys.Length == 0)
        {
            double rate = Factor(KeyFrame<float>.s_animator.DefaultValue());
            return new Curve(keys, [], [], rate, rate);
        }

        var pieces = new Piece[keys.Length - 1];
        var sums = new double[keys.Length];
        bool linearlyInterpolated = animation.HierarchicalParent is IComponentInterpolator<float> component
            ? component.InterpolatesLinearly
            : KeyFrame<float>.s_animator is FloatAnimator;

        for (int i = 0; i < pieces.Length; i++)
        {
            double width = Width(keys[i].Time.Flicks, keys[i + 1].Time.Flicks);
            sums[i + 1] = sums[i];
            if (width == 0) continue;

            Piece piece = BuildPiece(animation, keys[i], keys[i + 1], width, linearlyInterpolated);
            pieces[i] = piece;
            sums[i + 1] += piece.Integrate(0, 1, width, width);
        }

        return new Curve(keys, pieces, sums, Factor(keys[0].Value), Factor(keys[^1].Value));
    }

    private Piece BuildPiece(KeyFrameAnimation<float> animation, KeySnapshot from, KeySnapshot to, double width,
        bool linearlyInterpolated)
    {
        Easing easing = to.Easing;
        if (linearlyInterpolated)
        {
            if (easing is HoldEasing || (from.Value == to.Value
                && easing.TryGetOutputRange(out float minimum, out float maximum)
                && float.IsFinite(minimum) && float.IsFinite(maximum)))
            {
                return new Piece(Factor(from.Value), 0);
            }

            if (easing is LinearEasing && _speedTransform == null)
                return new Piece(from.Value / 100d, (to.Value - (double)from.Value) / 100d);
        }

        // Keep a spline's evaluations and its cached areas on the same set of control points.
        if (easing.GetType() == typeof(SplineEasing))
        {
            var (x1, y1, x2, y2) = to.ControlPoints;
            easing = new SplineEasing(new KeySpline(x1, y1, x2, y2));
        }

        double Rate(double progress)
        {
            float eased = easing.Ease((float)progress);
            var time = new MediaTime((long)((Int128)from.Time.Flicks + (Int128)Math.Round(progress * width)));
            float value = animation.InterpolateValues(eased, from.Value, to.Value, from.Time, to.Time, time);
            return Factor(value);
        }

        // Custom easings can hide a narrow peak between the nodes of a single rule.
        int panels = easing.GetType().Assembly == typeof(Easing).Assembly ? 1 : 32;
        double minimumWidth = Math.Max(1024 / width, Math.ScaleB(1d, -20));
        var (ends, sums) = GaussKronrod.Partition(Rate, 0, 1, s_relativeTolerance, minimumWidth, panels);
        return new Piece(0, 0, new EasedIntegral(Rate, ends, sums));
    }

    private double Factor(float percent) => (_speedTransform?.Invoke(percent) ?? percent) / 100d;

    private static double Width(long from, long to) => (double)((Int128)to - from);

    private void OnAnimationEdited(object? sender, EventArgs e)
    {
        _hasKeySnapshot = false;
        Invalidate();
    }

    private sealed class Curve(KeySnapshot[] keys, Piece[] pieces, double[] sums, double before, double after)
    {
        public double Integrate(long from, long to)
        {
            int first = KeyAtOrBefore(from);
            int last = KeyAtOrBefore(to);
            if (first == last) return IntegratePiece(first, from, to);

            // Integrate the partial pieces directly so a common tail or prefix loses no precision.
            double middle = last == first + 1 ? 0 : sums[last] - sums[first + 1];
            return IntegratePiece(first, from, keys[first + 1].Time.Flicks) + middle
                + IntegratePiece(last, keys[last].Time.Flicks, to);
        }

        private double IntegratePiece(int i, long from, long to)
        {
            if (from == to) return 0;
            double duration = Width(from, to);
            if (i < 0) return before * duration;
            if (i >= pieces.Length) return after * duration;

            long start = keys[i].Time.Flicks;
            double width = Width(start, keys[i + 1].Time.Flicks);
            return pieces[i].Integrate(Width(start, from) / width, Width(start, to) / width, width, duration);
        }

        // At duplicate times, the last key starts the outgoing piece.
        private int KeyAtOrBefore(long time)
        {
            int low = 0;
            int high = keys.Length;
            while (low < high)
            {
                int middle = (low + high) >>> 1;
                if (keys[middle].Time.Flicks <= time) low = middle + 1;
                else high = middle;
            }
            return low - 1;
        }
    }

    private readonly record struct Piece(double Rate, double RateChange, EasedIntegral? Eased = null)
    {
        public double Integrate(double from, double to, double width, double duration)
            => Eased is { } eased
                ? eased.Integrate(from, to) * width
                : (Rate + RateChange * ((from + to) / 2)) * duration;
    }

    private sealed record EasedIntegral(Func<double, double> Rate, double[] Ends, double[] Sums)
    {
        public double Integrate(double from, double to)
        {
            if (from == 0 && to == 1) return Sums[^1];
            int first = GaussKronrod.FindInterval(Ends, from);
            int last = GaussKronrod.FindInterval(Ends, to);
            if (first == last) return GaussKronrod.Integrate(Rate, from, to);

            double start = from == 0 ? 0 : GaussKronrod.Prefix(Rate, Ends, Sums, from);
            double end = to == 1 ? Sums[^1] : GaussKronrod.Prefix(Rate, Ends, Sums, to);
            return end - start;
        }
    }

    private readonly record struct KeySnapshot(IKeyFrame Frame, MediaTime Time, float Value, Easing Easing,
        (float X1, float Y1, float X2, float Y2) ControlPoints)
    {
        public static KeySnapshot Read(IKeyFrame frame)
        {
            Easing easing = frame.Easing.Value;
            return new KeySnapshot(frame, frame.KeyTime.Value, frame is KeyFrame<float> typed ? typed.Value.Value : float.NaN,
                easing, easing is SplineEasing spline ? (spline.X1, spline.Y1, spline.X2, spline.Y2) : default);
        }

        public bool Matches(KeySnapshot other)
            => ReferenceEquals(Frame, other.Frame) && Time == other.Time && Value.Equals(other.Value)
                && ReferenceEquals(Easing, other.Easing) && ControlPoints.Equals(other.ControlPoints);
    }
}
