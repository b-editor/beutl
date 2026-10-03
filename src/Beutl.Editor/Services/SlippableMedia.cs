using Beutl.Animation;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

// Shared media-offset primitives for the trim services. Slip shifts the media window
// of a single element; Roll/Slide additionally shift the trimmed neighbour's in-point so
// its content stays anchored across the moving cut. Both need the same source-backed
// media enumeration (including nested Drawable/Sound containers) and the same "one delta
// across every stream" clamping, so it lives here rather than being duplicated per service.
internal static class SlippableMedia
{
    // A single source-backed media stream whose OffsetPosition can be slipped.
    // Total is the absolute source duration (null when the stream has no bounded source).
    internal sealed class Target
    {
        public Target(IProperty<TimeSpan> offset, TimeSpan? total, IProperty<float> speed, bool isVideo = false)
        {
            Offset = offset;
            Total = total;
            IsVideo = isVideo;
            SupportsTrimming = TryGetConstantSpeed(speed, out double factor, evaluatesExpressions: isVideo);
            Speed = factor;
            IsFrozen = SupportsTrimming && factor <= 0;
        }

        public IProperty<TimeSpan> Offset { get; }

        public TimeSpan? Total { get; }

        public bool IsVideo { get; }

        // Source-time units consumed per timeline-time unit.
        public double Speed { get; }

        // Input-time remapping cannot advance a proven constant-zero source. Keep this
        // fact even if a controller later marks its general mapping unsupported.
        public bool IsFrozen { get; }

        public bool SupportsTrimming { get; set; }

        // Source-time ticks sampled through every reference path. Keep fractional ticks
        // until clamping so slow media cannot round past its source boundary.
        public double VisibleSourceEndTicks { get; set; }

        public TimeSpan OutPointHeadroom { get; set; } = TimeSpan.MaxValue;

        public TimeSpan Current
        {
            get => Offset.CurrentValue;
            set => Offset.CurrentValue = value;
        }
    }

    // Disabled (IsEnabled == false) media is deliberately included, unlike playback's
    // Element.CollectObjects: trim edits apply one shared delta to every stream so linked
    // media (e.g. a temporarily muted audio track) stays in sync with the visible content
    // and re-enabling it does not reveal a desynced or out-of-range offset. The same
    // reasoning keeps disabled streams in the clamp bounds — a delta that would push a
    // disabled stream outside its source is refused, not applied desynced.
    public static List<Target> Collect(Element element)
    {
        var targets = new Dictionary<EngineObject, Target>();
        var path = new HashSet<EngineObject>();
        var incoming = new HashSet<Target>();
        foreach (EngineObject obj in element.Objects)
        {
            // Only top-level disabled objects are filtered by Element.CollectObjects.
            // A presenter can still render a disabled controller through a separate path.
            incoming.UnionWith(CollectFrom(obj, element, incoming, targets, path, obj.IsEnabled, element.Range));
        }

        // Frozen media consumes no source time, even under an unsupported controller.
        return targets.Values.Where(static target => !target.IsFrozen).ToList();
    }

    // Cache media independently from traversal: aliases share one offset write, but every
    // active reference path must be checked. The path set stops cycles without suppressing
    // a later active visit after an earlier disabled top-level occurrence.
    private static HashSet<Target> CollectFrom(
        EngineObject obj, Element element, IReadOnlyCollection<Target> incoming,
        Dictionary<EngineObject, Target> targets, HashSet<EngineObject> path, bool applyMappings,
        TimeRange window)
    {
        var result = new HashSet<Target>();
        if (!path.Add(obj)) return result;

        try
        {
            switch (obj)
            {
                case SourceVideo or SourceSound or SceneSound:
                    if (!targets.TryGetValue(obj, out Target? target))
                    {
                        target = obj switch
                        {
                            SourceVideo video => CreateVideoTarget(video),
                            SourceSound sound => CreateSoundTarget(sound),
                            SceneSound sound => CreateSceneSoundTarget(sound),
                            _ => throw new InvalidOperationException()
                        };
                        targets.Add(obj, target);
                    }
                    IncludeWindow(target, obj, element, window);
                    result.Add(target);
                    break;
                case SoundGroup soundGroup:
                    foreach (Sound child in soundGroup.Children)
                        result.UnionWith(CollectFrom(child, element, [], targets, path, applyMappings, window));
                    break;
                case DrawableGroup drawableGroup:
                    // Containers consume incoming drawable flow before reconciling their
                    // property children. Those children are not added to each other's flow.
                    foreach (Drawable child in drawableGroup.Children)
                        result.UnionWith(CollectFrom(child, element, [], targets, path, applyMappings, window));
                    break;
                case DrawableDecorator decorator:
                    foreach (Drawable child in decorator.Children)
                        result.UnionWith(CollectFrom(child, element, [], targets, path, applyMappings, window));
                    break;
                case DrawableTimeController controller:
                    if (controller.Target.CurrentValue is { } controlled)
                    {
                        // Identity mappings translate the incoming clock by the target's
                        // start minus the controller's start, including nested anchors.
                        // Playback leaves the clock unchanged for an empty target range.
                        TimeRange targetWindow = controlled.Duration > TimeSpan.Zero
                            ? window.AddStart(controlled.Start - controller.Start) : window;
                        // Disabled streams retain the window needed when re-enabled.
                        result.UnionWith(CollectFrom(controlled, element, [], targets, path, applyMappings, targetWindow));
                    }
                    if (!HasIdentityTimeMapping(controller))
                    {
                        if (applyMappings) RejectVideoMappings(incoming);
                        // Explicit targets are included even while disabled, so their
                        // unsupported mapping must also be safe when re-enabled.
                        RejectVideoMappings(result);
                    }
                    break;
                case IPresenter<Drawable> presenter:
                    if (presenter.Target.CurrentValue is { } presented)
                        result.UnionWith(CollectFrom(presented, element, incoming, targets, path, applyMappings, window));
                    break;
            }

            return result;
        }
        finally
        {
            path.Remove(obj);
        }
    }

