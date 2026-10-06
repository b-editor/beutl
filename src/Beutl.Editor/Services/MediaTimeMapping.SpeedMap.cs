using Beutl.Animation;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Editor.Services;

internal sealed partial class MediaTimeMapping
{
    private sealed class SpeedMap
    {
        private readonly float _constant;
        private readonly IAnimation<float>? _animation;
        private readonly SpeedIntegrator _integrator;
        private readonly EditorSpeedIntegral? _editorIntegral;
        private readonly float? _constantAnimation;
        private readonly List<double> _negativeSeconds = [0];
        private readonly float _minimum;
        private readonly float _maximum;

        public bool IsSupported { get; }

        // A broad range can bound resize safely without giving an accurate point
        // integral for an offset write. Custom audio interpolation has only that range.
        public bool CanWriteOffsets => _editorIntegral?.HasCustomInterpolation != true;

        public SpeedMap(IProperty<float> speed, int sampleRate, bool videoPlayback = false)
        {
            _constant = speed.Animation is KeyFrameAnimation<float> { KeyFrames.Count: 0 } ? 0 : speed.CurrentValue;
            _animation = speed.Animation is KeyFrameAnimation<float> { KeyFrames.Count: 0 } ? null : speed.Animation;
            _minimum = _constant;
            _maximum = _constant;
            bool known = _animation == null || _animation.TryGetOutputRange(out _minimum, out _maximum);
            IsSupported = known && float.IsFinite(_minimum) && float.IsFinite(_maximum) && _minimum >= 0 && _maximum >= _minimum;
            if (videoPlayback)
            {
                // Video/controller playback integrates only nonempty keyframe curves.
                // Other animations multiply time by their evaluated value instead.
                IsSupported &= (_animation == null || _animation is KeyFrameAnimation<float>)
                    && (!speed.HasExpression || _animation is KeyFrameAnimation<float> { KeyFrames.Count: > 0 });
            }
            _integrator = new SpeedIntegrator(sampleRate);
            if (sampleRate > 60 && _animation != null)
            {
                if (_animation is KeyFrameAnimation<float> keys && keys.KeyFrames.Count > 0
                    && keys.KeyFrames[0] is KeyFrame<float> first
                    && keys.KeyFrames.All(k => k is KeyFrame<float> value && value.Value == first.Value))
                    _constantAnimation = first.Value;
                else
                    _editorIntegral = new EditorSpeedIntegral(_animation, sampleRate);
            }
            // These caches live for one operation and never subscribe to the model.
            _integrator.EnsureCache(null);
        }

        public Interval Map(Interval time, TimeSpan ownerStart, bool extrapolate = false, bool conservative = false)
        {
            if (conservative && _animation == null)
            {
                TimeSpan Bound(TimeSpan value, bool upper)
                {
                    double ticks = value.Ticks * (_constant / 100d);
                    return TimeSpan.FromTicks((long)Math.Clamp(upper ? Math.Ceiling(ticks) : Math.Floor(ticks),
                        long.MinValue + 1d, long.MaxValue - 1024d));
                }
                return new Interval(Bound(time.Min, false), Bound(time.Max, true));
            }
            TimeSpan a = At(time.Min, ownerStart, extrapolate, out TimeSpan errorA);
            TimeSpan b = At(time.Max, ownerStart, extrapolate, out TimeSpan errorB);
            if (!conservative) return Interval.Between(a, b);
            TimeSpan lower = Add(a, -errorA);
            TimeSpan upper = Add(b, errorB);
            if (_editorIntegral != null && IsSupported)
            {
                Interval startBounds = DeclaredBounds(time.Min, ownerStart, extrapolate);
                Interval endBounds = DeclaredBounds(time.Max, ownerStart, extrapolate);
                lower = Max(lower, startBounds.Min);
                upper = Min(upper, endBounds.Max);
            }
            return new Interval(lower, upper);
        }

        private Interval DeclaredBounds(TimeSpan localTime, TimeSpan ownerStart, bool extrapolate)
        {
            TimeSpan from = _animation!.UseGlobalClock ? ownerStart : TimeSpan.Zero;
            TimeSpan to = from + localTime;
            if (!extrapolate) { from = Max(from, TimeSpan.Zero); to = Max(to, TimeSpan.Zero); }
            TimeSpan duration = to - from;
            if (duration == TimeSpan.Zero) return new Interval(TimeSpan.Zero, TimeSpan.Zero);
            Interval bounds = Interval.Between(Scale(duration, _minimum), Scale(duration, _maximum));
            TimeSpan lower = Add(bounds.Min, -TimeSpan.FromTicks(1));
            TimeSpan upper = Add(bounds.Max, TimeSpan.FromTicks(1));
            if (duration > TimeSpan.Zero) lower = Max(lower, TimeSpan.Zero);
            else upper = Min(upper, TimeSpan.Zero);
            return new Interval(lower, upper);
        }

