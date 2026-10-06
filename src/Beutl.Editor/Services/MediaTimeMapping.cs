using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

// An operation-local snapshot. Both the bounds and the eventual offset write must use
// the pre-edit clocks, even after another participating element has been resized.
internal sealed partial class MediaTimeMapping
{
    internal enum TrimRole { Front, Middle, Back }

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
    private readonly bool _sourceSupported;
    private readonly bool _sourceLocked;
    private readonly bool _opaquePortal;
    private readonly bool _clipToSourceRange;

    internal readonly record struct ControllerLink(DrawableTimeController Controller, Drawable Target);

    public MediaTimeMapping(Element element, EngineObject source, IProperty<float> speed,
        IReadOnlyList<ControllerLink> controllers, int sampleRate, TimeSpan? videoDuration = null,
        IReadOnlySet<Element>? timingPeers = null, bool ignoreLoops = false,
        IReadOnlyDictionary<Element, TrimRole>? timingRoles = null, bool portalInput = false)
    {
        _elementRange = element.Range;
        _sourceClock = new Clock(element, source, timingPeers, timingRoles);
        _speed = new SpeedMap(speed, sampleRate, videoPlayback: source is SourceVideo);
        _controllers = controllers.Select(c => new Controller(element, c.Controller, c.Target, timingPeers, ignoreLoops, timingRoles)).ToArray();
        _videoDuration = videoDuration;
        _loopVideo = !ignoreLoops && source is SourceVideo video && video.IsLoop.CurrentValue;
        _loopAnimation = !ignoreLoops && source is SourceVideo animatedVideo ? animatedVideo.IsLoop.Animation : null;
        _opaquePortal = source is PortalObject;
        _clipToSourceRange = portalInput || source is Sound;
        // Referenced/portal media can belong to a locked element even when the
        // consumer is editable. Its clock still supplies bounds for ordinary resize.
        Element? owner = source.FindHierarchicalParent<Element>();
        _sourceLocked = owner != null && (owner.IsLocked
            || owner.HierarchicalParent is Scene scene && scene.IsElementLocked(owner));
        // A stored source or property value does not describe an evaluated source
        // switch/expression. Keep these edits atomic until their mapping is bounded.
        _sourceSupported = source switch
        {
            SourceVideo videoSource => videoSource.Source.Animation == null && !videoSource.Source.HasExpression
                && !videoSource.IsLoop.HasExpression && !videoSource.OffsetPosition.HasExpression,
            SourceSound sound => !sound.Source.HasExpression,
            SceneSound sound => !sound.ReferencedScene.HasExpression,
            DrawableTimeController controller => !controller.Target.HasExpression,
            IPresenter<Drawable> presenter => !presenter.Target.HasExpression,
            PortalObject => false,
            _ => true
        };
    }

    public bool IsSupported => _sourceSupported && _speed.IsSupported && _controllers.All(c => c.IsSupported);

    public bool CanWriteOffsets => !_sourceLocked && IsSupported && _speed.CanWriteOffsets;

    public bool HasVariableDuration => _controllers.Any(c => c.HasVariableDuration);

    // Unknown portal inputs may belong to another resized participant. Keep the
    // group together when that unresolved constraint refuses a geometry change.
    public bool HasSharedClock => _opaquePortal || _sourceClock.UsesPeerClock || _controllers.Any(c => c.HasSharedClock);

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

    public Interval Range(TimeSpan from, TimeSpan to, TimeSpan startDelta = default, TimeSpan lengthDelta = default,
        bool extrapolate = false, bool conservative = true, bool sourceOffset = false)
    {
        var elementRange = new TimeRange(_elementRange.Start + startDelta, _elementRange.Duration + lengthDelta);
        Interval range = Interval.Between(from, to).Shift(elementRange.Start);
        return Map(range, elementRange, extrapolate, conservative, sourceOffset);
    }

    public Interval? SampledRange(TimeSpan from, TimeSpan to, TimeSpan startDelta = default, TimeSpan lengthDelta = default,
        bool conservative = true)
    {
        var elementRange = new TimeRange(_elementRange.Start + startDelta, _elementRange.Duration + lengthDelta);
        Interval range = Interval.Between(from, to).Shift(elementRange.Start);
        if (_clipToSourceRange)
        {
            // Audio is evaluated only within its own range; portal input is supplied
            // only while its provider is active, before any controller remaps time.
            TimeRange visible = _sourceClock.GetRange(elementRange);
            if (range.Max <= visible.Start || range.Min >= visible.End) return null;
            range = new Interval(Max(range.Min, visible.Start), Min(range.Max, visible.End));
        }
        return Map(range, elementRange, extrapolate: false, conservative, sourceOffset: false);
    }