    private static void RejectVideoMappings(IEnumerable<Target> targets)
    {
        foreach (Target target in targets)
        {
            if (target.IsVideo)
                target.SupportsTrimming = false;
        }
    }

    private static bool TryGetConstantSpeed(
        IProperty<float> speed, out double factor, bool evaluatesExpressions = false)
    {
        factor = speed.CurrentValue / 100.0;
        // Video/controller resources evaluate expressions; audio SpeedNode reads the
        // base value or animation directly. Do not mistake an expression for a constant.
        if (evaluatesExpressions && speed.HasExpression) return false;
        if (speed.Animation == null) return true;

        // Flat keyframes have a constant source-time mapping, but their value can differ
        // from CurrentValue. Varying curves need interval integration (tracked in #2090).
        if (speed.Animation is not KeyFrameAnimation<float> animation)
            return false;

        // With no keyframes, playback uses the animator's default value (zero for float),
        // not the property's stored speed. A frozen empty curve must not block linked media.
        if (animation.KeyFrames.Count == 0)
        {
            factor = animation.Interpolate(TimeSpan.Zero) / 100.0;
            return true;
        }

        if (animation.KeyFrames[0] is not KeyFrame<float> first)
            return false;

        factor = first.Value / 100.0;
        return animation.KeyFrames.All(frame => frame is KeyFrame<float> typed && typed.Value == first.Value);
    }

    private static bool TryGetConstantLoop(IProperty<bool> loop, out bool value)
    {
        value = loop.CurrentValue;
        if (loop.HasExpression) return false;
        if (loop.Animation == null) return true;
        if (loop.Animation is not KeyFrameAnimation<bool> animation) return false;

        if (animation.KeyFrames.Count == 0)
        {
            value = animation.Interpolate(TimeSpan.Zero);
            return true;
        }

        if (animation.KeyFrames[0] is not KeyFrame<bool> first) return false;
        value = first.Value;
        return animation.KeyFrames.All(frame => frame is KeyFrame<bool> typed && typed.Value == first.Value);
    }

    private static bool HasIdentityTimeMapping(DrawableTimeController controller)
        => TryGetConstantSpeed(controller.Speed, out double factor, evaluatesExpressions: true) && factor == 1
           && !controller.OffsetPosition.HasExpression && controller.OffsetPosition.CurrentValue == TimeSpan.Zero
           && !controller.AdjustTimeRange.HasExpression && !controller.AdjustTimeRange.CurrentValue
           && !controller.Reverse.HasExpression && !controller.Reverse.CurrentValue
           && !controller.Loop.HasExpression && !controller.Loop.CurrentValue
           && !controller.HoldFirstFrame.HasExpression && !controller.HoldFirstFrame.CurrentValue
           && !controller.HoldLastFrame.HasExpression && !controller.HoldLastFrame.CurrentValue
           && !controller.FrameRate.HasExpression && controller.FrameRate.CurrentValue == 0;

    public static bool CanTrim(IReadOnlyList<Target> targets)
        => targets.All(static target => target.SupportsTrimming);

