using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

internal static class SlippableMedia
{
    private sealed record Node(EngineObject Object, IReadOnlyList<Node> Inputs);

    internal sealed class Target(IProperty<TimeSpan> offset, TimeSpan? total, MediaTimeMapping mapping, TimeSpan length)
    {
        public IProperty<TimeSpan> Offset { get; } = offset;
        public TimeSpan? Total { get; } = total;
        public TimeSpan InitialOffset { get; } = offset.CurrentValue;
        public MediaTimeMapping Mapping { get; } = mapping;
        public TimeSpan Length { get; } = length;

        public TimeSpan SourceDelta(TimeSpan delta, bool trim)
            => Mapping.At(delta, extrapolate: delta < TimeSpan.Zero)
                - (trim ? Mapping.At(TimeSpan.Zero, delta, -delta) : Mapping.At(TimeSpan.Zero));

        public bool CanSlip(TimeSpan delta)
        {
            TimeSpan change = SourceDelta(delta, trim: false);
            return Fits(Length, TimeSpan.Zero, change, clampEnd: true, allowRecovery: true);
        }

        public bool CanTrim(TimeSpan delta, bool clampEnd)
            => Fits(Length - delta, delta, SourceDelta(delta, trim: true), clampEnd, allowRecovery: true);

        public bool Fits(TimeSpan length, TimeSpan startDelta = default, TimeSpan offsetDelta = default,
            bool clampEnd = true, bool allowRecovery = false)
        {
            TimeSpan offset = InitialOffset + offsetDelta;
            if (offset < TimeSpan.Zero) return false;
            MediaTimeMapping.Interval range = Mapping.Range(TimeSpan.Zero, length, startDelta, length - Length).Shift(offset);
            TimeSpan lower = TimeSpan.Zero;
            TimeSpan? upper = clampEnd ? Total : null;
            if (allowRecovery)
            {
                // Existing projects may already be out of range. Permit a trim/slip
                // towards valid media without granting any additional overrun.
                MediaTimeMapping.Interval before = Mapping.Range(TimeSpan.Zero, Length).Shift(InitialOffset);
                if (before.Min < lower) lower = before.Min;
                if (upper is { } limit && before.Max > limit) upper = before.Max;
            }
            return range.Min >= lower && (upper == null || range.Max <= upper);
        }
    }

    public static List<Target> Collect(Element element, IReadOnlySet<Element>? timingPeers = null)
    {
        var targets = new List<Target>();
        var path = new HashSet<object>();
        var controllers = new List<MediaTimeMapping.ControllerLink>();
        Project? project = element.FindHierarchicalParent<Project>();
        int sampleRate = project?.Variables.TryGetValue(ProjectVariableKeys.SampleRate, out string? value) == true
            && int.TryParse(value, out int rate) && rate > 0 ? rate : 44100;

        var flow = new List<Node>();
        foreach (EngineObject obj in element.Objects)
        {
            IReadOnlyList<Node> inputs = [];
            if (obj is DrawableTimeController { IsEnabled: true })
            {
                int index = flow.FindIndex(node => node.Object is Drawable { IsEnabled: true });
                if (index >= 0)
                {
                    inputs = [flow[index]];
                    flow.RemoveAt(index);
                }
            }
            else if (obj.IsEnabled && obj is SoundGroup or DrawableGroup or DrawableDecorator)
            {
                bool Consumes(Node node) => node.Object.IsEnabled
                    && (obj is SoundGroup ? node.Object is Sound : node.Object is Drawable);
                inputs = flow.FindAll(Consumes);
                flow.RemoveAll(Consumes);
            }
            flow.Add(new Node(obj, inputs));
        }
        foreach (Node node in flow) CollectFrom(node.Object, node.Inputs);
        return targets;

        // Keep disabled streams in sync too. Detect cycles per path, rather than
        // discarding a second path whose time controller can impose tighter bounds.
        void CollectFrom(EngineObject obj, IReadOnlyList<Node>? inputs = null)
        {
            if (!path.Add(obj)) return;
            if (inputs != null && obj is SoundGroup or DrawableGroup or DrawableDecorator)
            {
                foreach (Node child in inputs) CollectFrom(child.Object, child.Inputs);
            }
            switch (obj)
            {
                case SourceVideo video:
                    using (var resource = video.Source.CurrentValue?.ToResource(CompositionContext.Default))
                    {
                        targets.Add(new Target(video.OffsetPosition, resource?.Duration,
                            new MediaTimeMapping(element, video, video.Speed, controllers, 60, resource?.Duration, timingPeers), element.Length));
                    }
                    break;
                case SourceSound sound:
                    using (var resource = sound.Source.CurrentValue?.ToResource(CompositionContext.Default))
                    {
                        targets.Add(new Target(sound.OffsetPosition, resource?.Duration > TimeSpan.Zero ? resource.Duration : null,
                            new MediaTimeMapping(element, sound, sound.Speed, controllers, sampleRate, timingPeers: timingPeers), element.Length));
                    }
                    break;
                case SceneSound sound:
                    targets.Add(new Target(sound.OffsetPosition, sound.ReferencedScene.CurrentValue?.Duration,
                        new MediaTimeMapping(element, sound, sound.Speed, controllers, sampleRate, timingPeers: timingPeers), element.Length));
                    break;
                case SoundGroup group:
                    foreach (Sound child in group.Children) CollectFrom(child);
                    break;
                case DrawableGroup group:
                    foreach (Drawable child in group.Children) CollectFrom(child);
                    break;
                case DrawableDecorator decorator:
                    foreach (Drawable child in decorator.Children) CollectFrom(child);
                    break;
                case DrawableTimeController controller:
                    Node? input = inputs?.FirstOrDefault();
                    if ((input?.Object ?? controller.Target.CurrentValue) is Drawable target)
                    {
                        if (controller.IsEnabled) controllers.Add(new MediaTimeMapping.ControllerLink(controller, target));
                        CollectFrom(target, input?.Inputs);
                        if (controller.IsEnabled) controllers.RemoveAt(controllers.Count - 1);
                    }
                    break;
                case IPresenter<Drawable> presenter:
                    if (presenter.Target.CurrentValue is { } presented) CollectFrom(presented);
                    break;
            }
            path.Remove(obj);
        }
    }

