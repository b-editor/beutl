using Beutl.Animation;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Editor.Services;

internal sealed partial class MediaTimeMapping
{
    private sealed class SpeedMap
    {
        private readonly float _constant;
        private readonly KeyFrameAnimation<float>? _animation;
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
            _constant = speed.GetKeyFrameAnimation() is { KeyFrames.Count: 0 } ? 0 : speed.Value;
            _animation = speed.GetKeyFrameAnimation() is { KeyFrames.Count: 0 } ? null : speed.GetKeyFrameAnimation();
            _minimum = _constant;
            _maximum = _constant;
            bool known = _animation == null || _animation.TryGetOutputRange(out _minimum, out _maximum);
            IsSupported = known && float.IsFinite(_minimum) && float.IsFinite(_maximum) && _minimum >= 0 && _maximum >= _minimum;
            if (videoPlayback)
            {
                // Video/controller playback integrates only nonempty keyframe curves.
                // An expression over no keyframes multiplies time by its evaluated value instead.
                IsSupported &= speed is not IExpressionProperty { HasExpression: true } || _animation is { KeyFrames.Count: > 0 };
            }
            _integrator = new SpeedIntegrator(sampleRate);
            if (sampleRate > 60 && _animation != null)
            {
                if (_animation is KeyFrameAnimation<float> keys && keys.KeyFrames.Count > 0
                    && keys.KeyFrames[0] is KeyFrame<float> first
                    && keys.KeyFrames.All(k => k is KeyFrame<float> value && value.Value.Value == first.Value.Value))
                    _constantAnimation = first.Value.Value;
                else
                    _editorIntegral = new EditorSpeedIntegral(_animation, sampleRate);
            }
            // These caches live for one operation and never subscribe to the model.
            _integrator.EnsureCache(null);
        }

        public Interval Map(Interval time, MediaTime ownerStart, bool extrapolate = false, bool conservative = false)
        {
            if (conservative && _animation == null)
            {
                MediaTime Bound(MediaTime value, bool upper)
                {
                    double flicks = value.Flicks * (_constant / 100d);
                    return new MediaTime((long)Math.Clamp(upper ? Math.Ceiling(flicks) : Math.Floor(flicks),
                        long.MinValue + 1d, long.MaxValue - 1024d));
                }
                return new Interval(Bound(time.Min, false), Bound(time.Max, true));
            }
            MediaTime a = At(time.Min, ownerStart, extrapolate, out MediaTime errorA);
            MediaTime b = At(time.Max, ownerStart, extrapolate, out MediaTime errorB);
            if (!conservative) return Interval.Between(a, b);
            MediaTime lower = Add(a, -errorA);
            MediaTime upper = Add(b, errorB);
            if (_editorIntegral != null && IsSupported)
            {
                Interval startBounds = DeclaredBounds(time.Min, ownerStart, extrapolate);
                Interval endBounds = DeclaredBounds(time.Max, ownerStart, extrapolate);
                lower = Max(lower, startBounds.Min);
                upper = Min(upper, endBounds.Max);
            }
            return new Interval(lower, upper);
        }

        private Interval DeclaredBounds(MediaTime localTime, MediaTime ownerStart, bool extrapolate)
        {
            MediaTime from = _animation!.UseGlobalClock.Value ? ownerStart : MediaTime.Zero;
            MediaTime to = from + localTime;
            if (!extrapolate) { from = Max(from, MediaTime.Zero); to = Max(to, MediaTime.Zero); }
            MediaTime duration = to - from;
            if (duration == MediaTime.Zero) return new Interval(MediaTime.Zero, MediaTime.Zero);
            Interval bounds = Interval.Between(Scale(duration, _minimum), Scale(duration, _maximum));
            MediaTime lower = Add(bounds.Min, -new MediaTime(1));
            MediaTime upper = Add(bounds.Max, new MediaTime(1));
            if (duration > MediaTime.Zero) lower = Max(lower, MediaTime.Zero);
            else upper = Min(upper, MediaTime.Zero);
            return new Interval(lower, upper);
        }

