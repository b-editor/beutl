using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

internal static class SlippableMedia
{
    private sealed record Node(EngineObject Object, IReadOnlyList<Node> Inputs, bool Opaque = false, bool FlowResolved = false);

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
            if (!Mapping.IsSupported) return false;
            TimeSpan change = SourceDelta(delta, trim: false);
            return Fits(Length, TimeSpan.Zero, change, clampEnd: true, allowRecovery: true);
        }

        public bool CanTrim(TimeSpan delta, bool clampEnd)
            => Mapping.IsSupported && Fits(Length - delta, delta, SourceDelta(delta, trim: true), clampEnd, allowRecovery: true);

        public TimeSpan ClampSlip(TimeSpan requested)
        {
            if (!Mapping.IsSupported) return TimeSpan.Zero;
            if (requested == TimeSpan.Zero || CanSlip(requested)) return requested;
            MediaTimeMapping.Interval window = Mapping.Range(TimeSpan.Zero, Length).Shift(InitialOffset);
            (TimeSpan lower, TimeSpan? upper) = SourceLimits(clampEnd: true, allowRecovery: true);
            TimeSpan minimum = lower - window.Min;
            if (minimum < -InitialOffset) minimum = -InitialOffset;
            TimeSpan maximum = upper is { } end ? end - window.Max : TimeSpan.MaxValue;
            if (minimum > maximum) return TimeSpan.Zero;
            var valid = new MediaTimeMapping.Interval(minimum, maximum).Shift(Mapping.At(TimeSpan.Zero));
            long sign = requested < TimeSpan.Zero ? -1 : 1;
            long magnitude = requested.Ticks == long.MinValue ? long.MaxValue : Math.Abs(requested.Ticks);
            return Find(0, magnitude) ?? TimeSpan.Zero;

            TimeSpan? Find(long low, long high)
            {
                TimeSpan atHigh = TimeSpan.FromTicks(sign * high);
                if (CanSlip(atHigh)) return atHigh;
                TimeSpan atLow = TimeSpan.FromTicks(sign * low);
                MediaTimeMapping.Interval range = Mapping.Range(atLow, atHigh, extrapolate: sign < 0);
                if (range.Max < valid.Min || range.Min > valid.Max || high - low <= 1) return null;
                long middle = low + (high - low) / 2;
                // Prune whole invalid source-time ranges, searching nearest the
                // requested end first. A failed midpoint must not discard later loops.
                return Find(middle, high) ?? Find(low, middle);
            }
        }

        public bool Fits(TimeSpan length, TimeSpan startDelta = default, TimeSpan offsetDelta = default,
            bool clampEnd = true, bool allowRecovery = false)
        {
            if (!Mapping.IsSupported) return false;
            TimeSpan offset = InitialOffset + offsetDelta;
            if (offset < TimeSpan.Zero) return false;
            MediaTimeMapping.Interval range = Mapping.Range(TimeSpan.Zero, length, startDelta, length - Length).Shift(offset);
            if (allowRecovery)
            {
                MediaTimeMapping.Interval nominal = Mapping.Range(TimeSpan.Zero, length, startDelta,
                    length - Length, conservative: false).Shift(offset);
                MediaTimeMapping.Interval before = Mapping.Range(TimeSpan.Zero, Length, conservative: false).Shift(InitialOffset);
                TimeSpan minimum = before.Min < TimeSpan.Zero ? before.Min : TimeSpan.Zero;
                TimeSpan? maximum = clampEnd ? Total : null;
                if (maximum is { } limit && before.Max > limit) maximum = before.Max;
                if (nominal.Min < minimum || maximum is { } end && nominal.Max > end) return false;
            }
            (TimeSpan lower, TimeSpan? upper) = SourceLimits(clampEnd, allowRecovery);
            return range.Min >= lower && (upper == null || range.Max <= upper);
        }

        private (TimeSpan Lower, TimeSpan? Upper) SourceLimits(bool clampEnd, bool allowRecovery)
        {
            TimeSpan lower = TimeSpan.Zero;
            TimeSpan? upper = clampEnd ? Total : null;
            if (allowRecovery)
            {
                // Existing projects may already be out of range. Permit a trim/slip
                // towards valid media without granting any additional overrun.
                MediaTimeMapping.Interval before = Mapping.Range(TimeSpan.Zero, Length).Shift(InitialOffset);
                // Admit the current numerical allowance while the separate nominal
                // check prevents any additional nominal overrun.
                if (before.Min < lower) lower = before.Min;
                if (upper is { } limit && before.Max > limit) upper = before.Max;
            }
            return (lower, upper);
        }
    }

    public static List<Target> Collect(Element element, IReadOnlySet<Element>? timingPeers = null, bool ignoreLoops = false,
        IReadOnlyDictionary<Element, MediaTimeMapping.TrimRole>? timingRoles = null)
    {
        var targets = new List<Target>();
        var path = new HashSet<object>();
        var controllers = new List<MediaTimeMapping.ControllerLink>();
        Project? project = element.FindHierarchicalParent<Project>();
        int sampleRate = project?.Variables.TryGetValue(ProjectVariableKeys.SampleRate, out string? value) == true
            && int.TryParse(value, out int rate) && rate > 0 ? rate : 44100;

        var flow = new List<Node>();
        var consumedPortalElements = new HashSet<Element>();
        bool portalFlow = false;
        bool opaqueConsumption = false;
        foreach (EngineObject obj in element.Objects)
        {
            if (obj is PortalObject { IsEnabled: true } portal)
            {
                portalFlow = true;
                if (portal.Clear.CurrentValue || portal.Clear.HasExpression) flow.Clear();
                List<Node>? imported = portal.Clear.HasExpression ? null : ResolvePortal(portal);
                if (imported == null)
                {
                    flow.Add(new Node(portal, [], Opaque: true));
                    opaqueConsumption = true;
                }
                else flow.AddRange(imported);
                flow.Add(new Node(portal, []));
                continue;
            }
            IReadOnlyList<Node> inputs = [];
            if (obj is DrawableTimeController { IsEnabled: true })
            {
                int index = flow.FindIndex(node => node.Opaque || node.Object is Drawable { IsEnabled: true });
                if (index >= 0)
                {
                    inputs = [flow[index]];
                    flow.RemoveAt(index);
                }
            }
            else if (obj.IsEnabled && obj is SoundGroup or DrawableGroup or DrawableDecorator)
            {
                bool Consumes(Node node) => node.Opaque || node.Object.IsEnabled
                    && (obj is SoundGroup ? node.Object is Sound : node.Object is Drawable);
                inputs = flow.FindAll(Consumes);
                flow.RemoveAll(Consumes);
            }
            flow.Add(new Node(obj, inputs, FlowResolved: portalFlow));
        }
        foreach (Node node in flow) CollectNode(node);
        return targets;

        List<Node>? ResolvePortal(PortalObject portal)
        {
            if (portal.Count.HasExpression) return null;
            if (portal.Count.CurrentValue <= 0) return [];
            if (opaqueConsumption || element.HierarchicalParent is not Scene scene) return null;
            int firstLayer = portal.ZIndex + 1;
            int lastLayer = portal.ZIndex + portal.Count.CurrentValue;
            Element[] candidates = scene.Children.Where(candidate => candidate.IsEnabled && candidate.ZIndex >= firstLayer && candidate.ZIndex <= lastLayer
                && !consumedPortalElements.Contains(candidate) && candidate.Objects.Any(obj => obj.IsEnabled)).ToArray();
            if (candidates.Length == 0) return [];
            // A single plain provider has one stable input order whenever it is
            // active. Switching, nested, or competing portals need a richer flow
            // model; never substitute the controller's stored target for them.
            if (candidates.Length != 1) return null;
            Element owner = candidates[0];
            if (owner.Start >= element.Range.End || element.Start >= owner.Range.End) return null;
            if (element.Objects.Count(obj => obj.IsEnabled && obj is DrawableTimeController) > 1
                || element.Objects.Any(obj => obj.IsEnabled && (obj is IPresenter<Drawable> && obj is not DrawableTimeController
                    || obj is IFlowOperator && obj is not DrawableTimeController))) return null;
            EngineObject[] objects = owner.Objects.Where(obj => obj.IsEnabled).ToArray();
            if (objects.Any(obj => obj is IFlowOperator or IPresenter<Drawable> || obj is not Drawable and not Sound)) return null;
            if (scene.Children.Any(other => other.IsEnabled && other != element && other.ZIndex <= element.ZIndex
                && other.Objects.OfType<PortalObject>().Any(prior => prior.IsEnabled
                    && (prior.Count.HasExpression || prior.ZIndex < owner.ZIndex && prior.ZIndex + prior.Count.CurrentValue >= owner.ZIndex))))
                return null;
            consumedPortalElements.Add(owner);
            return objects.Select(obj => new Node(obj, [])).ToList();
        }

        void CollectNode(Node node)
        {
            if (node.Opaque)
            {
                // These unattached properties only carry a rejected constraint;
                // IsSupported prevents them from ever becoming an offset write.
                targets.Add(new Target(Property.Create<TimeSpan>(), null,
                    new MediaTimeMapping(element, node.Object, Property.Create(100f), [], 60), element.Length));
                return;
            }
            CollectFrom(node.Object, node.Inputs, node.FlowResolved);
        }

        // Keep disabled streams in sync too. Detect cycles per path, rather than
        // discarding a second path whose time controller can impose tighter bounds.
        void CollectFrom(EngineObject obj, IReadOnlyList<Node>? inputs = null, bool flowResolved = false)
        {
            if (!path.Add(obj)) return;
            if (inputs != null && obj is SoundGroup or DrawableGroup or DrawableDecorator)
            {
                foreach (Node child in inputs) CollectNode(child);
            }
            switch (obj)
            {
                case SourceVideo video:
                    using (var resource = video.Source.CurrentValue?.ToResource(CompositionContext.Default))
                    {
                        targets.Add(new Target(video.OffsetPosition, resource?.Duration,
                            new MediaTimeMapping(element, video, video.Speed, controllers, 60, resource?.Duration, timingPeers, ignoreLoops, timingRoles), element.Length));
                    }
                    break;
                case SourceSound sound:
                    using (var resource = sound.Source.CurrentValue?.ToResource(CompositionContext.Default))
                    {
                        targets.Add(new Target(sound.OffsetPosition, resource?.Duration > TimeSpan.Zero ? resource.Duration : null,
                            new MediaTimeMapping(element, sound, sound.Speed, controllers, sampleRate, timingPeers: timingPeers, timingRoles: timingRoles), element.Length));
                    }
                    break;
                case SceneSound sound:
                    targets.Add(new Target(sound.OffsetPosition, sound.ReferencedScene.CurrentValue?.Duration,
                        new MediaTimeMapping(element, sound, sound.Speed, controllers, sampleRate, timingPeers: timingPeers, timingRoles: timingRoles), element.Length));
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
                    if (input?.Opaque == true)
                    {
                        CollectNode(input);
                        break;
                    }
                    if (input == null && flowResolved) break;
                    if (input == null && controller.Target.HasExpression)
                    {
                        // Represent an unknown target as an unsupported mapping, even
                        // when its stored target is null, so linked edits remain atomic.
                        // A consumed Flow input overrides Target and needs no such guard.
                        targets.Add(new Target(controller.OffsetPosition, null,
                            new MediaTimeMapping(element, controller, controller.Speed, controllers, 60,
                                timingPeers: timingPeers, timingRoles: timingRoles), element.Length));
                        break;
                    }
                    if ((input?.Object ?? controller.Target.CurrentValue) is Drawable target)
                    {
                        if (controller.IsEnabled) controllers.Add(new MediaTimeMapping.ControllerLink(controller, target));
                        CollectFrom(target, input?.Inputs, input?.FlowResolved == true);
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
        if (targets.Count == 0 || targets.Any(t => !t.Mapping.CanWriteOffsets)) return TimeSpan.Zero;
        TimeSpan previous;
        do
        {
            previous = delta;
            foreach (Target target in targets)
                delta = target.ClampSlip(delta);
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
            if (!target.Mapping.CanWriteOffsets) return false;
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

    internal sealed class ResizeConstraints(TimeSpan elementStart, TimeSpan elementLength, List<Target> targets,
        TimeSpan? providerDuration = null)
    {
        public bool HasMonotonicDuration => targets.All(t => !t.Mapping.HasVariableDuration);

        public bool HasSharedClock => targets.Any(t => t.Mapping.HasSharedClock);

        public TimeSpan ClampEdgeDelta(TimeSpan delta, bool leftEdge)
            => leftEdge ? ClampStart(elementStart + delta) - elementStart
                : ClampLength(elementLength + delta) - elementLength;

        public TimeSpan ClampStart(TimeSpan requestedStart)
        {
            TimeSpan end = elementStart + elementLength;
            if (requestedStart > end) requestedStart = end;
            TimeSpan delta = requestedStart - elementStart;
            bool Fits(TimeSpan length) => FitsProvider(length, allowRecovery: true)
                && targets.All(t => t.Fits(length, elementLength - length, allowRecovery: true));
            TimeSpan result = elementLength - ClampDelta(delta, d => Fits(elementLength - d));
            if (!HasMonotonicDuration)
                result = SearchDurationPhases(result, end - requestedStart, delta, Fits);
            return end - result;
        }

        public TimeSpan ClampLength(TimeSpan requestedLength, TimeSpan? start = null)
        {
            TimeSpan startDelta = (start ?? elementStart) - elementStart;
            TimeSpan initialLength = startDelta == TimeSpan.Zero ? elementLength : TimeSpan.Zero;
            // A loop period can depend on the new element length. Validate each
            // requested duration even if it is below a previously valid maximum.
            bool Fits(TimeSpan length) => FitsProvider(length, allowRecovery: true)
                && targets.All(t => t.Fits(length, startDelta, allowRecovery: true));
            TimeSpan result = initialLength + ClampDelta(requestedLength - initialLength, d => Fits(initialLength + d));
            return HasMonotonicDuration ? result : SearchDurationPhases(result, requestedLength, startDelta, Fits);
        }

        // Null means unbounded; zero means a known source is exhausted.
        public TimeSpan? GetMaximumDuration(TimeSpan? start = null)
        {
            if (targets.Any(t => !t.Mapping.IsSupported)) return TimeSpan.Zero;
            if (providerDuration == null && targets.All(t => t.Total == null)) return null;
            TimeSpan startDelta = (start ?? elementStart) - elementStart;
            bool Fits(TimeSpan length) => FitsProvider(length, allowRecovery: false)
                && targets.All(t => t.Fits(length, startDelta));
            if (!Fits(TimeSpan.Zero)) return TimeSpan.Zero;

            long maximum = long.MaxValue - Math.Max(0, (start ?? elementStart).Ticks);
            long high = Math.Min(TimeSpan.TicksPerSecond, maximum);
            while (Fits(TimeSpan.FromTicks(high)))
            {
                if (high == maximum) return null;
                high = high > maximum / 2 ? maximum : high * 2;
            }
            TimeSpan result = ClampDelta(TimeSpan.FromTicks(high), Fits);
            if (HasMonotonicDuration) return result;
            // A failed probe is not an upper bound when the loop period grows
            // with the element. Also inspect the later start-phase boundaries.
            TimeSpan searchEnd = TimeSpan.FromTicks(high);
            foreach (Target target in targets)
            {
                if (target.Mapping.DurationLoopPhase(startDelta) is not { } phase) continue;
                long ticks = (long)Math.Clamp((decimal)phase.Phase - phase.DurationOffset, 0, maximum);
                if (ticks > searchEnd.Ticks) searchEnd = TimeSpan.FromTicks(ticks);
            }
            return SearchDurationPhases(result, searchEnd, startDelta, Fits);
        }

        internal TimeSpan SearchDurationPhases(TimeSpan baseline, TimeSpan requested, TimeSpan startDelta, Func<TimeSpan, bool> fits)
        {
            if (baseline == requested || fits(requested)) return requested;
            long low = Math.Min(baseline.Ticks, requested.Ticks);
            long high = Math.Max(baseline.Ticks, requested.Ticks);
            var points = new SortedSet<long> { low, high };
            foreach (Target target in targets)
            {
                if (target.Mapping.DurationLoopPhase(startDelta) is not { Phase: > 0 } phase) continue;
                long minimumPeriod = (long)Math.Clamp((decimal)low + phase.DurationOffset, 1, long.MaxValue);
                long maximumPeriod = (long)Math.Clamp((decimal)high + phase.DurationOffset, 1, long.MaxValue);
                long first = Math.Max(1, phase.Phase / maximumPeriod);
                long last = (long)Math.Clamp((decimal)phase.Phase / minimumPeriod + 1, first, long.MaxValue);
                // Bound work even when a large controller offset crosses many
                // periods; direct requested values are always validated first.
                long count = Math.Min(128, last - first);
                for (long index = 0; index <= count; index++)
                {
                    long cycle = count == 0 ? first : first + (long)((decimal)(last - first) * index / count);
                    decimal tick = (decimal)(phase.Phase / cycle) - phase.DurationOffset;
                    for (int adjacent = -1; adjacent <= 1; adjacent++)
                        if (tick + adjacent >= low && tick + adjacent <= high) points.Add((long)(tick + adjacent));
                }
            }
            long[] boundaries = points.ToArray();
            for (int i = 1; i < boundaries.Length; i++)
            {
                long width = boundaries[i] - boundaries[i - 1];
                for (int part = 1; part < 8; part++)
                    points.Add(boundaries[i - 1] + (long)((decimal)width * part / 8));
            }
            IEnumerable<long> ordered = requested > baseline ? points.Reverse() : points;
            TimeSpan previous = requested;
            foreach (long tick in ordered)
            {
                TimeSpan candidate = TimeSpan.FromTicks(tick);
                if (fits(candidate))
                    return candidate + ClampDelta(previous - candidate, d => fits(candidate + d));
                previous = candidate;
            }
            return baseline;
        }

        private bool FitsProvider(TimeSpan length, bool allowRecovery)
            => providerDuration is not { } maximum
                || length <= (allowRecovery && elementLength > maximum ? elementLength : maximum);
    }

    public static ResizeConstraints CreateResizeConstraints(Element element, IReadOnlySet<Element>? timingPeers = null)
        => new(element.Start, element.Length, Collect(element, timingPeers), GetProviderDuration(element));

    public static TimeSpan ClampSharedResizeDelta(IReadOnlyList<ResizeConstraints> constraints, TimeSpan delta, bool leftEdge)
    {
        TimeSpan previous;
        do
        {
            previous = delta;
            foreach (ResizeConstraints constraint in constraints)
                delta = constraint.ClampEdgeDelta(delta, leftEdge);
            // A peer's tighter limit can select an unsafe intermediate loop phase.
            // Keep the shared edge and all clocks on the same validated delta.
        } while (delta != TimeSpan.Zero && delta != previous);
        return delta;
    }

    public static TimeSpan? GetMaximumDuration(Element element, TimeSpan? start = null)
        => CreateResizeConstraints(element).GetMaximumDuration(start);

    public static bool HasOriginalDuration(Element element)
        => element.HasOriginalDuration() || Collect(element).Any(t => t.Total.HasValue);

    public static TimeSpan? GetOriginalDuration(Element element)
    {
        TimeSpan? maximum = CreateResizeConstraints(element).GetMaximumDuration();
        if (maximum.HasValue) return maximum;
        // Repetition can be unbounded while the underlying source still has an
        // original length. Offer one cycle; a frozen source falls back to source time.
        List<Target> targets = Collect(element, ignoreLoops: true);
        TimeSpan? duration = new ResizeConstraints(element.Start, element.Length, targets,
            GetProviderDuration(element)).GetMaximumDuration();
        if (duration.HasValue) return duration;
        foreach (Target target in targets)
        {
            if (target.Total is not { } total) continue;
            TimeSpan remaining = total > target.InitialOffset ? total - target.InitialOffset : TimeSpan.Zero;
            if (duration == null || remaining < duration) duration = remaining;
        }
        return duration;
    }

    private static TimeSpan? GetProviderDuration(Element element)
    {
        TimeSpan? duration = null;
        foreach (EngineObject obj in element.Objects)
        {
            if (obj is SourceVideo or SourceSound || obj is not IOriginalDurationProvider provider) continue;
            if (provider.HasOriginalDuration() && provider.TryGetOriginalDuration(out TimeSpan value) && value >= TimeSpan.Zero
                && (duration == null || value < duration)) duration = value;
        }
        return duration;
    }

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
