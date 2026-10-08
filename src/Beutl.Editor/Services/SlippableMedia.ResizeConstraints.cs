namespace Beutl.Editor.Services;

internal static partial class SlippableMedia
{
    internal sealed class ResizeConstraints(TimeSpan elementStart, TimeSpan elementLength, List<Target> targets,
        TimeSpan? providerDuration = null, Func<TimeSpan, TimeSpan, List<Target>>? targetsAt = null)
    {
        public bool HasMonotonicDuration => targets.All(t => !t.HasVariableDuration);

        public bool HasSharedClock => targetsAt != null || targets.Any(t => t.HasSharedClock);

        private List<Target> Targets(TimeSpan length, TimeSpan startDelta = default)
            => targetsAt?.Invoke(length, startDelta) ?? targets;

        public TimeSpan ClampEdgeDelta(TimeSpan delta, bool leftEdge)
            => leftEdge ? ClampStart(elementStart + delta) - elementStart
                : ClampLength(elementLength + delta) - elementLength;

        public TimeSpan ClampStart(TimeSpan requestedStart)
        {
            TimeSpan end = elementStart + elementLength;
            if (requestedStart > end) requestedStart = end;
            TimeSpan delta = requestedStart - elementStart;
            bool Fits(TimeSpan length) => FitsProvider(length, allowRecovery: true)
                && Targets(length, elementLength - length).All(t => t.Fits(length, elementLength - length, allowRecovery: true));
            TimeSpan result = elementLength - ClampDelta(delta, d => Fits(elementLength - d));
            if (!HasMonotonicDuration)
                result = SearchDurationPhases(result, end - requestedStart, delta, Fits, movingStart: true);
            return end - result;
        }

        public TimeSpan ClampLength(TimeSpan requestedLength, TimeSpan? start = null)
        {
            TimeSpan startDelta = (start ?? elementStart) - elementStart;
            TimeSpan initialLength = startDelta == TimeSpan.Zero ? elementLength : TimeSpan.Zero;
            // A loop period can depend on the new element length. Validate each
            // requested duration even if it is below a previously valid maximum.
            bool Fits(TimeSpan length) => FitsProvider(length, allowRecovery: true)
                && Targets(length, startDelta).All(t => t.Fits(length, startDelta, allowRecovery: true));
            TimeSpan result = initialLength + ClampDelta(requestedLength - initialLength, d => Fits(initialLength + d));
            return HasMonotonicDuration ? result : SearchDurationPhases(result, requestedLength, startDelta, Fits);
        }

        // Null means unbounded; zero means a known source is exhausted.
        public TimeSpan? GetMaximumDuration(TimeSpan? start = null)
        {
            if (Targets(elementLength).Any(t => !t.IsSupported)) return TimeSpan.Zero;
            if (targetsAt == null && providerDuration == null && targets.All(t => t.Total == null)) return null;
            TimeSpan startDelta = (start ?? elementStart) - elementStart;
            bool Fits(TimeSpan length) => FitsProvider(length, allowRecovery: false)
                && Targets(length, startDelta).All(t => t.Fits(length, startDelta));
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
                foreach (MediaTimeMapping mapping in target.SampleMappings)
                {
                    if (mapping.DurationLoopPhase(startDelta) is not { } phase) continue;
                    long ticks = (long)Math.Clamp((decimal)phase.Phase - phase.DurationOffset, 0, maximum);
                    if (ticks > searchEnd.Ticks) searchEnd = TimeSpan.FromTicks(ticks);
                }
            return SearchDurationPhases(result, searchEnd, startDelta, Fits);
        }

        internal TimeSpan SearchDurationPhases(TimeSpan baseline, TimeSpan requested, TimeSpan startDelta, Func<TimeSpan, bool> fits,
            bool movingStart = false)
        {
            if (baseline == requested || fits(requested)) return requested;
            long low = Math.Min(baseline.Ticks, requested.Ticks);
            long high = Math.Max(baseline.Ticks, requested.Ticks);
            var points = new SortedSet<long> { low, high };
            foreach (Target target in targets)
                foreach (MediaTimeMapping mapping in target.SampleMappings)
                {
                    if (movingStart)
                    {
                        AddMovingStartPhases(mapping, low, high, points);
                        continue;
                    }
                    if (mapping.DurationLoopPhase(startDelta) is not { Phase: > 0 } phase) continue;
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

        private void AddMovingStartPhases(MediaTimeMapping mapping, long low, long high, SortedSet<long> points)
        {
            // With a fixed right edge, every duration has a different owner start.
            // Bracket cycle crossings on that moving clock and refine the actual
            // phase equation, instead of reusing the requested start's phase.
            (decimal Phase, decimal Period)? Sample(long length)
            {
                TimeSpan startDelta = elementLength - TimeSpan.FromTicks(length);
                if (mapping.DurationLoopPhase(startDelta) is not { } phase) return null;
                decimal period = (decimal)length + phase.DurationOffset;
                return period > 0 ? (phase.Phase, period) : null;
            }
            void AddBoundary(long length)
            {
                for (int adjacent = -1; adjacent <= 1; adjacent++)
                    if ((decimal)length + adjacent >= low && (decimal)length + adjacent <= high)
                        points.Add(length + adjacent);
            }

            long previous = low;
            var previousPhase = Sample(previous);
            for (int part = 1; part <= 32; part++)
            {
                long next = low + (long)((decimal)(high - low) * part / 32);
                points.Add(next);
                var nextPhase = Sample(next);
                if (previousPhase is { } a && nextPhase is { } b)
                {
                    decimal ratioA = a.Phase / a.Period;
                    decimal ratioB = b.Phase / b.Period;
                    long first = (long)Math.Max(1, Math.Ceiling(Math.Min(ratioA, ratioB)));
                    long last = (long)Math.Floor(Math.Max(ratioA, ratioB));
                    long count = Math.Min(128, last - first);
                    for (long index = 0; index <= count; index++)
                    {
                        long cycle = count == 0 ? first : first + (long)((decimal)(last - first) * index / count);
                        long left = previous;
                        long right = next;
                        decimal valueLeft = a.Phase - cycle * a.Period;
                        decimal valueRight = b.Phase - cycle * b.Period;
                        if (valueLeft == 0) AddBoundary(left);
                        if (valueRight == 0) AddBoundary(right);
                        if (Math.Sign(valueLeft) == Math.Sign(valueRight)) continue;
                        while (right - left > 1)
                        {
                            long middle = left + (right - left) / 2;
                            if (Sample(middle) is not { } atMiddle) break;
                            decimal value = atMiddle.Phase - cycle * atMiddle.Period;
                            if (Math.Sign(value) == Math.Sign(valueLeft)) { left = middle; valueLeft = value; }
                            else right = middle;
                        }
                        AddBoundary(left);
                        AddBoundary(right);
                    }
                }
                previous = next;
                previousPhase = nextPhase;
            }
        }

        private bool FitsProvider(TimeSpan length, bool allowRecovery)
            => providerDuration is not { } maximum
                || length <= (allowRecovery && elementLength > maximum ? elementLength : maximum);
    }
}