    private static Target CreateVideoTarget(SourceVideo video)
    {
        // Reject before resource creation: evaluating a time-dependent expression at the
        // default time cannot establish a constant mapping and may need playback-only context.
        if (video.Speed.HasExpression)
            return new Target(video.OffsetPosition, null, video.Speed, isVideo: true);

        // Read the raw source duration: CalculateOriginalTime already divides by Speed and
        // therefore returns timeline time, whereas OffsetPosition is stored in source time.
        // An exhausted source still has a known total even when TryGetOriginalDuration returns false.
        using var resource = video.ToResource(CompositionContext.Default);
        TimeSpan? total = ((SourceVideo.Resource)resource).Source?.Duration;
        return new Target(video.OffsetPosition, total, video.Speed, isVideo: true);
    }

    private static Target CreateSoundTarget(SourceSound sound)
    {
        // SourceSound.TryGetOriginalDuration returns the full source duration.
        TimeSpan? total = sound.TryGetOriginalDuration(out TimeSpan duration) ? duration : null;
        return new Target(sound.OffsetPosition, total, sound.Speed);
    }

    private static Target CreateSceneSoundTarget(SceneSound sound)
    {
        // The referenced scene is the "source": its duration bounds how far the media
        // window can advance. Unresolved references stay unbounded, like a SourceVideo
        // without a loaded source.
        TimeSpan? total = sound.ReferencedScene.CurrentValue?.Duration;
        return new Target(sound.OffsetPosition, total, sound.Speed);
    }

    private static void IncludeWindow(Target target, EngineObject media, Element element, TimeRange window)
    {
        if (!target.SupportsTrimming || target.IsFrozen) return;

        double visibleEnd;
        TimeSpan room = TimeSpan.MaxValue;
        if (media is SourceVideo video)
        {
            double localStart = (window.Start - video.TimeRange.Start).Ticks * target.Speed;
            double localEnd = (window.End - video.TimeRange.Start).Ticks * target.Speed;
            visibleEnd = localEnd;
            room = SourceTailRoom(target, localEnd);
            if (target.Total is { } duration)
            {
                if (localStart < 0)
                {
                    visibleEnd = localEnd < 0 ? duration.Ticks + localEnd : Math.Max(duration.Ticks, localEnd);
                    if (target.Current > TimeSpan.Zero)
                    {
                        // Positive offsets reach the wrapped source end before local zero.
                        TimeSpan wrappedRoom = TimeSpan.FromTicks(
                            TimelineHeadroom(target, Math.Floor(-target.Current.Ticks - localEnd)));
                        if (wrappedRoom < room) room = wrappedRoom;
                    }
                }

                bool loopVaries = !TryGetConstantLoop(video.IsLoop, out bool isLoop);
                if (duration > TimeSpan.Zero && (isLoop || loopVaries))
                {
                    var loop = LoopWindow(target, localStart, localEnd, duration);
                    // A varying loop flag can expose either mapping within the window.
                    visibleEnd = loopVaries ? Math.Max(visibleEnd, loop.VisibleEnd) : loop.VisibleEnd;
                    room = loopVaries && room < loop.Room ? room : loop.Room;
                }
            }
        }
        else
        {
            TimeRange visible = media.TimeRange.Intersect(element.Range);
            visibleEnd = visible.IsEmpty ? 0 : (visible.End - media.TimeRange.Start).Ticks * target.Speed;
            if (target.Total is { } duration)
            {
                double sourceEnd = duration.Ticks - target.Current.Ticks;
                // Fixed audio only limits growth when more of its range would be exposed
                // beyond the source end. Inherited ranges grow with the element.
                if (FollowsElementRange(media, element)
                    || (media.TimeRange.End > element.Range.End && media.TimeRange.Duration.Ticks * target.Speed > sourceEnd))
                {
                    room = SourceTailRoom(target, (element.Range.End - media.TimeRange.Start).Ticks * target.Speed);
                }
            }
        }

        // A shared media offset must satisfy both its ordinary and controlled paths.
        target.VisibleSourceEndTicks = Math.Max(target.VisibleSourceEndTicks, visibleEnd);
        if (room < target.OutPointHeadroom) target.OutPointHeadroom = room;
    }

