using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

internal static partial class SlippableMedia
{
    private sealed record Node(EngineObject Object, IReadOnlyList<Node> Inputs, bool Opaque = false, bool FlowResolved = false, bool PortalInput = false);

    internal sealed class Target(IProperty<MediaTime> offset, MediaTime? total, MediaTimeMapping mapping, MediaTime length)
    {
        public IProperty<MediaTime> Offset { get; } = offset;
        public MediaTime? Total { get; } = total;
        public MediaTime InitialOffset { get; } = offset.Value;
        public MediaTimeMapping Mapping { get; } = mapping;
        public List<MediaTimeMapping> SampleMappings { get; } = [mapping];
        public MediaTime Length { get; } = length;
        private TimeRange? Window { get; init; }

        public Target WithWindow(TimeRange window)
        {
            var target = new Target(Offset, Total, Mapping, Length) { Window = window };
            target.SampleMappings.Clear();
            target.SampleMappings.AddRange(SampleMappings);
            return target;
        }

        public bool IsSupported => SampleMappings.All(m => m.IsSupported);
        public bool CanWriteOffsets => IsSupported && Mapping.CanWriteOffsets;
        public bool HasVariableDuration => SampleMappings.Any(m => m.HasVariableDuration);
        public bool HasSharedClock => SampleMappings.Any(m => m.HasSharedClock);

        public MediaTime SourceDelta(MediaTime delta, bool trim)
            => Mapping.Range(delta, delta, extrapolate: delta < MediaTime.Zero, conservative: false, sourceOffset: true).Min
                - Mapping.Range(MediaTime.Zero, MediaTime.Zero, trim ? delta : MediaTime.Zero,
                    trim ? -delta : MediaTime.Zero, conservative: false, sourceOffset: true).Min;

        public bool CanSlip(MediaTime delta)
        {
            if (!IsSupported) return false;
            MediaTime change = SourceDelta(delta, trim: false);
            return Fits(Length, MediaTime.Zero, change, clampEnd: true, allowRecovery: true);
        }

        public bool CanTrim(MediaTime delta, bool clampEnd)
            => IsSupported && Fits(Length - delta, delta, SourceDelta(delta, trim: true), clampEnd, allowRecovery: true);

        public MediaTime ClampSlip(MediaTime requested)
        {
            if (!IsSupported) return MediaTime.Zero;
            if (requested == MediaTime.Zero || CanSlip(requested)) return requested;
            MediaTime minimum = -InitialOffset;
            MediaTime maximum = MediaTime.MaxValue;
            foreach (MediaTimeMapping sampleMapping in SampleMappings)
            {
                MediaTimeMapping.Interval window = InitialWindow(sampleMapping);
                (MediaTime lower, MediaTime? upper) = SourceLimits(sampleMapping, clampEnd: true, allowRecovery: true);
                if (lower - window.Min > minimum) minimum = lower - window.Min;
                if (upper is { } end && end - window.Max < maximum) maximum = end - window.Max;
            }
            if (minimum > maximum) return MediaTime.Zero;
            var valid = new MediaTimeMapping.Interval(minimum, maximum).Shift(
                Mapping.Range(MediaTime.Zero, MediaTime.Zero, conservative: false, sourceOffset: true).Min);
            long sign = requested < MediaTime.Zero ? -1 : 1;
            long magnitude = requested.Flicks == long.MinValue ? long.MaxValue : Math.Abs(requested.Flicks);
            return Find(0, magnitude) ?? MediaTime.Zero;

            MediaTime? Find(long low, long high)
            {
                MediaTime atHigh = new MediaTime(sign * high);
                if (CanSlip(atHigh)) return atHigh;
                MediaTime atLow = new MediaTime(sign * low);
                MediaTimeMapping.Interval range = Mapping.Range(atLow, atHigh, extrapolate: sign < 0, sourceOffset: true);
                if (range.Max < valid.Min || range.Min > valid.Max || high - low <= 1) return null;
                long middle = low + (high - low) / 2;
                // Prune whole invalid source-time ranges, searching nearest the
                // requested end first. A failed midpoint must not discard later loops.
                return Find(middle, high) ?? Find(low, middle);
            }
        }

        public bool Fits(MediaTime length, MediaTime startDelta = default, MediaTime offsetDelta = default,
            bool clampEnd = true, bool allowRecovery = false)
        {
            if (!IsSupported) return false;
            MediaTime offset = InitialOffset + offsetDelta;
            if (offset < MediaTime.Zero) return false;
            foreach (MediaTimeMapping sampleMapping in SampleMappings)
            {
                if (sampleMapping.SampledRange(MediaTime.Zero, length, startDelta, length - Length, window: Window) is not { } sampled) continue;
                MediaTimeMapping.Interval range = sampled.Shift(offset);
                if (allowRecovery)
                {
                    MediaTimeMapping.Interval nominal = sampleMapping.SampledRange(MediaTime.Zero, length, startDelta,
                        length - Length, conservative: false, window: Window)!.Value.Shift(offset);
                    MediaTimeMapping.Interval before = InitialWindow(sampleMapping, conservative: false);
                    MediaTime minimum = before.Min < MediaTime.Zero ? before.Min : MediaTime.Zero;
                    MediaTime? maximum = clampEnd ? Total : null;
                    if (maximum is { } limit && before.Max > limit) maximum = before.Max;
                    if (nominal.Min < minimum || maximum is { } end && nominal.Max > end) return false;
                }
                (MediaTime lower, MediaTime? upper) = SourceLimits(sampleMapping, clampEnd, allowRecovery);
                if (range.Min < lower || upper is { } sourceEnd && range.Max > sourceEnd) return false;
            }
            return true;
        }