    public static TimeSpan ClampSharedDelta(IReadOnlyList<Target> targets, TimeSpan delta)
    {
        if (targets.Count == 0) return TimeSpan.Zero;
        TimeSpan previous;
        do
        {
            previous = delta;
            foreach (Target target in targets)
                delta = ClampDelta(delta, target.CanSlip);
            // A loop is not monotonic. Another stream's tighter bound can move an
            // earlier stream off a valid loop boundary, so recheck the shared delta.
        } while (delta != TimeSpan.Zero && delta != previous);
        return delta;
    }

    // Plan every write before mutating anything. A shared source referenced through
    // incompatible clocks cannot accept two different offsets in a single operation.
    public static bool TryGetOffsetChanges(IEnumerable<Target> targets, TimeSpan delta, bool trim,
        out Dictionary<IProperty<TimeSpan>, TimeSpan> changes)
    {
        changes = new();
        foreach (Target target in targets)
        {
            TimeSpan change = target.SourceDelta(delta, trim);
            if (changes.TryGetValue(target.Offset, out TimeSpan existing) && existing != change)
                return false;
            changes[target.Offset] = change;
        }
        return true;
    }

    public static void ApplyOffsetChanges(Dictionary<IProperty<TimeSpan>, TimeSpan> changes)
    {
        foreach ((IProperty<TimeSpan> offset, TimeSpan delta) in changes)
            if (delta != TimeSpan.Zero) offset.CurrentValue += delta;
    }

    public static TimeSpan OutPointRoom(IReadOnlyList<Target> targets, TimeSpan elementLength, TimeSpan limit)
    {
        foreach (Target target in targets)
            limit = ClampDelta(limit, delta => target.Fits(elementLength + delta, allowRecovery: true));
        return limit;
    }

    public static TimeSpan ClampInPointDelta(IReadOnlyList<Target> targets, TimeSpan delta, bool clampEnd)
    {
        foreach (Target target in targets)
            delta = ClampDelta(delta, d => target.CanTrim(d, clampEnd));
        return delta;
    }

    internal sealed class ResizeConstraints(TimeSpan elementStart, TimeSpan elementLength, List<Target> targets)
    {
        public TimeSpan ClampStart(TimeSpan requestedStart)
        {
            if (requestedStart > elementStart + elementLength) requestedStart = elementStart + elementLength;
            TimeSpan delta = requestedStart - elementStart;
            return elementStart + ClampDelta(delta,
                d => targets.All(t => t.Fits(elementLength - d, d, allowRecovery: true)));
        }

        public TimeSpan ClampLength(TimeSpan requestedLength, TimeSpan? start = null)
        {
            TimeSpan startDelta = (start ?? elementStart) - elementStart;
            TimeSpan initialLength = startDelta == TimeSpan.Zero ? elementLength : TimeSpan.Zero;
            // A loop period can depend on the new element length. Validate each
            // requested duration even if it is below a previously valid maximum.
            return initialLength + ClampDelta(requestedLength - initialLength,
                d => targets.All(t => t.Fits(initialLength + d, startDelta, allowRecovery: true)));
        }

        // Null means unbounded; zero means a known source is exhausted.
        public TimeSpan? GetMaximumDuration(TimeSpan? start = null)
        {
            if (targets.All(t => t.Total == null)) return null;
            TimeSpan startDelta = (start ?? elementStart) - elementStart;
            bool Fits(TimeSpan length) => targets.All(t => t.Fits(length, startDelta));
            if (!Fits(TimeSpan.Zero)) return TimeSpan.Zero;

            long maximum = long.MaxValue - Math.Max(0, (start ?? elementStart).Ticks);
            long high = Math.Min(TimeSpan.TicksPerSecond, maximum);
            while (Fits(TimeSpan.FromTicks(high)))
            {
                if (high == maximum) return null;
                high = high > maximum / 2 ? maximum : high * 2;
            }
            return ClampDelta(TimeSpan.FromTicks(high), Fits);
        }
    }

    public static ResizeConstraints CreateResizeConstraints(Element element)
        => new(element.Start, element.Length, Collect(element));

    public static TimeSpan? GetMaximumDuration(Element element, TimeSpan? start = null)
        => CreateResizeConstraints(element).GetMaximumDuration(start);

    internal static TimeSpan ClampDelta(TimeSpan requested, Func<TimeSpan, bool> fits)
    {
        if (requested == TimeSpan.Zero || fits(requested)) return requested;
        long low = 0;
        long high = requested.Ticks == long.MinValue ? long.MaxValue : Math.Abs(requested.Ticks);
        long sign = requested < TimeSpan.Zero ? -1 : 1;
        while (high - low > 1)
        {
            long mid = low + (high - low) / 2;
            if (fits(TimeSpan.FromTicks(sign * mid))) low = mid;
            else high = mid;
        }
        return TimeSpan.FromTicks(sign * low);
    }
}
