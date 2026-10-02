using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

// An operation-local snapshot. Both the bounds and the eventual offset write must use
// the pre-edit clocks, even after another participating element has been resized.
internal sealed class MediaTimeMapping
{
    internal readonly record struct Interval(TimeSpan Min, TimeSpan Max)
    {
        public static Interval Between(TimeSpan a, TimeSpan b) => a <= b ? new(a, b) : new(b, a);

        public Interval Shift(TimeSpan delta) => new(Add(Min, delta), Add(Max, delta));
    }

    private readonly TimeRange _elementRange;
    private readonly Clock _sourceClock;
    private readonly SpeedMap _speed;
    private readonly Controller[] _controllers;
    private readonly TimeSpan? _videoDuration;
    private readonly bool _loopVideo;
    private readonly IAnimation<bool>? _loopAnimation;

    internal readonly record struct ControllerLink(DrawableTimeController Controller, Drawable Target);

    public MediaTimeMapping(Element element, EngineObject source, IProperty<float> speed,
        IReadOnlyList<ControllerLink> controllers, int sampleRate, TimeSpan? videoDuration = null,
        IReadOnlySet<Element>? timingPeers = null, bool ignoreLoops = false)
    {
        _elementRange = element.Range;
        _sourceClock = new Clock(element, source, timingPeers);
        _speed = new SpeedMap(speed, sampleRate);
        _controllers = controllers.Select(c => new Controller(element, c.Controller, c.Target, timingPeers, ignoreLoops)).ToArray();
        _videoDuration = videoDuration;
        _loopVideo = !ignoreLoops && source is SourceVideo video && video.IsLoop.CurrentValue;
        _loopAnimation = !ignoreLoops && source is SourceVideo animatedVideo ? animatedVideo.IsLoop.Animation : null;
    }

    public bool IsSupported => _speed.IsSupported && _controllers.All(c => c.IsSupported);

    public bool HasVariableDuration => _controllers.Any(c => c.HasVariableDuration);

    public (long Phase, long DurationOffset)? DurationLoopPhase(TimeSpan startDelta = default)
    {
        var geometry = new TimeRange(_elementRange.Start + startDelta, _elementRange.Duration);
        Interval time = new(geometry.Start, geometry.Start);
        foreach (Controller controller in _controllers)
        {
            if (controller.DurationLoopPhase(time, geometry, _elementRange.Duration) is { } phase) return phase;
            if (controller.HasVariableDuration) break;
            time = controller.Map(time, geometry);
        }
        return null;
    }

    public TimeSpan At(TimeSpan time, TimeSpan startDelta = default, TimeSpan lengthDelta = default, bool extrapolate = false)
        => Range(time, time, startDelta, lengthDelta, extrapolate, conservative: false).Min;

    public Interval Range(TimeSpan from, TimeSpan to, TimeSpan startDelta = default, TimeSpan lengthDelta = default,
        bool extrapolate = false, bool conservative = true)
    {
        var elementRange = new TimeRange(_elementRange.Start + startDelta, _elementRange.Duration + lengthDelta);
        Interval range = Interval.Between(from, to).Shift(elementRange.Start);
        foreach (Controller controller in _controllers)
            range = controller.Map(range, elementRange);

        TimeRange sourceRange = _sourceClock.GetRange(elementRange);
        Interval sourceTime = range;
        range = _speed.Map(range.Shift(-sourceRange.Start), sourceRange.Start, extrapolate, conservative);
        (bool loop, bool plain) = LoopModes(sourceTime, sourceRange.Start);
        Interval? wrapped = loop && _videoDuration is { } duration && duration > TimeSpan.Zero ? Loop(range, duration) : null;
        if (wrapped.HasValue && !plain) return wrapped.Value;
        if (!extrapolate && _videoDuration is { } original && range.Min < TimeSpan.Zero)
            range = range.Max < TimeSpan.Zero
                ? range.Shift(original)
                : Interval.Between(Min(range.Min + original, TimeSpan.Zero), Max(original, range.Max));
        return wrapped is { } looped
            ? new Interval(Min(range.Min, looped.Min), Max(range.Max, looped.Max))
            : range;
    }

    private (bool Loop, bool Plain) LoopModes(Interval time, TimeSpan ownerStart)
    {
        if (_loopAnimation == null) return (_loopVideo, !_loopVideo);
        if (!_loopAnimation.UseGlobalClock) time = time.Shift(-ownerStart);
        if (time.Min == time.Max)
        {
            bool value = _loopAnimation.Interpolate(time.Min);
            return (value, !value);
        }
        if (_loopAnimation is not KeyFrameAnimation<bool> keys) return (true, true);
        if (keys.KeyFrames.Count == 0) return (false, true);
        int modes = 0;
        KeyFrame<bool>? previous = null;
        foreach (IKeyFrame frame in keys.KeyFrames)
        {
            if (frame is not KeyFrame<bool> key) return (true, true);
            if (key.KeyTime < time.Min) previous = key;
            else
            {
                modes |= key.Value ? 1 : 2;
                if (key.KeyTime > time.Max) break;
            }
        }
        if (previous != null) modes |= previous.Value ? 1 : 2;
        return ((modes & 1) != 0, (modes & 2) != 0);
    }

    private sealed class Clock
    {
        private readonly TimeRange _range;
        private readonly TimeRange _elementRange;
        private readonly bool _followsElement;

        public bool ChangesWithElement => _followsElement;