        private (MediaTime Lower, MediaTime? Upper) SourceLimits(MediaTimeMapping sampleMapping, bool clampEnd, bool allowRecovery)
        {
            MediaTime lower = MediaTime.Zero;
            MediaTime? upper = clampEnd ? Total : null;
            if (allowRecovery)
            {
                // Existing projects may already be out of range. Permit a trim/slip
                // towards valid media without granting any additional overrun.
                MediaTimeMapping.Interval before = InitialWindow(sampleMapping);
                // Admit the current numerical allowance while the separate nominal
                // check prevents any additional nominal overrun.
                if (before.Min < lower) lower = before.Min;
                if (upper is { } limit && before.Max > limit) upper = before.Max;
            }
            return (lower, upper);
        }

        private MediaTimeMapping.Interval InitialWindow(MediaTimeMapping sampleMapping, bool conservative = true)
            => sampleMapping.SampledRange(MediaTime.Zero, Length, conservative: conservative, window: Window)?.Shift(InitialOffset)
                ?? new MediaTimeMapping.Interval(MediaTime.Zero, MediaTime.Zero);
    }

    public static List<Target> Collect(Element element, IReadOnlySet<Element>? timingPeers = null, bool ignoreLoops = false,
        IReadOnlyDictionary<Element, MediaTimeMapping.TrimRole>? timingRoles = null, IReadOnlySet<Element>? portalCandidates = null,
        bool includePortalInputs = true)
        => new TargetCollector(element, timingPeers, ignoreLoops, timingRoles, portalCandidates, includePortalInputs).Collect();

    public static MediaTime ClampSharedDelta(IReadOnlyList<Target> targets, MediaTime delta)
    {
        if (targets.Count == 0 || targets.Any(t => !t.CanWriteOffsets)) return MediaTime.Zero;
        MediaTime previous;
        do
        {
            previous = delta;
            foreach (Target target in targets)
                delta = target.ClampSlip(delta);
            // A loop is not monotonic. Another stream's tighter bound can move an
            // earlier stream off a valid loop boundary, so recheck the shared delta.
        } while (delta != MediaTime.Zero && delta != previous);
        return delta;
    }

    // Plan every write before mutating anything. A shared source referenced through
    // incompatible clocks cannot accept two different offsets in a single operation.
    public static bool TryGetOffsetChanges(IEnumerable<Target> targets, MediaTime delta, bool trim,
        out Dictionary<IProperty<MediaTime>, MediaTime> changes)
    {
        changes = new();
        foreach (Target target in targets)
        {
            if (!target.CanWriteOffsets) return false;
            MediaTime change = target.SourceDelta(delta, trim);
            if (changes.TryGetValue(target.Offset, out MediaTime existing) && existing != change)
                return false;
            changes[target.Offset] = change;
        }
        return true;
    }

    public static void ApplyOffsetChanges(Dictionary<IProperty<MediaTime>, MediaTime> changes)
    {
        foreach ((IProperty<MediaTime> offset, MediaTime delta) in changes)
            if (delta != MediaTime.Zero) offset.Value += delta;
    }

    public static MediaTime OutPointRoom(IReadOnlyList<Target> targets, MediaTime elementLength, MediaTime limit)
    {
        foreach (Target target in targets)
            limit = ClampDelta(limit, delta => target.Fits(elementLength + delta, allowRecovery: true));
        return limit;
    }

    public static MediaTime ClampInPointDelta(IReadOnlyList<Target> targets, MediaTime delta, bool clampEnd)
    {
        foreach (Target target in targets)
            delta = ClampDelta(delta, d => target.CanTrim(d, clampEnd));
        return delta;
    }

