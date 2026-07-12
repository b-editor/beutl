using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
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
        public Target(IProperty<TimeSpan> offset, TimeSpan? total)
        {
            Offset = offset;
            Total = total;
        }

        public IProperty<TimeSpan> Offset { get; }

        public TimeSpan? Total { get; }

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
            CollectFrom(obj, targets, visited);
        }

        return targets;
    }

    // The visited set makes each node contribute once: a media object reachable through
    // several paths (e.g. one SourceVideo shared by two DrawablePresenter.Targets) must not
    // receive the shared delta once per path, and a presenter cycle must not recurse forever.
    private static void CollectFrom(object obj, List<Target> targets, HashSet<object> visited)
    {
        if (!visited.Add(obj)) return;

        switch (obj)
        {
            case SourceVideo video:
                targets.Add(CreateVideoTarget(video));
                break;
            case SourceSound sound:
                targets.Add(CreateSoundTarget(sound));
                break;
            case SoundGroup soundGroup:
                foreach (Sound child in soundGroup.Children)
                    CollectFrom(child, targets, visited);
                break;
            case DrawableGroup drawableGroup:
                foreach (Drawable child in drawableGroup.Children)
                    CollectFrom(child, targets, visited);
                break;
            case DrawableDecorator decorator:
                foreach (Drawable child in decorator.Children)
                    CollectFrom(child, targets, visited);
                break;
            // DrawablePresenter / DrawableTimeController render the drawable in Target
            // rather than a Children list, so a wrapped SourceVideo is only reachable here.
            case IPresenter<Drawable> presenter:
                if (presenter.Target.CurrentValue is { } presented)
                    CollectFrom(presented, targets, visited);
                break;
        }
    }

    private static Target CreateVideoTarget(SourceVideo video)
    {
        // SourceVideo.TryGetOriginalDuration returns the duration remaining from the current
        // offset, so the absolute source length is current + remaining.
        TimeSpan? total = video.TryGetOriginalDuration(out TimeSpan remaining)
            ? video.OffsetPosition.CurrentValue + remaining
            : null;
        return new Target(video.OffsetPosition, total);
    }

    private static Target CreateSoundTarget(SourceSound sound)
    {
        // SourceSound.TryGetOriginalDuration returns the full source duration.
        TimeSpan? total = sound.TryGetOriginalDuration(out TimeSpan duration) ? duration : null;
        return new Target(sound.OffsetPosition, total);
    }

    // The largest-magnitude delta (in the requested direction) that every stream can apply
    // without leaving [0, Total - elementLength]. Applying one shared delta keeps linked
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

        TimeSpan maxOffset = total - elementLength;
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

            TimeSpan available = total - target.Current - elementLength;
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