        public Clock(Element element, EngineObject obj, IReadOnlySet<Element>? timingPeers = null)
        {
            _range = obj.TimeRange;
            _elementRange = element.Range;
            // Top-level objects always receive Element.Range. Descendants can opt out
            // using IsTimeAnchor; presenter references can belong to another element.
            IHierarchical? current = obj;
            while (current != null)
            {
                if (current is EngineObject engine)
                {
                    if (element.Objects.Contains(engine)
                        || timingPeers?.Any(peer => peer.Objects.Contains(engine)) == true)
                    {
                        _followsElement = true;
                        break;
                    }
                    if (engine.IsTimeAnchor) break;
                }
                if (current == element || current is Element peer && timingPeers?.Contains(peer) == true)
                {
                    _followsElement = true;
                    break;
                }
                current = current.HierarchicalParent;
            }
        }

        public TimeRange GetRange(TimeRange elementRange) => _followsElement
            ? new TimeRange(_range.Start + (elementRange.Start - _elementRange.Start),
                _range.Duration + (elementRange.Duration - _elementRange.Duration))
            : _range;
    }

    private sealed class Controller
    {
        private readonly Clock _clock;
        private readonly Clock? _targetClock;
        private readonly SpeedMap _speed;
        private readonly TimeSpan _offset;
        private readonly bool _adjust;
        private readonly bool _reverse;
        private readonly bool _loop;
        private readonly bool _holdFirst;
        private readonly bool _holdLast;
        private readonly float _frameRate;

        public bool IsSupported => _speed.IsSupported;

        public bool HasVariableDuration => _targetClock?.ChangesWithElement == true && (_loop || _reverse || _holdLast);

        public (long Phase, long DurationOffset)? DurationLoopPhase(Interval time, TimeRange elementRange, TimeSpan originalLength)
        {
            if (!_loop || _targetClock?.ChangesWithElement != true || !IsSupported) return null;
            TimeRange target = _targetClock.GetRange(elementRange);
            TimeRange owner = _clock.GetRange(elementRange);
            Interval raw = _speed.Map(time.Shift(_offset - (_adjust ? target.Start : owner.Start)), owner.Start);
            long phase = raw.Min.Ticks == long.MinValue ? long.MaxValue : Math.Abs(raw.Min.Ticks);
            return (phase, (target.Duration - originalLength).Ticks);
        }

        public Controller(Element element, DrawableTimeController controller, Drawable target, IReadOnlySet<Element>? timingPeers, bool ignoreLoops)
        {
            _clock = new Clock(element, controller, timingPeers);
            _targetClock = new Clock(element, target, timingPeers);
            _speed = new SpeedMap(controller.Speed, 60);
            _offset = controller.OffsetPosition.CurrentValue;
            _adjust = controller.AdjustTimeRange.CurrentValue;
            _reverse = controller.Reverse.CurrentValue;
            _loop = !ignoreLoops && controller.Loop.CurrentValue;
            _holdFirst = controller.HoldFirstFrame.CurrentValue;
            _holdLast = controller.HoldLastFrame.CurrentValue;
            _frameRate = controller.FrameRate.CurrentValue;
        }

        public Interval Map(Interval time, TimeRange elementRange)
        {
            TimeRange? targetRange = _targetClock?.GetRange(elementRange);
            if (targetRange is not { Duration: var duration } target || duration <= TimeSpan.Zero)
                return time;

            TimeRange owner = _clock.GetRange(elementRange);
            time = _speed.Map(time.Shift(_offset - (_adjust ? target.Start : owner.Start)), owner.Start);
            if (_reverse)
                time = new Interval(duration - time.Max, duration - time.Min);
            if (_loop)
            {
                time = Loop(time, duration);
            }
            if (_holdFirst)
                time = new Interval(Max(time.Min, TimeSpan.Zero), Max(time.Max, TimeSpan.Zero));
            if (_holdLast)
                time = new Interval(Min(time.Min, duration), Min(time.Max, duration));
            if (_frameRate > 0)
                time = new Interval(Quantize(time.Min), Quantize(time.Max));
            return time.Shift(target.Start);
        }

        private TimeSpan Quantize(TimeSpan time) => TimeSpan.FromSeconds(Math.Floor(time.TotalSeconds * _frameRate) / _frameRate);

    }

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

        public SpeedMap(IProperty<float> speed, int sampleRate)
        {
            _constant = speed.Animation is KeyFrameAnimation<float> { KeyFrames.Count: 0 } ? 0 : speed.CurrentValue;
            _animation = speed.Animation is KeyFrameAnimation<float> { KeyFrames.Count: 0 } ? null : speed.Animation;
            _minimum = _constant;
            _maximum = _constant;
            bool known = _animation == null || _animation.TryGetOutputRange(out _minimum, out _maximum);
            IsSupported = known && float.IsFinite(_minimum) && float.IsFinite(_maximum) && _minimum >= 0 && _maximum >= _minimum;
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

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static TimeSpan Add(TimeSpan a, TimeSpan b)
        => TimeSpan.FromTicks((long)Math.Clamp((decimal)a.Ticks + b.Ticks, long.MinValue + 1m, long.MaxValue));

    private static Interval Loop(Interval time, TimeSpan duration)
    {
        long period = duration.Ticks;
        long minCycle = (long)Math.Floor(time.Min.Ticks / (double)period);
        long maxCycle = (long)Math.Floor(time.Max.Ticks / (double)period);
        // A wrap inside the interval reaches both ends, even if its two endpoints
        // happen to request the very same frame.
        return minCycle == maxCycle
            ? new Interval(Modulo(time.Min), Modulo(time.Max))
            : new Interval(TimeSpan.Zero, duration);

        TimeSpan Modulo(TimeSpan value)
        {
            long remainder = value.Ticks % period;
            return TimeSpan.FromTicks(remainder < 0 ? remainder + period : remainder);
        }
    }
}
