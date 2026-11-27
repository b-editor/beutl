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
        private readonly SpeedIntegrator _integrator = new();
        private readonly EditorSpeedIntegral? _editorIntegral;
        private readonly float _minimum;
        private readonly float _maximum;
        // The expression that sets the speed, which only reading it at a given moment can tell.
        private readonly IExpressionProperty<float>? _expression;

        public bool IsSupported { get; }

        public bool IsMappingSupported => IsSupported || _expression != null;

        // A broad range can bound resize safely without giving an accurate point
        // integral for an offset write. Custom audio interpolation has only that range.
        public bool CanWriteOffsets => _editorIntegral?.HasCustomInterpolation != true;

        public SpeedMap(IProperty<float> speed, int sampleRate, bool videoPlayback = false)
        {
            var animation = speed.GetKeyFrameAnimation();
            _constant = animation is { KeyFrames.Count: 0 } ? 0 : speed.Value;
            _animation = animation is { KeyFrames.Count: > 0 } ? animation : null;
            _minimum = _constant;
            _maximum = _constant;
            bool known = _animation == null || _animation.TryGetOutputRange(out _minimum, out _maximum);
            // Video/controller playback integrates only nonempty keyframe curves, and an expression over no
            // keyframes multiplies time by its evaluated value instead. Audio evaluates any expression for each
            // window, even over keyframes, so nothing stored says how far it has played.
            _expression = speed is IExpressionProperty<float> { HasExpression: true } expression
                && !(videoPlayback && _animation != null) ? expression : null;
            IsSupported = known && float.IsFinite(_minimum) && float.IsFinite(_maximum) && _minimum >= 0 && _maximum >= _minimum
                && _expression == null;

            _integrator.EnsureCache(_animation);
            if (!videoPlayback && sampleRate > 60 && _animation != null && (!known || _minimum != _maximum))
                _editorIntegral = new EditorSpeedIntegral(_animation, sampleRate);
        }

        // This speed at the given time, after elapsed has played, with an expression at the value it gives then. Null when
        // that value cannot place a position.
        public SpeedMap? AtMoment(MediaTime time, MediaTime elapsed)
        {
            // Playback multiplies the elapsed time by the value, so with none elapsed it is not read.
            if (_expression == null || elapsed == MediaTime.Zero) return this;
            float percent = _expression.Evaluate(new EvaluationContext(time));
            return float.IsFinite(percent) && percent >= 0 ? new SpeedMap(Property.CreateValue(percent), 60) : null;
        }

        public Interval Map(Interval time, MediaTime ownerStart, bool extrapolate = false, bool conservative = false)
        {
            if (conservative && _animation == null)
            {
                double factor = _constant / 100d;
                return new Interval(Bound(time.Min.Flicks * factor, upper: false), Bound(time.Max.Flicks * factor, upper: true));
            }

            Interval a = At(time.Min, ownerStart, extrapolate, conservative);
            Interval b = At(time.Max, ownerStart, extrapolate, conservative);
            return conservative ? new Interval(a.Min, b.Max) : Interval.Between(a.Min, b.Max);
        }

        private Interval At(MediaTime localTime, MediaTime ownerStart, bool extrapolate, bool conservative)
        {
            if (_animation == null)
            {
                MediaTime fixedPosition = Scale(localTime, _constant);
                return new Interval(fixedPosition, fixedPosition);
            }

            MediaTime from = _animation.UseGlobalClock.Value ? ownerStart : MediaTime.Zero;
            MediaTime to = from + localTime;
            if (!extrapolate)
            {
                from = Max(from, MediaTime.Zero);
                to = Max(to, MediaTime.Zero);
            }
            if (from == to) return new Interval(MediaTime.Zero, MediaTime.Zero);

            MediaTime position = _integrator.IntegrateCached(from, to);
            if (!conservative || _editorIntegral == null) return new Interval(position, position);

            // Seeks use the continuous mapping. Consecutive audio windows advance by the DSP's sample sum,
            // so source bounds also need to cover its position and the error in estimating it.
            EditorSpeedIntegral.Estimate estimate = _editorIntegral.Integrate(from, to);
            MediaTime lower = Bound((estimate.Seconds - estimate.Error) * MediaTime.FlicksPerSecond, upper: false);
            MediaTime upper = Bound((estimate.Seconds + estimate.Error) * MediaTime.FlicksPerSecond, upper: true);
            if (IsSupported)
            {
                MediaTime duration = to - from;
                Interval bounds = Interval.Between(Scale(duration, _minimum), Scale(duration, _maximum));
                lower = Max(lower, Add(bounds.Min, -new MediaTime(1)));
                upper = Min(upper, Add(bounds.Max, new MediaTime(1)));
                if (duration > MediaTime.Zero) lower = Max(lower, MediaTime.Zero);
                else upper = Min(upper, MediaTime.Zero);
            }

            return new Interval(Min(position, lower), Max(position, upper));
        }

        private static MediaTime Bound(double flicks, bool upper)
            => new MediaTime((long)Math.Clamp(upper ? Math.Ceiling(flicks) : Math.Floor(flicks),
                long.MinValue + 1d, long.MaxValue - 1024d));

        private static MediaTime Scale(MediaTime time, float percent)
            => new MediaTime((long)Math.Clamp(Math.Round(time.Flicks * (percent / 100d), MidpointRounding.AwayFromZero),
                long.MinValue + 1d, long.MaxValue - 1024d));
    }
}