    public static ResizeConstraints CreateResizeConstraints(Element element, IReadOnlySet<Element>? timingPeers = null, bool ignoreLoops = false)
    {
        MediaTime? duration = GetProviderDuration(element);
        if (element.HierarchicalParent is not Scene scene || !element.Objects.OfType<PortalObject>().Any(p => p.IsEnabled.Value))
            return new(element.Start.Value, element.Length.Value, Collect(element, timingPeers, ignoreLoops), duration);

        TimeRange original = element.Range;
        var ranges = scene.Children.Select(child => (Element: child, child.Range)).ToArray();
        var targets = new List<Target>();
        var sourceDurations = new Dictionary<MediaSource, MediaTime?>(ReferenceEqualityComparer.Instance);
        var cache = new List<(HashSet<Element> Active, List<Target> Targets)>();
        List<Target> Snapshot(HashSet<Element> active)
        {
            foreach (var entry in cache)
                if (entry.Active.SetEquals(active)) return entry.Targets;
            List<Target> collected = new TargetCollector(element, timingPeers, ignoreLoops, null, active,
                includePortalInputs: false, sourceDurations: sourceDurations).Collect();
            cache.Add((active, collected));
            targets.AddRange(collected);
            return collected;
        }

        // Flow is selected before controllers remap time. Rebuild it at provider
        // boundaries, including gaps, and use each clock only while it is active.
        // Cache the clocks by active set; changing geometry only clips their windows.
        List<Target> Resolve(MediaTime length, MediaTime startDelta)
        {
            Int128 windowStart = (Int128)original.Start.Flicks + startDelta.Flicks;
            Int128 windowEnd = windowStart + length.Flicks;
            var shifted = ranges.Select(entry =>
            {
                Int128 start = entry.Range.Start.Flicks;
                Int128 end = start + entry.Range.Duration.Flicks;
                if (entry.Element == element || timingPeers?.Contains(entry.Element) == true)
                {
                    start += startDelta.Flicks;
                    end += (Int128)startDelta.Flicks + length.Flicks - original.Duration.Flicks;
                }
                return (entry.Element, Start: start, End: end);
            }).Where(entry => entry.Start < entry.End && entry.Start < windowEnd && windowStart < entry.End).ToArray();
            var boundaries = new SortedSet<long> { 0, length.Flicks };
            foreach (var entry in shifted)
            {
                if (entry.Start > windowStart) boundaries.Add((long)(entry.Start - windowStart));
                if (entry.End < windowEnd) boundaries.Add((long)(entry.End - windowStart));
            }
            long[] points = boundaries.ToArray();
            var result = new List<Target>();
            for (int i = 1; i < points.Length; i++)
            {
                Int128 from = windowStart + points[i - 1];
                HashSet<Element> active = shifted.Where(entry => entry.Start <= from && from < entry.End)
                    .Select(entry => entry.Element).ToHashSet();
                var window = new TimeRange(new MediaTime((long)from), new MediaTime(points[i] - points[i - 1]));
                foreach (Target target in Snapshot(active)) result.Add(target.WithWindow(window));
            }
            return result;
        }
        Resolve(original.Duration, MediaTime.Zero);
        return new(original.Start, original.Duration, targets, duration, Resolve);
    }

    public static MediaTime ClampSharedResizeDelta(IReadOnlyList<ResizeConstraints> constraints, MediaTime delta, bool leftEdge)
    {
        MediaTime previous;
        do
        {
            previous = delta;
            foreach (ResizeConstraints constraint in constraints)
                delta = constraint.ClampEdgeDelta(delta, leftEdge);
            // A peer's tighter limit can select an unsafe intermediate loop phase.
            // Keep the shared edge and all clocks on the same validated delta.
        } while (delta != MediaTime.Zero && delta != previous);
        return delta;
    }

    public static bool HasOriginalDuration(Element element)
    {
        ResizeConstraints constraints = CreateResizeConstraints(element);
        return constraints.IsSupported
            && (element.HasOriginalDuration() || constraints.MediaTargets.Any(t => t.Total.HasValue));
    }

    public static MediaTime? GetOriginalDuration(Element element)
    {
        ResizeConstraints constraints = CreateResizeConstraints(element);
        if (!constraints.IsSupported) return null;
        MediaTime? maximum = constraints.GetMaximumDuration();
        if (maximum.HasValue) return maximum;
        // Repetition can be unbounded while the underlying source still has an
        // original length. Offer one cycle; a frozen source falls back to source time.
        constraints = CreateResizeConstraints(element, ignoreLoops: true);
        if (!constraints.IsSupported) return null;
        MediaTime? duration = constraints.GetMaximumDuration();
        if (duration.HasValue) return duration;
        foreach (Target target in constraints.MediaTargets)
        {
            if (target.Total is not { } total) continue;
            MediaTime remaining = total > target.InitialOffset ? total - target.InitialOffset : MediaTime.Zero;
            if (duration == null || remaining < duration) duration = remaining;
        }
        return duration;
    }

    private static MediaTime? GetProviderDuration(Element element)
    {
        MediaTime? duration = null;
        foreach (EngineObject obj in element.Objects)
        {
            if (obj is SourceVideo or SourceSound || obj is not IOriginalDurationProvider provider) continue;
            if (provider.HasOriginalDuration() && provider.TryGetOriginalDuration(out MediaTime value) && value >= MediaTime.Zero
                && (duration == null || value < duration)) duration = value;
        }
        return duration;
    }

    internal static MediaTime ClampDelta(MediaTime requested, Func<MediaTime, bool> fits)
    {
        if (requested == MediaTime.Zero || fits(requested)) return requested;
        long low = 0;
        long high = requested.Flicks == long.MinValue ? long.MaxValue : Math.Abs(requested.Flicks);
        long sign = requested < MediaTime.Zero ? -1 : 1;
        while (high - low > 1)
        {
            long mid = low + (high - low) / 2;
            if (fits(new MediaTime(sign * mid))) low = mid;
            else high = mid;
        }
        return new MediaTime(sign * low);
    }
}