    private Interval Map(Interval range, TimeRange elementRange, bool extrapolate, bool conservative, bool sourceOffset)
    {
        foreach (Controller controller in _controllers)
            range = controller.Map(range, elementRange);

        TimeRange sourceRange = _sourceClock.GetRange(elementRange);
        Interval sourceTime = range;
        range = _speed.Map(range.Shift(-sourceRange.Start), sourceRange.Start, extrapolate, conservative);
        // SourceVideo applies its wrap before OffsetPosition. Offset writes use
        // the speed integral, while bounds use the wrapped playback window.
        if (sourceOffset) return range;
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
        int modes = (_loopAnimation.Interpolate(time.Min) ? 1 : 2) | (_loopAnimation.Interpolate(time.Max) ? 1 : 2);
        KeyFrame<bool>? previous = null;
        foreach (IKeyFrame frame in keys.KeyFrames)
        {
            if (frame is not KeyFrame<bool> key) return (true, true);
            if (key.KeyTime < time.Min) previous = key;
            else
            {
                // Linear/hold boolean interpolation keeps the preceding value until
                // the next key. Other easings can reach the next value early.
                if (key.KeyTime > time.Max && key.Easing is LinearEasing or HoldEasing) break;
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
        private readonly TrimRole? _elementRole;
        private readonly TrimRole? _ownerRole;

        public bool ChangesWithElement => _followsElement;

        public bool UsesPeerClock { get; }

        public Clock(Element element, EngineObject obj, IReadOnlySet<Element>? timingPeers = null,
            IReadOnlyDictionary<Element, TrimRole>? timingRoles = null)
        {
            _range = obj.TimeRange;
            _elementRange = element.Range;
            // Top-level objects always receive Element.Range. Descendants can opt out
            // using IsTimeAnchor; presenter references can belong to another element.
            IHierarchical? current = obj;
            Element? clockOwner = null;
            while (current != null)
            {
                if (current is EngineObject engine)
                {
                    bool ownedHere = element.Objects.Contains(engine);
                    Element? peerOwner = ownedHere ? null : timingPeers?.FirstOrDefault(peer => peer.Objects.Contains(engine));
                    if (ownedHere || peerOwner != null)
                    {
                        _followsElement = true;
                        UsesPeerClock = !ownedHere;
                        clockOwner = ownedHere ? element : peerOwner;
                        break;
                    }
                    if (engine.IsTimeAnchor) break;
                }
                if (current == element || current is Element peer && timingPeers?.Contains(peer) == true)
                {
                    _followsElement = true;
                    UsesPeerClock = current != element;
                    clockOwner = (Element)current;
                    break;
                }
                current = current.HierarchicalParent;
            }
            if (timingRoles?.TryGetValue(element, out TrimRole elementRole) == true) _elementRole = elementRole;
            if (clockOwner != null && timingRoles?.TryGetValue(clockOwner, out TrimRole ownerRole) == true) _ownerRole = ownerRole;
        }

        public TimeRange GetRange(TimeRange elementRange)
        {
            if (!_followsElement) return _range;
            TimeSpan startDelta = elementRange.Start - _elementRange.Start;
            TimeSpan lengthDelta = elementRange.Duration - _elementRange.Duration;
            if (UsesPeerClock && _elementRole is { } role && _ownerRole is { } ownerRole)
            {
                // Fronts grow, middles move, and backs move while shrinking. A
                // reference to another role cannot reuse this element's geometry.
                TimeSpan delta = role == TrimRole.Front ? lengthDelta : startDelta;
                startDelta = ownerRole == TrimRole.Front ? TimeSpan.Zero : delta;
                lengthDelta = ownerRole switch
                {
                    TrimRole.Front => delta,
                    TrimRole.Back => -delta,
                    _ => TimeSpan.Zero
                };
            }
            return new TimeRange(_range.Start + startDelta, _range.Duration + lengthDelta);
        }
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
        private readonly bool _hasMappingExpression;

        public bool IsSupported => !_hasMappingExpression && _speed.IsSupported;

        public bool HasVariableDuration => _targetClock?.ChangesWithElement == true && (_loop || _reverse || _holdLast);

        public bool HasSharedClock => _clock.UsesPeerClock || _targetClock?.UsesPeerClock == true;

        public (long Phase, long DurationOffset)? DurationLoopPhase(Interval time, TimeRange elementRange, TimeSpan originalLength)
        {
            if (!_loop || _targetClock?.ChangesWithElement != true || !IsSupported) return null;
            TimeRange target = _targetClock.GetRange(elementRange);
            TimeRange owner = _clock.GetRange(elementRange);
            Interval raw = _speed.Map(time.Shift(_offset - (_adjust ? target.Start : owner.Start)), owner.Start);
            long phase = raw.Min.Ticks == long.MinValue ? long.MaxValue : Math.Abs(raw.Min.Ticks);
            return (phase, (target.Duration - originalLength).Ticks);
        }

        public Controller(Element element, DrawableTimeController controller, Drawable target, IReadOnlySet<Element>? timingPeers, bool ignoreLoops,
            IReadOnlyDictionary<Element, TrimRole>? timingRoles)
        {
            _clock = new Clock(element, controller, timingPeers, timingRoles);
            _targetClock = new Clock(element, target, timingPeers, timingRoles);
            _speed = new SpeedMap(controller.Speed, 60, videoPlayback: true);
            _offset = controller.OffsetPosition.CurrentValue;
            _adjust = controller.AdjustTimeRange.CurrentValue;
            _reverse = controller.Reverse.CurrentValue;
            _loop = !ignoreLoops && controller.Loop.CurrentValue;
            _holdFirst = controller.HoldFirstFrame.CurrentValue;
            _holdLast = controller.HoldLastFrame.CurrentValue;
            _frameRate = controller.FrameRate.CurrentValue;
            _hasMappingExpression = controller.OffsetPosition.HasExpression || controller.AdjustTimeRange.HasExpression
                || controller.Reverse.HasExpression || controller.Loop.HasExpression || controller.HoldFirstFrame.HasExpression
                || controller.HoldLastFrame.HasExpression || controller.FrameRate.HasExpression;
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