        private MediaTime At(MediaTime localTime, MediaTime ownerStart, bool extrapolate, out MediaTime error)
        {
            error = MediaTime.Zero;
            if (_animation == null) return Scale(localTime, _constant);
            if (localTime == MediaTime.Zero) return MediaTime.Zero;
            if (!_animation.UseGlobalClock.Value) return Integrate(localTime, extrapolate, out error);
            if (_editorIntegral != null && _animation is KeyFrameAnimation<float> { KeyFrames.Count: > 0 } keys)
            {
                MediaTime from = extrapolate ? ownerStart : Max(ownerStart, MediaTime.Zero);
                MediaTime to = extrapolate ? localTime + ownerStart : Max(localTime + ownerStart, MediaTime.Zero);
                MediaTime first = Min(MediaTime.Zero, keys.KeyFrames[0].KeyTime.Value);
                MediaTime last = Max(MediaTime.Zero, keys.KeyFrames[^1].KeyTime.Value);
                if (from >= first && from <= last && to >= first && to <= last)
                {
                    EditorSpeedIntegral.Estimate estimate = _editorIntegral.Integrate(from, to);
                    error = new MediaTime((long)Math.Ceiling(estimate.Error * MediaTime.FlicksPerSecond));
                    return MediaTime.FromSeconds(estimate.Seconds);
                }
            }
            MediaTime end = Integrate(localTime + ownerStart, extrapolate, out MediaTime endError);
            MediaTime start = Integrate(ownerStart, extrapolate, out MediaTime startError);
            error = Add(endError, startError);
            return end - start;
        }

        private MediaTime Integrate(MediaTime time, bool extrapolate, out MediaTime error)
        {
            error = MediaTime.Zero;
            if (!extrapolate && time < MediaTime.Zero) return MediaTime.Zero;
            if (_constantAnimation is { } constant) return Scale(time, constant);
            // Keyframe animations hold their endpoint values. Avoid sampling hours of
            // constant tail when finding an inverse, especially at audio sample rates.
            if (_animation is KeyFrameAnimation<float> { KeyFrames.Count: > 0 } keys)
            {
                MediaTime last = Max(MediaTime.Zero, keys.KeyFrames[^1].KeyTime.Value);
                if (time > last)
                    return Add(Integrate(last, extrapolate, out error), Scale(time - last, keys.Interpolate(last)));
                MediaTime first = Min(MediaTime.Zero, keys.KeyFrames[0].KeyTime.Value);
                if (time < first)
                    return Add(Integrate(first, extrapolate, out error), Scale(time - first, keys.Interpolate(first)));
            }
            if (_editorIntegral != null)
            {
                EditorSpeedIntegral.Estimate estimate = _editorIntegral.Integrate(time);
                error = new MediaTime((long)Math.Ceiling(estimate.Error * MediaTime.FlicksPerSecond));
                return MediaTime.FromSeconds(estimate.Seconds);
            }
            if (time < MediaTime.Zero) return IntegrateNegative(time);
            return _integrator.Integrate(time, _animation!);
        }

        private MediaTime IntegrateNegative(MediaTime time)
        {
            // Trimming earlier needs signed pre-roll, including curves whose keys
            // already lie before zero after a split. Playback's integrator only
            // samples nonnegative times, so accumulate this side on the same grid.
            int rate = _integrator.SampleRate;
            int seconds = (int)Math.Floor(-time.ToSeconds());
            while (_negativeSeconds.Count <= seconds)
            {
                int second = _negativeSeconds.Count - 1;
                double value = _negativeSeconds[^1];
                for (int i = 1; i <= rate; i++)
                    value -= _animation!.Interpolate(MediaTime.FromSeconds(-second - i / (double)rate)) / (100d * rate);
                _negativeSeconds.Add(value);
            }
            double samples = -time.ToSeconds() * rate;
            long wholeSamples = (long)samples;
            double sum = _negativeSeconds[seconds];
            for (long i = (long)seconds * rate + 1; i <= wholeSamples; i++)
                sum -= _animation!.Interpolate(MediaTime.FromSeconds(-i / (double)rate)) / (100d * rate);
            sum -= _animation!.Interpolate(time) * (samples - wholeSamples) / (100d * rate);
            return MediaTime.FromSeconds(sum);
        }

        private static MediaTime Scale(MediaTime time, float percent)
            => new MediaTime((long)Math.Clamp(Math.Round(time.Flicks * (percent / 100d), MidpointRounding.AwayFromZero),
                long.MinValue + 1d, long.MaxValue - 1024d));
    }
}
