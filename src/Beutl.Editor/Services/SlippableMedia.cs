using Beutl.Animation;
using Beutl.Audio;
using Beutl.Composition;
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
        public Target(IProperty<TimeSpan> offset, TimeSpan? total, IProperty<float> speed, bool isVideo = false)
        {
            Offset = offset;
            Total = total;
            IsVideo = isVideo;
            SupportsTrimming = TryGetConstantSpeed(speed, out double factor, evaluatesExpressions: isVideo);
            Speed = factor;
        }

        public IProperty<TimeSpan> Offset { get; }

        public TimeSpan? Total { get; }

        public bool IsVideo { get; }

        // Source-time units consumed per timeline-time unit.
        public double Speed { get; }

        public bool SupportsTrimming { get; set; }

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
            bool supportsTimeMapping = true;
            CollectFrom(obj, targets, visited, ref supportsTimeMapping);

            // A controller can consume preceding drawables through Flow, or an explicit
            // nested Target. Guard videos reached so far; later top-level drawables cannot
            // feed this controller. Audio never enters drawable flow.
            if (!supportsTimeMapping)
            {
                foreach (Target target in targets)
                {
                    if (target.IsVideo)
                        target.SupportsTrimming = false;
                }
            }
        }

        // Frozen media consumes no source time, so it neither moves nor limits a trim.
        // An animated stream whose base value is zero is not necessarily frozen.
        targets.RemoveAll(static target => target.SupportsTrimming && target.Speed <= 0);
        return targets;
    }

    // The visited set makes each node contribute once: a media object reachable through
    // several paths (e.g. one SourceVideo shared by two DrawablePresenter.Targets) must not
    // receive the shared delta once per path, and a presenter cycle must not recurse forever.
    private static void CollectFrom(
        object obj, List<Target> targets, HashSet<object> visited, ref bool supportsTimeMapping)
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
            case SceneSound sceneSound:
                targets.Add(CreateSceneSoundTarget(sceneSound));
                break;
            case SoundGroup soundGroup:
                foreach (Sound child in soundGroup.Children)
                    CollectFrom(child, targets, visited, ref supportsTimeMapping);
                break;
            case DrawableGroup drawableGroup:
                foreach (Drawable child in drawableGroup.Children)
                    CollectFrom(child, targets, visited, ref supportsTimeMapping);
                break;
            case DrawableDecorator decorator:
                foreach (Drawable child in decorator.Children)
                    CollectFrom(child, targets, visited, ref supportsTimeMapping);
                break;
            case DrawableTimeController controller:
                supportsTimeMapping &= HasIdentityTimeMapping(controller);
                if (controller.Target.CurrentValue is { } controlled)
                    CollectFrom(controlled, targets, visited, ref supportsTimeMapping);
                break;
            // Presenters render Target rather than a Children list.
            case IPresenter<Drawable> presenter:
                if (presenter.Target.CurrentValue is { } presented)
                    CollectFrom(presented, targets, visited, ref supportsTimeMapping);
                break;
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

    // The largest-magnitude timeline delta (in the requested direction) that every stream can
    // apply without leaving [0, Total - elementLength * Speed] in source time. One shared timeline
    // delta keeps linked streams in sync even when one hits its source boundary first.
    public static TimeSpan ClampSharedDelta(IReadOnlyList<Target> targets, TimeSpan delta, TimeSpan elementLength)
    {
        if (delta == TimeSpan.Zero || targets.Count == 0 || !CanTrim(targets)) return TimeSpan.Zero;

        long magnitude = Math.Abs(delta.Ticks);
        foreach (Target target in targets)
        {
            long allowed = delta > TimeSpan.Zero
                ? ForwardHeadroom(target, elementLength)
                : TimelineHeadroom(target, target.Current.Ticks);
            magnitude = Math.Min(magnitude, allowed);
        }

        return TimeSpan.FromTicks(delta > TimeSpan.Zero ? magnitude : -magnitude);
    }

    private static long ForwardHeadroom(Target target, TimeSpan elementLength)
    {
        if (target.Total is not { } total) return long.MaxValue;

        // OffsetPosition stores whole ticks; leave no fractional source tick for the
        // later TimeSpan multiplication to round up past the source tail.
        double maxOffset = Math.Max(0, Math.Floor(total.Ticks - elementLength.Ticks * target.Speed));
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
    public static TimeSpan OutPointRoom(IReadOnlyList<Target> targets, TimeSpan elementLength)
    {
        TimeSpan room = TimeSpan.MaxValue;
        foreach (Target target in targets)
        {
            TimeSpan available = TimeSpan.FromTicks(ForwardHeadroom(target, elementLength));
            if (available < room) room = available;
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