        private TimeSpan At(TimeSpan localTime, TimeSpan ownerStart, bool extrapolate, out TimeSpan error)
        {
            error = TimeSpan.Zero;
            if (_animation == null) return Scale(localTime, _constant);
            if (localTime == TimeSpan.Zero) return TimeSpan.Zero;
            if (!_animation.UseGlobalClock) return Integrate(localTime, extrapolate, out error);
            if (_editorIntegral != null && _animation is KeyFrameAnimation<float> { KeyFrames.Count: > 0 } keys)
            {
                TimeSpan from = extrapolate ? ownerStart : Max(ownerStart, TimeSpan.Zero);
                TimeSpan to = extrapolate ? localTime + ownerStart : Max(localTime + ownerStart, TimeSpan.Zero);
                TimeSpan first = Min(TimeSpan.Zero, keys.KeyFrames[0].KeyTime);
                TimeSpan last = Max(TimeSpan.Zero, keys.KeyFrames[^1].KeyTime);
                if (from >= first && from <= last && to >= first && to <= last)
                {
                    EditorSpeedIntegral.Estimate estimate = _editorIntegral.Integrate(from, to);
                    error = TimeSpan.FromTicks((long)Math.Ceiling(estimate.Error * TimeSpan.TicksPerSecond));
                    return TimeSpan.FromSeconds(estimate.Seconds);
                }
            }
            TimeSpan end = Integrate(localTime + ownerStart, extrapolate, out TimeSpan endError);
            TimeSpan start = Integrate(ownerStart, extrapolate, out TimeSpan startError);
            error = Add(endError, startError);
            return end - start;
        }

        private TimeSpan Integrate(TimeSpan time, bool extrapolate, out TimeSpan error)
        {
            error = TimeSpan.Zero;
            if (!extrapolate && time < TimeSpan.Zero) return TimeSpan.Zero;
            if (_constantAnimation is { } constant) return Scale(time, constant);
            // Keyframe animations hold their endpoint values. Avoid sampling hours of
            // constant tail when finding an inverse, especially at audio sample rates.
            if (_animation is KeyFrameAnimation<float> { KeyFrames.Count: > 0 } keys)
            {
                TimeSpan last = Max(TimeSpan.Zero, keys.KeyFrames[^1].KeyTime);
                if (time > last)
                    return Add(Integrate(last, extrapolate, out error), Scale(time - last, keys.Interpolate(last)));
                TimeSpan first = Min(TimeSpan.Zero, keys.KeyFrames[0].KeyTime);
                if (time < first)
                    return Add(Integrate(first, extrapolate, out error), Scale(time - first, keys.Interpolate(first)));
            }
            if (_editorIntegral != null)
            {
                EditorSpeedIntegral.Estimate estimate = _editorIntegral.Integrate(time);
                error = TimeSpan.FromTicks((long)Math.Ceiling(estimate.Error * TimeSpan.TicksPerSecond));
                return TimeSpan.FromSeconds(estimate.Seconds);
            }
            if (time < TimeSpan.Zero) return IntegrateNegative(time);
            return _integrator.Integrate(time, _animation!);
        }

        private TimeSpan IntegrateNegative(TimeSpan time)
        {
            // Trimming earlier needs signed pre-roll, including curves whose keys
            // already lie before zero after a split. Playback's integrator only
            // samples nonnegative times, so accumulate this side on the same grid.
            int rate = _integrator.SampleRate;
            int seconds = (int)Math.Floor(-time.TotalSeconds);
            while (_negativeSeconds.Count <= seconds)
            {
                int second = _negativeSeconds.Count - 1;
                double value = _negativeSeconds[^1];
                for (int i = 1; i <= rate; i++)
                    value -= _animation!.Interpolate(TimeSpan.FromSeconds(-second - i / (double)rate)) / (100d * rate);
                _negativeSeconds.Add(value);
            }
            double samples = -time.TotalSeconds * rate;
            long wholeSamples = (long)samples;
            double sum = _negativeSeconds[seconds];
            for (long i = (long)seconds * rate + 1; i <= wholeSamples; i++)
                sum -= _animation!.Interpolate(TimeSpan.FromSeconds(-i / (double)rate)) / (100d * rate);
            sum -= _animation!.Interpolate(time) * (samples - wholeSamples) / (100d * rate);
            return TimeSpan.FromSeconds(sum);
        }

        private static TimeSpan Scale(TimeSpan time, float percent)
            => TimeSpan.FromTicks((long)Math.Clamp(Math.Round(time.Ticks * (percent / 100d), MidpointRounding.AwayFromZero),
                long.MinValue + 1d, long.MaxValue - 1024d));
    }
}
