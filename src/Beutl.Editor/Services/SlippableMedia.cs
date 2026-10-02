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
        public Target(IProperty<TimeSpan> offset, TimeSpan? total, TimeSpan visibleEnd, TimeSpan outPointHeadroom)
        {
            Offset = offset;
            Total = total;
            VisibleEnd = visibleEnd;
            OutPointHeadroom = outPointHeadroom;
        }

        public IProperty<TimeSpan> Offset { get; }

        public TimeSpan? Total { get; }

        // End of the visible window relative to the source object's start. Keep any
        // elapsed prefix when the containing element clips the beginning of that window.
        public TimeSpan VisibleEnd { get; }

        public TimeSpan OutPointHeadroom { get; }

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
        var targets = new Dictionary<IProperty<TimeSpan>, Target>();
        var visited = new HashSet<(object, bool)>();
        foreach (EngineObject obj in element.Objects)
        {
            CollectFrom(obj, element, targets, visited);
        }

        return [.. targets.Values];
    }

    // Visit shared objects once per timing context. A source reached through both a plain
    // presenter and a time controller must satisfy both windows, regardless of visit order.
    // Targets remain unique by offset property, and presenter cycles stay bounded.
    private static void CollectFrom(object obj, Element element, Dictionary<IProperty<TimeSpan>, Target> targets,
        HashSet<(object, bool)> visited, bool timeControlled = false)
    {
        if (!visited.Add((obj, timeControlled))) return;

        switch (obj)
        {
            case SourceVideo video:
                AddTarget(targets, CreateVideoTarget(video, element, timeControlled));
                break;
            case SourceSound sound:
                AddTarget(targets, CreateSoundTarget(sound, element));
                break;
            case SceneSound sceneSound:
                AddTarget(targets, CreateSceneSoundTarget(sceneSound, element));
                break;
            case SoundGroup soundGroup:
                foreach (Sound child in soundGroup.Children)
                    CollectFrom(child, element, targets, visited, timeControlled);
                break;
            case DrawableGroup drawableGroup:
                foreach (Drawable child in drawableGroup.Children)
                    CollectFrom(child, element, targets, visited, timeControlled);
                break;
            case DrawableDecorator decorator:
                foreach (Drawable child in decorator.Children)
                    CollectFrom(child, element, targets, visited, timeControlled);
                break;
            case DrawableTimeController controller:
                if (controller.Target.CurrentValue is { } controlled)
                    CollectFrom(controlled, element, targets, visited, timeControlled: true);
                break;
            case IPresenter<Drawable> presenter:
                if (presenter.Target.CurrentValue is { } presented)
                    CollectFrom(presented, element, targets, visited, timeControlled);
                break;
        }
    }

    private static void AddTarget(Dictionary<IProperty<TimeSpan>, Target> targets, Target target)
    {
        if (targets.TryGetValue(target.Offset, out Target? existing))
        {
            target = new Target(target.Offset, target.Total,
                existing.VisibleEnd > target.VisibleEnd ? existing.VisibleEnd : target.VisibleEnd,
                existing.OutPointHeadroom < target.OutPointHeadroom ? existing.OutPointHeadroom : target.OutPointHeadroom);
        }

        targets[target.Offset] = target;
    }

    private static Target CreateVideoTarget(SourceVideo video, Element element, bool timeControlled)
    {
        // An exhausted source still has a known total even when TryGetOriginalDuration returns false.
        using var resource = video.ToResource(CompositionContext.Default);
        TimeSpan? total = video.CalculateOriginalTime((SourceVideo.Resource)resource);
        // Controller-aware sampling remains tracked in #2569. Apply the legacy window
        // only on paths beneath that controller, without relaxing an independent path.
        if (timeControlled || HasSpeedMapping(video.Speed)
            || video.IsLoop.CurrentValue || video.IsLoop.Animation is not null || video.IsLoop.HasExpression)
        {
            return CreateElementBoundTarget(video.OffsetPosition, total, element.Length);
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

                // A positive offset reaches the wrapped source end before local zero.
                if (video.OffsetPosition.CurrentValue > TimeSpan.Zero)
                {
                    TimeSpan wrappedRoom = video.TimeRange.Start - video.OffsetPosition.CurrentValue - element.Range.End;
                    if (wrappedRoom < TimeSpan.Zero) wrappedRoom = TimeSpan.Zero;
                    if (wrappedRoom < room) room = wrappedRoom;
                }
            }
        }
        if (visibleEnd < TimeSpan.Zero) visibleEnd = TimeSpan.Zero;

        return new Target(video.OffsetPosition, total, visibleEnd, room);
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

    private static Target CreateElementBoundTarget(IProperty<TimeSpan> offset, TimeSpan? total, TimeSpan length)
    {
        TimeSpan room = total is { } duration ? duration - offset.CurrentValue - length : TimeSpan.MaxValue;
        if (room < TimeSpan.Zero) room = TimeSpan.Zero;
        return new Target(offset, total, length, room);
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