    private static (double VisibleEnd, TimeSpan Room) LoopWindow(
        Target target, double localStart, double localEnd, TimeSpan duration)
    {
        double period = duration.Ticks;
        double endPhase = localEnd % period;
        if (endPhase < 0) endPhase += period;

        // A cycle boundary exposes the source tail even when both endpoints map earlier.
        bool reachesTail = Math.Floor(localStart / period) != Math.Floor(localEnd / period);
        double visibleEnd = reachesTail ? period : endPhase;
        TimeSpan room = TimeSpan.MaxValue;
        if (target.Current != TimeSpan.Zero)
        {
            // Nonzero offsets cannot grow through the next cycle's invalid source interval.
            double sourceRoom = Math.Floor(period - Math.Max(0, target.Current.Ticks) - visibleEnd);
            room = TimeSpan.FromTicks(TimelineHeadroom(target, sourceRoom));
        }

        return (visibleEnd, room);
    }

    private static TimeSpan SourceTailRoom(Target target, double sampledEndTicks)
        => target.Total is { } total
            ? TimeSpan.FromTicks(TimelineHeadroom(target, Math.Floor(total.Ticks - target.Current.Ticks - sampledEndTicks)))
            : TimeSpan.MaxValue;

    private static bool FollowsElementRange(EngineObject media, Element element)
    {
        while (media.HierarchicalParent != element)
        {
            if (media.IsTimeAnchor || media.HierarchicalParent is not EngineObject parent)
                return false;
            media = parent;
        }

        return true;
    }

    // The largest-magnitude timeline delta (in the requested direction) that every stream can
    // apply without leaving its sampled source window. One shared timeline
    // delta keeps linked streams in sync even when one hits its source boundary first.
    public static TimeSpan ClampSharedDelta(IReadOnlyList<Target> targets, TimeSpan delta)
    {
        if (delta == TimeSpan.Zero || targets.Count == 0 || !CanTrim(targets)) return TimeSpan.Zero;

        long magnitude = Math.Abs(delta.Ticks);
        foreach (Target target in targets)
        {
            long allowed = delta > TimeSpan.Zero
                ? ForwardHeadroom(target)
                : TimelineHeadroom(target, target.Current.Ticks);
            magnitude = Math.Min(magnitude, allowed);
        }

        return TimeSpan.FromTicks(delta > TimeSpan.Zero ? magnitude : -magnitude);
    }

    private static long ForwardHeadroom(Target target)
    {
        if (target.Total is not { } total) return long.MaxValue;

        // OffsetPosition stores whole ticks; leave no fractional source tick for the
        // later TimeSpan multiplication to round up past the source tail.
        double maxOffset = Math.Max(0, Math.Floor(total.Ticks - target.VisibleSourceEndTicks));
        return TimelineHeadroom(target, maxOffset - target.Current.Ticks);
    }

    private static long TimelineHeadroom(Target target, double sourceTicks)
    {
        double ticks = Math.Max(0, sourceTicks) / target.Speed;
        // Round down so converting the shared delta back never passes a source boundary.
        // Very slow media can have more timeline headroom than TimeSpan can represent.
        return ticks >= long.MaxValue ? long.MaxValue : (long)ticks;
    }

    // `applied` spans one whole trim operation: the per-element visited set in Collect only
    // dedups within an element, so a media instance referenced from several participating
    // elements (e.g. via another element's DrawablePresenter.Target) would otherwise receive
    // the delta once per element. Callers touching multiple elements pass one shared set.
    public static void ApplyOffsetDelta(
        IReadOnlyList<Target> targets, TimeSpan delta, HashSet<IProperty<TimeSpan>>? applied = null)
    {
        if (delta == TimeSpan.Zero) return;

        foreach (Target target in targets)
        {
            if (applied is null || applied.Add(target.Offset))
            {
                target.Current += delta * target.Speed;
            }
        }
    }

    // Room to extend the element's out-point (grow its length while the in-point stays put),
    // bounded by the tightest source tail among its streams, expressed in timeline time.
    // TimeSpan.MaxValue when unbounded.
    public static TimeSpan OutPointRoom(IReadOnlyList<Target> targets)
    {
        TimeSpan room = TimeSpan.MaxValue;
        foreach (Target target in targets)
        {
            if (target.OutPointHeadroom < room) room = target.OutPointHeadroom;
        }

        return room;
    }

    // Timeline room to pull the element's in-point earlier, bounded by each stream's current
    // source offset divided by its speed (the offset cannot go below zero). This bound holds even
    // when the source duration is unknown (Total == null), so those streams are not skipped.
    // TimeSpan.MaxValue when the element has no slip-able media.
    public static TimeSpan InPointRoom(IReadOnlyList<Target> targets)
    {
        TimeSpan room = TimeSpan.MaxValue;
        foreach (Target target in targets)
        {
            TimeSpan available = TimeSpan.FromTicks(TimelineHeadroom(target, target.Current.Ticks));
            if (available < room) room = available;
        }

        return room;
    }
}
