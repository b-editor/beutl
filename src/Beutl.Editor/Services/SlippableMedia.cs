using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
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

        public bool IsSupported => SampleMappings.All(m => m.IsSupported);
        public bool CanWriteOffsets => IsSupported && Mapping.CanWriteOffsets;
        public bool HasVariableDuration => SampleMappings.Any(m => m.HasVariableDuration);
        public bool HasSharedClock => SampleMappings.Any(m => m.HasSharedClock);

        public MediaTime SourceDelta(MediaTime delta, bool trim)
            => Mapping.Range(delta, delta, extrapolate: delta < MediaTime.Zero, conservative: false, sourceOffset: true).Min
                - Mapping.Range(MediaTime.Zero, MediaTime.Zero, trim ? delta : MediaTime.Zero,
                    trim ? -delta : MediaTime.Zero, conservative: false, sourceOffset: true).Min;

        public MediaTime ContextTime(MediaTime localTime)
            => Mapping.MapControllerTime(localTime);

        public MediaTime SourcePosition(MediaTime localTime, bool wrapVideo)
            => Mapping.Range(localTime, localTime, extrapolate: false, conservative: false,
                sourceOffset: !wrapVideo).Min;

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
                if (sampleMapping.SampledRange(MediaTime.Zero, length, startDelta, length - Length) is not { } sampled) continue;
                MediaTimeMapping.Interval range = sampled.Shift(offset);
                if (allowRecovery)
                {
                    MediaTimeMapping.Interval nominal = sampleMapping.SampledRange(MediaTime.Zero, length, startDelta,
                        length - Length, conservative: false)!.Value.Shift(offset);
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
            => sampleMapping.SampledRange(MediaTime.Zero, Length, conservative: conservative)?.Shift(InitialOffset)
                ?? new MediaTimeMapping.Interval(MediaTime.Zero, MediaTime.Zero);
    }

    public static List<Target> Collect(Element element, IReadOnlySet<Element>? timingPeers = null, bool ignoreLoops = false,
        IReadOnlyDictionary<Element, MediaTimeMapping.TrimRole>? timingRoles = null, SplitBatch? batch = null)
        => new TargetCollector(element, timingPeers, ignoreLoops, timingRoles, batch: batch).Collect();

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

    public static ResizeConstraints CreateResizeConstraints(Element element, IReadOnlySet<Element>? timingPeers = null)
        => new(element.Start.Value, element.Length.Value, CollectResizeTargets(element, timingPeers), GetProviderDuration(element),
            sharedClock: element.Objects.OfType<PortalObject>().Any(p => p.IsEnabled.Value));

    private static List<Target> CollectResizeTargets(Element element, IReadOnlySet<Element>? timingPeers = null, bool ignoreLoops = false)
        => new TargetCollector(element, timingPeers, ignoreLoops, null, forResize: true).Collect();

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
        return CollectResizeTargets(element).Any(t => t.Total.HasValue) || GetProviderDuration(element).HasValue;
    }

    public static MediaTime? GetOriginalDuration(Element element)
    {
        List<Target> mapped = CollectResizeTargets(element);
        MediaTime? maximum = new ResizeConstraints(element.Start.Value, element.Length.Value, mapped,
            GetProviderDuration(element)).GetMaximumDuration();
        if (maximum.HasValue) return maximum;
        // Repetition can be unbounded while the underlying source still has an
        // original length. Offer one cycle; a frozen source falls back to source time.
        List<Target> targets = CollectResizeTargets(element, ignoreLoops: true);
        MediaTime? duration = new ResizeConstraints(element.Start.Value, element.Length.Value, targets,
            GetProviderDuration(element)).GetMaximumDuration();
        if (duration.HasValue) return duration;
        foreach (Target target in targets)
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
