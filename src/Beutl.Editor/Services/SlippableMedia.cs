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
    private readonly record struct TimeContext(
        TimeRange Range,
        Func<TimeSpan, TimeSpan>? TimelineDurationFromTarget,
        bool HasUnboundedTail,
        bool IsReversed);

    // A single source-backed media stream whose OffsetPosition can be slipped.
    // Total is the absolute source duration (null when the stream has no bounded source).
    internal sealed class Target
    {
        public Target(
            IProperty<TimeSpan> offset,
            TimeSpan? total,
            TimeSpan? consumedDuration = null,
            TimeSpan? timelineRoom = null)
        {
            Offset = offset;
            Total = total;
            ConsumedDuration = consumedDuration;
            TimelineRoom = timelineRoom;
        }

        public IProperty<TimeSpan> Offset { get; }

        public TimeSpan? Total { get; private set; }

        public TimeSpan? ConsumedDuration { get; private set; }

        public TimeSpan? TimelineRoom { get; private set; }

        public TimeSpan Current
        {
            get => Offset.CurrentValue;
            set => Offset.CurrentValue = value;
        }

        internal void Merge(Target other)
        {
            if (other.Total is { } total)
            {
                Total = Total is { } currentTotal ? TimeSpan.FromTicks(Math.Min(currentTotal.Ticks, total.Ticks)) : total;
            }

            if (other.ConsumedDuration is { } consumed)
            {
                ConsumedDuration = ConsumedDuration is { } currentConsumed
                    ? TimeSpan.FromTicks(Math.Max(currentConsumed.Ticks, consumed.Ticks))
                    : consumed;
            }

            if (other.TimelineRoom is { } room)
            {
                TimelineRoom = TimelineRoom is { } currentRoom
                    ? TimeSpan.FromTicks(Math.Min(currentRoom.Ticks, room.Ticks))
                    : room;
            }
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
        var targets = new List<Target>();
        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var context = new TimeContext(element.Range, null, false, false);
        foreach (EngineObject obj in element.Objects)
        {
            CollectFrom(obj, targets, active, context);
        }

        return targets;
    }

    // The active set prevents cycles while still allowing a shared media object to contribute
    // once for every presentation path. Those path-specific bounds are merged by AddTarget.
    private static void CollectFrom(
        object obj,
        List<Target> targets,
        HashSet<object> active,
        TimeContext context)
    {
        if (!active.Add(obj)) return;

        try
        {
            switch (obj)
            {
                case SourceVideo video:
                    AddTarget(targets, CreateVideoTarget(video, context));
                    break;
                case SourceSound sound:
                    AddTarget(targets, CreateSoundTarget(sound));
                    break;
                case SceneSound sceneSound:
                    AddTarget(targets, CreateSceneSoundTarget(sceneSound));
                    break;
                case SoundGroup soundGroup:
                    foreach (Sound child in soundGroup.Children)
                        CollectFrom(child, targets, active, context);
                    break;
                case DrawableGroup drawableGroup:
                    foreach (Drawable child in drawableGroup.Children)
                        CollectFrom(child, targets, active, context);
                    break;
                case DrawableDecorator decorator:
                    foreach (Drawable child in decorator.Children)
                        CollectFrom(child, targets, active, context);
                    break;
                case DrawableTimeController controller:
                    if (controller.Target.CurrentValue is { } controlled)
                    {
                        using var resource = (DrawableTimeController.Resource)controller.ToResource(
                            CompositionContext.Default);
                        TimeRange mapped = controller.CalculateTargetTimeRange(context.Range, controlled, resource);
                        TimeSpan currentTime = context.IsReversed ? context.Range.Start : context.Range.End;
                        Func<TimeSpan, TimeSpan> mapper = duration =>
                        {
                            TimeSpan parentDuration = controller.CalculateTimelineDuration(
                                currentTime, duration, controlled, resource);
                            return context.TimelineDurationFromTarget?.Invoke(parentDuration) ?? parentDuration;
                        };
                        var mappedContext = new TimeContext(
                            mapped,
                            mapper,
                            context.HasUnboundedTail
                                || resource.Loop
                                || resource.HoldFirstFrame
                                || resource.HoldLastFrame,
                            context.IsReversed ^ resource.Reverse);
                        CollectFrom(controlled, targets, active, mappedContext);
                    }
                    break;
                // DrawablePresenter / DrawableTimeController render the drawable in Target
                // rather than a Children list, so a wrapped SourceVideo is only reachable here.
                case IPresenter<Drawable> presenter:
                    if (presenter.Target.CurrentValue is { } presented)
                        CollectFrom(presented, targets, active, context);
                    break;
            }
        }
        finally
        {
            active.Remove(obj);
        }
    }

    private static void AddTarget(List<Target> targets, Target target)
    {
        foreach (Target existing in targets)
        {
            if (ReferenceEquals(existing.Offset, target.Offset))
            {
                existing.Merge(target);
                return;
            }
        }

        targets.Add(target);
    }

    private static Target CreateVideoTarget(SourceVideo video, TimeContext context)
    {
        // Slip offsets and media bounds use source time, including speed conversion.
        using var resource = (SourceVideo.Resource)video.ToResource(CompositionContext.Default);
        TimeSpan? total = resource.Source is { } source && source.Duration > TimeSpan.Zero
            ? source.Duration
            : null;
        TimeSpan consumedDuration = GetConsumedDuration(video, context.Range, resource);
        if (consumedDuration < TimeSpan.Zero) consumedDuration = TimeSpan.Zero;
        TimeSpan? timelineRoom = null;
        if (total is { } sourceDuration)
        {
            TimeSpan sourceRoom = sourceDuration - video.OffsetPosition.CurrentValue - consumedDuration;
            if (sourceRoom < TimeSpan.Zero) sourceRoom = TimeSpan.Zero;
            if (context.HasUnboundedTail)
            {
                timelineRoom = TimeSpan.MaxValue;
            }
            else if (context.IsReversed)
            {
                TimeSpan targetRoom = context.Range.Start - video.TimeRange.Start;
                if (targetRoom < TimeSpan.Zero) targetRoom = TimeSpan.Zero;
                timelineRoom = context.TimelineDurationFromTarget?.Invoke(targetRoom) ?? targetRoom;
            }
            else
            {
                TimeSpan targetRoom = video.CalculateTimelineDuration(
                    GetVideoClockStartAt(video, context.Range.End), sourceRoom, resource);
                timelineRoom = context.TimelineDurationFromTarget?.Invoke(targetRoom) ?? targetRoom;
            }
        }

        return new Target(video.OffsetPosition, total, consumedDuration, timelineRoom);
    }

    private static TimeSpan GetConsumedDuration(
        SourceVideo video, TimeRange range, SourceVideo.Resource resource)
    {
        TimeSpan duration = range.Duration;
        TimeSpan clockStart = GetVideoClockStartAt(video, range.Start);
        return duration > TimeSpan.Zero
            ? video.CalculateVideoDuration(clockStart, duration, resource)
            : TimeSpan.Zero;
    }

    private static TimeSpan GetVideoClockStartAt(SourceVideo video, TimeSpan time)
    {
        return video.Speed.Animation is KeyFrameAnimation<float> { UseGlobalClock: true }
            ? time
            : time - video.TimeRange.Start;
    }

    private static Target CreateSoundTarget(SourceSound sound)
    {
        // SourceSound.TryGetOriginalDuration returns the full source duration.
        TimeSpan? total = sound.TryGetOriginalDuration(out TimeSpan duration) ? duration : null;
        return new Target(sound.OffsetPosition, total);
    }

    private static Target CreateSceneSoundTarget(SceneSound sound)
    {
        // The referenced scene is the "source": its duration bounds how far the media
        // window can advance. Unresolved references stay unbounded, like a SourceVideo
        // without a loaded source.
        TimeSpan? total = sound.ReferencedScene.CurrentValue?.Duration;
        return new Target(sound.OffsetPosition, total);
    }

    // The largest-magnitude delta (in the requested direction) that every stream can apply
    // without leaving [0, Total - consumed duration]. Applying one shared delta keeps linked
    // streams (e.g. a video + audio pair) in sync even when one hits its source boundary first.
    public static TimeSpan ClampSharedDelta(IReadOnlyList<Target> targets, TimeSpan delta, TimeSpan elementLength)
    {
        if (delta == TimeSpan.Zero || targets.Count == 0) return TimeSpan.Zero;

        long magnitude = Math.Abs(delta.Ticks);
        foreach (Target target in targets)
        {
            long allowed = delta > TimeSpan.Zero
                ? ForwardHeadroom(target, elementLength)
                : Math.Max(0L, target.Current.Ticks);
            magnitude = Math.Min(magnitude, allowed);
        }

        return TimeSpan.FromTicks(delta > TimeSpan.Zero ? magnitude : -magnitude);
    }

    private static long ForwardHeadroom(Target target, TimeSpan elementLength)
    {
        if (target.Total is not { } total) return long.MaxValue;

        TimeSpan maxOffset = total - (target.ConsumedDuration ?? elementLength);
        if (maxOffset < TimeSpan.Zero) maxOffset = TimeSpan.Zero;
        return Math.Max(0L, (maxOffset - target.Current).Ticks);
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
                target.Current += delta;
            }
        }
    }

    // Room to extend the element's out-point (grow its length while the in-point stays put),
    // bounded by the tightest source tail among its streams. TimeSpan.MaxValue when unbounded.
    public static TimeSpan OutPointRoom(IReadOnlyList<Target> targets, TimeSpan elementLength)
    {
        TimeSpan room = TimeSpan.MaxValue;
        foreach (Target target in targets)
        {
            if (target.Total is not { } total) continue;

            TimeSpan available = target.TimelineRoom
                ?? (total - target.Current - elementLength);
            if (available < TimeSpan.Zero) available = TimeSpan.Zero;
            if (available < room) room = available;
        }

        return room;
    }

    // Room to pull the element's in-point earlier, bounded by the smallest current offset among
    // its streams (the offset cannot go below zero). Unlike OutPointRoom this bound holds even
    // when the source duration is unknown (Total == null), so those streams are not skipped.
    // TimeSpan.MaxValue when the element has no slip-able media.
    public static TimeSpan InPointRoom(IReadOnlyList<Target> targets)
    {
        TimeSpan room = TimeSpan.MaxValue;
        foreach (Target target in targets)
        {
            if (target.Current < room) room = target.Current;
        }

        return room;
    }
}
