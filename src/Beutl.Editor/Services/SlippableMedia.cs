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
        public Target(IProperty<TimeSpan> offset, TimeSpan? total, TimeSpan visibleEnd, TimeSpan outPointHeadroom,
            bool isVideo = false)
        {
            Offset = offset;
            Total = total;
            VisibleEnd = visibleEnd;
            OutPointHeadroom = outPointHeadroom;
            IsVideo = isVideo;
        }

        public IProperty<TimeSpan> Offset { get; }

        public TimeSpan? Total { get; }

        // End of the visible window relative to the source object's start. Keep any
        // elapsed prefix when the containing element clips the beginning of that window.
        public TimeSpan VisibleEnd { get; }

        public TimeSpan OutPointHeadroom { get; }

        public bool IsVideo { get; }

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
        var targets = new List<Target>();
        var visited = new HashSet<object>();
        foreach (EngineObject obj in element.Objects)
        {
            CollectFrom(obj, element, targets, visited);
        }

        // Time controllers can sample beyond their target's declared range. Until trim
        // operations share their time mapping (#2569), retain the pre-existing video bounds.
        // They only process drawables, so independent audio keeps its own clipped window.
        // Check the whole traversal so a shared source reached before its controller is
        // treated the same way regardless of collection order.
        if (visited.Any(static obj => obj is DrawableTimeController))
        {
            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                if (target.IsVideo)
                    targets[i] = CreateElementBoundTarget(target.Offset, target.Total, element.Length, isVideo: true);
            }
        }

        return targets;
    }

    // The visited set makes each node contribute once: a media object reachable through
    // several paths (e.g. one SourceVideo shared by two DrawablePresenter.Targets) must not
    // receive the shared delta once per path, and a presenter cycle must not recurse forever.
    private static void CollectFrom(object obj, Element element, List<Target> targets, HashSet<object> visited)
    {
        if (!visited.Add(obj)) return;

        switch (obj)
        {
            case SourceVideo video:
                targets.Add(CreateVideoTarget(video, element));
                break;
            case SourceSound sound:
                targets.Add(CreateSoundTarget(sound, element));
                break;
            case SceneSound sceneSound:
                targets.Add(CreateSceneSoundTarget(sceneSound, element));
                break;
            case SoundGroup soundGroup:
                foreach (Sound child in soundGroup.Children)
                    CollectFrom(child, element, targets, visited);
                break;
            case DrawableGroup drawableGroup:
                foreach (Drawable child in drawableGroup.Children)
                    CollectFrom(child, element, targets, visited);
                break;
            case DrawableDecorator decorator:
                foreach (Drawable child in decorator.Children)
                    CollectFrom(child, element, targets, visited);
                break;
            // DrawablePresenter / DrawableTimeController render the drawable in Target
            // rather than a Children list, so a wrapped SourceVideo is only reachable here.
            case IPresenter<Drawable> presenter:
                if (presenter.Target.CurrentValue is { } presented)
                    CollectFrom(presented, element, targets, visited);
                break;
        }
    }

    private static Target CreateVideoTarget(SourceVideo video, Element element)
    {
        // An exhausted source still has a known total even when TryGetOriginalDuration returns false.
        using var resource = video.ToResource(CompositionContext.Default);
        TimeSpan? total = video.CalculateOriginalTime((SourceVideo.Resource)resource);
        if (HasSpeedMapping(video.Speed)
            || video.IsLoop.CurrentValue || video.IsLoop.Animation is not null || video.IsLoop.HasExpression)
        {
            return CreateElementBoundTarget(video.OffsetPosition, total, element.Length, isVideo: true);
        }

        // DrawableGroup and presenters do not clip a video at its own TimeRange.End.
        // SourceVideo is sampled throughout the containing element's active window.
        TimeSpan visibleEnd = element.Range.End - video.TimeRange.Start;
        TimeSpan room = TimeSpan.MaxValue;
        if (total is { } duration)
        {
            room = video.TimeRange.Start + duration - video.OffsetPosition.CurrentValue - element.Range.End;
            if (room < TimeSpan.Zero) room = TimeSpan.Zero;

            // SourceVideo wraps negative local times from the source's end, even with
            // IsLoop disabled. A window crossing local zero samples up to that source end.
            if (element.Start < video.TimeRange.Start)
            {
                visibleEnd = visibleEnd < TimeSpan.Zero
                    ? duration + visibleEnd
                    : (visibleEnd > duration ? visibleEnd : duration);
            }
        }
        if (visibleEnd < TimeSpan.Zero) visibleEnd = TimeSpan.Zero;

        return new Target(video.OffsetPosition, total, visibleEnd, room, isVideo: true);
    }

    private static Target CreateSoundTarget(SourceSound sound, Element element)
    {
        // SourceSound.TryGetOriginalDuration returns the full source duration.
        TimeSpan? total = sound.TryGetOriginalDuration(out TimeSpan duration) ? duration : null;
        return CreateClippedSoundTarget(sound, total, element);
    }

    private static Target CreateSceneSoundTarget(SceneSound sound, Element element)
    {
        // The referenced scene is the "source": its duration bounds how far the media
        // window can advance. Unresolved references stay unbounded, like a SourceVideo
        // without a loaded source.
        TimeSpan? total = sound.ReferencedScene.CurrentValue?.Duration;
        return CreateClippedSoundTarget(sound, total, element);
    }

    private static bool HasSpeedMapping(IProperty<float> speed)
        => speed.CurrentValue != 100f || speed.Animation is not null || speed.HasExpression;

    private static Target CreateClippedSoundTarget(Sound media, TimeSpan? total, Element element)
    {
        IProperty<TimeSpan> offset = media.OffsetPosition;
        // Speed-aware clamp and offset conversion are tracked separately in #2069/#2090.
        // Do not relax their existing bounds using an unmapped nested timeline duration.
        if (HasSpeedMapping(media.Speed))
            return CreateElementBoundTarget(offset, total, element.Length);

        TimeRange visible = media.TimeRange.Intersect(element.Range);
        TimeSpan visibleEnd = visible.IsEmpty ? TimeSpan.Zero : visible.End - media.TimeRange.Start;
        TimeSpan outPointHeadroom = TimeSpan.MaxValue;
        if (total is { } duration)
        {
            TimeSpan sourceEnd = duration - offset.CurrentValue;
            // A fixed range already fully exposed by the element never grows with it.
            // A longer fixed range only constrains growth if revealing more of it would
            // reach the source end before the fixed range itself ends.
            if (FollowsElementRange(media, element)
                || (media.TimeRange.End > element.Range.End && media.TimeRange.Duration > sourceEnd))
            {
                outPointHeadroom = media.TimeRange.Start + sourceEnd - element.Range.End;
                if (outPointHeadroom < TimeSpan.Zero) outPointHeadroom = TimeSpan.Zero;
            }
        }

        return new Target(offset, total, visibleEnd, outPointHeadroom);
    }

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

    private static Target CreateElementBoundTarget(IProperty<TimeSpan> offset, TimeSpan? total, TimeSpan length,
        bool isVideo = false)
    {
        TimeSpan room = total is { } duration ? duration - offset.CurrentValue - length : TimeSpan.MaxValue;
        if (room < TimeSpan.Zero) room = TimeSpan.Zero;
        return new Target(offset, total, length, room, isVideo);
    }

    // The largest-magnitude delta (in the requested direction) that every stream can apply
    // without leaving [0, Total - VisibleEnd]. Applying one shared delta keeps linked
    // streams (e.g. a video + audio pair) in sync even when one hits its source boundary first.
    public static TimeSpan ClampSharedDelta(IReadOnlyList<Target> targets, TimeSpan delta)
    {
        if (delta == TimeSpan.Zero || targets.Count == 0) return TimeSpan.Zero;

        long magnitude = Math.Abs(delta.Ticks);
        foreach (Target target in targets)
        {
            long allowed = delta > TimeSpan.Zero
                ? ForwardHeadroom(target)
                : Math.Max(0L, target.Current.Ticks);
            magnitude = Math.Min(magnitude, allowed);
        }

        return TimeSpan.FromTicks(delta > TimeSpan.Zero ? magnitude : -magnitude);
    }

    private static long ForwardHeadroom(Target target)
    {
        if (target.Total is not { } total) return long.MaxValue;

        TimeSpan maxOffset = total - target.VisibleEnd;
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
    public static TimeSpan OutPointRoom(IReadOnlyList<Target> targets)
    {
        TimeSpan room = TimeSpan.MaxValue;
        foreach (Target target in targets)
        {
            if (target.OutPointHeadroom < room) room = target.OutPointHeadroom;
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
