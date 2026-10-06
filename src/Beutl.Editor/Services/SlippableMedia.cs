using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

internal static partial class SlippableMedia
{
    private sealed record Node(EngineObject Object, IReadOnlyList<Node> Inputs, bool Opaque = false, bool FlowResolved = false, bool PortalInput = false);

    internal sealed class Target(IProperty<TimeSpan> offset, TimeSpan? total, MediaTimeMapping mapping, TimeSpan length)
    {
        public IProperty<TimeSpan> Offset { get; } = offset;
        public TimeSpan? Total { get; } = total;
        public TimeSpan InitialOffset { get; } = offset.CurrentValue;
        public MediaTimeMapping Mapping { get; } = mapping;
        public TimeSpan Length { get; } = length;

        public TimeSpan SourceDelta(TimeSpan delta, bool trim)
            => Mapping.Range(delta, delta, extrapolate: delta < TimeSpan.Zero, conservative: false, sourceOffset: true).Min
                - Mapping.Range(TimeSpan.Zero, TimeSpan.Zero, trim ? delta : TimeSpan.Zero,
                    trim ? -delta : TimeSpan.Zero, conservative: false, sourceOffset: true).Min;

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
            MediaTimeMapping.Interval window = InitialWindow();
            (TimeSpan lower, TimeSpan? upper) = SourceLimits(clampEnd: true, allowRecovery: true);
            TimeSpan minimum = lower - window.Min;
            if (minimum < -InitialOffset) minimum = -InitialOffset;
            TimeSpan maximum = upper is { } end ? end - window.Max : TimeSpan.MaxValue;
            if (minimum > maximum) return TimeSpan.Zero;
            var valid = new MediaTimeMapping.Interval(minimum, maximum).Shift(
                Mapping.Range(TimeSpan.Zero, TimeSpan.Zero, conservative: false, sourceOffset: true).Min);
            long sign = requested < TimeSpan.Zero ? -1 : 1;
            long magnitude = requested.Ticks == long.MinValue ? long.MaxValue : Math.Abs(requested.Ticks);
            return Find(0, magnitude) ?? TimeSpan.Zero;

            TimeSpan? Find(long low, long high)
            {
                TimeSpan atHigh = TimeSpan.FromTicks(sign * high);
                if (CanSlip(atHigh)) return atHigh;
                TimeSpan atLow = TimeSpan.FromTicks(sign * low);
                MediaTimeMapping.Interval range = Mapping.Range(atLow, atHigh, extrapolate: sign < 0, sourceOffset: true);
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
            if (Mapping.SampledRange(TimeSpan.Zero, length, startDelta, length - Length) is not { } sampled) return true;
            MediaTimeMapping.Interval range = sampled.Shift(offset);
            if (allowRecovery)
            {
                MediaTimeMapping.Interval nominal = Mapping.SampledRange(TimeSpan.Zero, length, startDelta,
                    length - Length, conservative: false)!.Value.Shift(offset);
                MediaTimeMapping.Interval before = InitialWindow(conservative: false);
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
                MediaTimeMapping.Interval before = InitialWindow();
                // Admit the current numerical allowance while the separate nominal
                // check prevents any additional nominal overrun.
                if (before.Min < lower) lower = before.Min;
                if (upper is { } limit && before.Max > limit) upper = before.Max;
            }
            return (lower, upper);
        }

        private MediaTimeMapping.Interval InitialWindow(bool conservative = true)
            => Mapping.SampledRange(TimeSpan.Zero, Length, conservative: conservative)?.Shift(InitialOffset)
                ?? new MediaTimeMapping.Interval(TimeSpan.Zero, TimeSpan.Zero);
    }

    public static List<Target> Collect(Element element, IReadOnlySet<Element>? timingPeers = null, bool ignoreLoops = false,
        IReadOnlyDictionary<Element, MediaTimeMapping.TrimRole>? timingRoles = null, IReadOnlySet<Element>? portalCandidates = null)
        => new TargetCollector(element, timingPeers, ignoreLoops, timingRoles, portalCandidates).Collect();

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

    public static ResizeConstraints CreateResizeConstraints(Element element, IReadOnlySet<Element>? timingPeers = null)
    {
        List<Target> targets = Collect(element, timingPeers);
        TimeSpan? duration = GetProviderDuration(element);
        if (element.HierarchicalParent is not Scene scene || !element.Objects.OfType<PortalObject>().Any(p => p.IsEnabled))
            return new(element.Start, element.Length, targets, duration);

        TimeRange original = element.Range;
        var ranges = scene.Children.Select(child => (Element: child, child.Range)).ToArray();
        HashSet<Element> Active(TimeSpan length, TimeSpan startDelta)
        {
            var window = new TimeRange(original.Start + startDelta, length);
            return ranges.Where(entry =>
            {
                TimeRange range = entry.Range;
                if (entry.Element == element || timingPeers?.Contains(entry.Element) == true)
                    range = new TimeRange(range.Start + startDelta, range.Duration + length - original.Duration);
                return range.Start < window.End && window.Start < range.End;
            }).Select(entry => entry.Element).ToHashSet();
        }

        // Only a change in active providers needs a new flow/clock snapshot, not
        // every bisection tick. Keep all discovered clocks for loop-phase searches.
        var cache = new List<(HashSet<Element> Active, List<Target> Targets)> { (Active(original.Duration, TimeSpan.Zero), [.. targets]) };
        List<Target> Resolve(TimeSpan length, TimeSpan startDelta)
        {
            HashSet<Element> active = Active(length, startDelta);
            foreach (var entry in cache)
                if (entry.Active.SetEquals(active)) return entry.Targets;
            List<Target> collected = Collect(element, timingPeers, portalCandidates: active);
            cache.Add((active, collected));
            targets.AddRange(collected);
            return collected;
        }
        return new(original.Start, original.Duration, targets, duration, Resolve);
    }

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
    {
        List<Target> targets = Collect(element);
        return targets.All(t => t.Mapping.IsSupported)
            && (element.HasOriginalDuration() || targets.Any(t => t.Total.HasValue));
    }

    public static TimeSpan? GetOriginalDuration(Element element)
    {
        List<Target> mapped = Collect(element);
        if (mapped.Any(t => !t.Mapping.IsSupported)) return null;
        TimeSpan? maximum = new ResizeConstraints(element.Start, element.Length, mapped,
            GetProviderDuration(element)).GetMaximumDuration();
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
