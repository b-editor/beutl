using Beutl.Animation;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

// Shared media-offset primitives for the trim services. Slip shifts the media window
// of a single element; Roll/Slide additionally shift the trimmed neighbour's in-point so
// its content stays anchored across the moving cut. Both need the same source-backed
// media enumeration (including nested Drawable/Sound containers) and the same "one delta
// across every stream" clamping, so it lives here rather than being duplicated per service.
internal static class SlippableMedia
{
    private readonly record struct TimeContext(
        TimeRange Range,
        TimeRange ReachableRange,
        Func<TimeSpan, TimeSpan>? TimelineDurationFromTarget,
        bool HasUnboundedTail,
        bool IsReversed,
        bool AffectsOffset);

    private readonly record struct TimeMappingTargetState(
        TimeRange Range,
        TimeRange ReachableRange,
        CoreObject Target,
        TimeSpan Prefix,
        TimeSpan? StateEnd,
        bool IgnoreTail,
        bool AffectsOffset);

    internal sealed class TargetCollection : IReadOnlyList<Target>
    {
        private readonly List<Target> _targets;

        public TargetCollection(List<Target> targets, bool isComplete)
        {
            _targets = targets;
            IsComplete = isComplete;
        }

        public bool IsComplete { get; }

        public int Count => _targets.Count;

        public Target this[int index] => _targets[index];

        public IEnumerator<Target> GetEnumerator() => _targets.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    // A single source-backed media stream whose OffsetPosition can be slipped.
    // Total is the absolute source duration (null when the stream has no bounded source).
    internal sealed class Target
    {
        public Target(
            IProperty<TimeSpan> offset,
            TimeSpan? total,
            TimeSpan? consumedDuration = null,
            TimeSpan? timelineRoom = null,
            TimeSpan? zeroConsumptionPadding = null,
            TimeSpan? sourceEndPosition = null,
            TimeSpan? forwardOffsetLimit = null,
            bool affectsOffset = true)
        {
            Offset = offset;
            Total = total;
            ConsumedDuration = consumedDuration;
            TimelineRoom = timelineRoom;
            ZeroConsumptionPadding = zeroConsumptionPadding;
            SourceEndPosition = sourceEndPosition;
            ForwardOffsetLimit = forwardOffsetLimit;
            AffectsOffset = affectsOffset;
        }

        public IProperty<TimeSpan> Offset { get; }

        public TimeSpan? Total { get; private set; }

        public TimeSpan? ConsumedDuration { get; private set; }

        public TimeSpan? TimelineRoom { get; private set; }

        public TimeSpan? ZeroConsumptionPadding { get; private set; }

        public TimeSpan? SourceEndPosition { get; private set; }

        public TimeSpan? ForwardOffsetLimit { get; private set; }

        public bool AffectsOffset { get; }

        public TimeSpan Current
        {
            get => Offset.CurrentValue;
            set => Offset.CurrentValue = value;
        }

        internal void Merge(Target other)
        {
            if (other.Total is { } total)
            {
                Total = Total is { } currentTotal ? TimeSpan.FromTicks(Math.Min(currentTotal.Ticks, total.Ticks)) : total;
            }

            if (other.ConsumedDuration is { } consumed)
            {
                ConsumedDuration = ConsumedDuration is { } currentConsumed
                    ? TimeSpan.FromTicks(Math.Max(currentConsumed.Ticks, consumed.Ticks))
                    : consumed;
            }

            if (other.TimelineRoom is { } room)
            {
                TimelineRoom = TimelineRoom is { } currentRoom
                    ? TimeSpan.FromTicks(Math.Min(currentRoom.Ticks, room.Ticks))
                    : room;
            }

            if (other.ZeroConsumptionPadding is { } padding)
            {
                ZeroConsumptionPadding = ZeroConsumptionPadding is { } currentPadding
                    ? TimeSpan.FromTicks(Math.Max(currentPadding.Ticks, padding.Ticks))
                    : padding;
            }

            if (other.SourceEndPosition is { } sourceEndPosition)
            {
                SourceEndPosition = SourceEndPosition is { } currentSourceEndPosition
                    ? TimeSpan.FromTicks(Math.Max(currentSourceEndPosition.Ticks, sourceEndPosition.Ticks))
                    : sourceEndPosition;
            }

            if (other.ForwardOffsetLimit is { } forwardOffsetLimit)
            {
                ForwardOffsetLimit = ForwardOffsetLimit is { } currentForwardOffsetLimit
                    ? TimeSpan.FromTicks(Math.Min(currentForwardOffsetLimit.Ticks, forwardOffsetLimit.Ticks))
                    : forwardOffsetLimit;
            }
        }
    }

    // Disabled (IsEnabled == false) media is deliberately included, unlike playback's
    // Element.CollectObjects: trim edits apply one shared delta to every stream so linked
    // media (e.g. a temporarily muted audio track) stays in sync with the visible content
    // and re-enabling it does not reveal a desynced or out-of-range offset. The same
    // reasoning keeps disabled streams in the clamp bounds — a delta that would push a
    // disabled stream outside its source is refused, not applied desynced.
    public static TargetCollection Collect(Element element, TimeSpan forwardExtension = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (forwardExtension < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(forwardExtension));

        var targets = new List<Target>();
        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        TimeSpan reachableDuration = AddDurationSaturated(element.Length, forwardExtension);
        TimeSpan maximumDuration = TimeSpan.MaxValue - element.Start;
        if (reachableDuration > maximumDuration)
            reachableDuration = maximumDuration;
        var context = new TimeContext(
            element.Range,
            new TimeRange(element.Start, reachableDuration),
            null,
            false,
            false,
            true);
        bool isComplete = true;
        foreach (EngineObject obj in element.Objects)
        {
            CollectFrom(obj, targets, active, context, ref isComplete);
        }

        return new TargetCollection(targets, isComplete);
    }

    // The active set prevents cycles while still allowing a shared media object to contribute
    // once for every presentation path. Those path-specific bounds are merged by AddTarget.
    private static void CollectFrom(
        object obj,
        List<Target> targets,
        HashSet<object> active,
        TimeContext context,
        ref bool isComplete)
    {
        if (!active.Add(obj)) return;

        try
        {
            switch (obj)
            {
                case SourceVideo video:
                    if (!HasCompleteVideoState(video))
                    {
                        isComplete = false;
                        break;
                    }

                    foreach (Target target in CreateVideoTargets(video, context))
                        AddTarget(targets, target);
                    break;
                case SourceSound sound:
                    if (!HasCompleteSoundState(sound))
                    {
                        isComplete = false;
                        break;
                    }

                    AddTarget(targets, CreateSoundTarget(sound, context));
                    break;
                case SceneSound sceneSound:
                    if (!HasCompleteSceneSoundState(sceneSound))
                    {
                        isComplete = false;
                        break;
                    }

                    AddTarget(targets, CreateSceneSoundTarget(sceneSound, context));
                    break;
                case SoundGroup soundGroup:
                    foreach (Sound child in soundGroup.Children)
                        CollectFrom(child, targets, active, context, ref isComplete);
                    break;
                case DrawableGroup drawableGroup:
                    foreach (Drawable child in drawableGroup.Children)
                        CollectFrom(child, targets, active, context, ref isComplete);
                    break;
                case DrawableDecorator decorator:
                    foreach (Drawable child in decorator.Children)
                        CollectFrom(child, targets, active, context, ref isComplete);
                    break;
                case ITimeMappingPresenter timeMappingPresenter:
                    if (!TryGetPresenterTargetStates(timeMappingPresenter, context, out var mappedStates))
                    {
                        isComplete = false;
                        break;
                    }

                    foreach (TimeMappingTargetState state in mappedStates)
                    {
                        CoreObject controlled = state.Target;
                        if (!timeMappingPresenter.CanProvideCompleteTimeMapping(
                                state.ReachableRange,
                                controlled,
                                context.IsReversed))
                        {
                            isComplete = false;
                            break;
                        }

                        TimeRange mapped = timeMappingPresenter.CalculateTargetTimeRange(state.Range, controlled);
                        TimeRange mappedReachable = timeMappingPresenter.CalculateTargetTimeRange(
                            state.ReachableRange,
                            controlled);
                        TimeSpan currentTime = context.IsReversed ? state.Range.Start : state.Range.End;
                        Func<TimeSpan, TimeSpan> mapper = duration =>
                        {
                            if (state.IgnoreTail)
                                return TimeSpan.MaxValue;
                            if (duration == TimeSpan.MaxValue)
                                return TimeSpan.MaxValue;

                            TimeSpan parentDuration = timeMappingPresenter.CalculateTimelineDuration(
                                currentTime,
                                duration,
                                controlled,
                                reverse: context.IsReversed);
                            if (parentDuration == TimeSpan.MaxValue)
                                return TimeSpan.MaxValue;

                            if (state.StateEnd is { } stateEnd
                                && parentDuration >= GetDurationBetween(currentTime, stateEnd))
                            {
                                return TimeSpan.MaxValue;
                            }

                            TimeSpan totalDuration = AddDurationSaturated(state.Prefix, parentDuration);
                            return context.TimelineDurationFromTarget?.Invoke(totalDuration) ?? totalDuration;
                        };
                        bool isReversed = context.IsReversed
                            ^ timeMappingPresenter.IsReversed(state.Range, controlled);
                        bool hasUnboundedTail = state.IgnoreTail
                            || context.HasUnboundedTail
                            || timeMappingPresenter.HasUnboundedTail(
                                state.Range,
                                controlled,
                                reverse: context.IsReversed);

                        var mappedContext = new TimeContext(
                            mapped,
                            mappedReachable,
                            mapper,
                            hasUnboundedTail,
                            isReversed,
                            state.AffectsOffset);
                        CollectFrom(controlled, targets, active, mappedContext, ref isComplete);
                    }
                    break;
                case ITargetStatePresenter targetStatePresenter:
                    if (!TryGetPresenterTargetStates(targetStatePresenter, context, out var identityStates))
                    {
                        isComplete = false;
                        break;
                    }

                    foreach (TimeMappingTargetState state in identityStates)
                    {
                        TimeSpan currentTime = context.IsReversed ? state.Range.Start : state.Range.End;
                        Func<TimeSpan, TimeSpan> mapper = duration =>
                        {
                            if (state.IgnoreTail || duration == TimeSpan.MaxValue)
                                return TimeSpan.MaxValue;

                            if (state.StateEnd is { } stateEnd
                                && duration >= GetDurationBetween(currentTime, stateEnd))
                            {
                                return TimeSpan.MaxValue;
                            }

                            TimeSpan totalDuration = AddDurationSaturated(state.Prefix, duration);
                            return context.TimelineDurationFromTarget?.Invoke(totalDuration) ?? totalDuration;
                        };
                        var presentedContext = new TimeContext(
                            state.Range,
                            state.ReachableRange,
                            mapper,
                            state.IgnoreTail || context.HasUnboundedTail,
                            context.IsReversed,
                            state.AffectsOffset);
                        CollectFrom(state.Target, targets, active, presentedContext, ref isComplete);
                    }
                    break;
                // Keep legacy typed presenters safe even when they have not opted into exact
                // target-state reporting yet.
                case IPresenter<Drawable> presenter:
                    if (presenter.Target.HasExpression || presenter.Target.Animation != null)
                    {
                        isComplete = false;
                    }
                    else if (presenter.Target.CurrentValue is { } presented)
                    {
                        CollectFrom(presented, targets, active, context, ref isComplete);
                    }
                    break;
            }
        }
        finally
        {
            active.Remove(obj);
        }
    }

    private static bool HasCompleteVideoState(SourceVideo video)
    {
        return !video.OffsetPosition.HasExpression
            && video.OffsetPosition.Animation == null
            && !video.Source.HasExpression
            && video.Source.Animation is null or KeyFrameAnimation<VideoSource?>
            && !video.IsLoop.HasExpression
            && video.IsLoop.Animation is null or KeyFrameAnimation<bool>
            && !video.Speed.HasExpression
            && video.Speed.Animation is null or KeyFrameAnimation<float>;
    }

    private static bool HasCompleteSoundState(SourceSound sound)
    {
        return !sound.OffsetPosition.HasExpression
            && sound.OffsetPosition.Animation == null
            && !sound.Source.HasExpression
            && sound.Source.Animation == null
            && !sound.Speed.HasExpression
            && sound.Speed.Animation == null
            && float.IsFinite(sound.Speed.CurrentValue)
            && sound.Speed.CurrentValue >= 0;
    }

    private static bool HasCompleteSceneSoundState(SceneSound sound)
    {
        return !sound.OffsetPosition.HasExpression
            && sound.OffsetPosition.Animation == null
            && !sound.ReferencedScene.HasExpression
            && sound.ReferencedScene.Animation == null
            && !sound.Speed.HasExpression
            && sound.Speed.Animation == null
            && float.IsFinite(sound.Speed.CurrentValue)
            && sound.Speed.CurrentValue >= 0;
    }

    private static bool TryGetPresenterTargetStates(
        ITargetStatePresenter presenter,
        TimeContext context,
        out List<TimeMappingTargetState> result)
    {
        result = [];
        if (!TryGetTargetStateQueryRange(context, out TimeRange queryRange)
            || !presenter.TryGetTargetStates(queryRange, out IReadOnlyList<PresenterTargetState> states)
            || !IsCompleteTargetStatePartition(presenter, queryRange, states))
        {
            return false;
        }

        TimeRange range = context.Range;
        PresenterTargetState? selectedEmptyState = null;
        if (!range.IsEmpty)
        {
            foreach (PresenterTargetState state in states)
            {
                TimeSpan start = state.CompositionRange.Start > range.Start
                    ? state.CompositionRange.Start
                    : range.Start;
                TimeSpan end = state.CompositionRange.End < range.End
                    ? state.CompositionRange.End
                    : range.End;
                if (start >= end || state.Target is not { } target)
                    continue;

                bool isTailState = context.IsReversed
                    ? state.CompositionRange.Start < range.Start
                    : state.CompositionRange.End > range.End;
                TimeRange activeRange = new(start, end - start);
                result.Add(new TimeMappingTargetState(
                    activeRange,
                    isTailState ? state.CompositionRange : activeRange,
                    target,
                    TimeSpan.Zero,
                    isTailState ? GetNextStateBoundary(state, context.IsReversed) : null,
                    !isTailState,
                    context.AffectsOffset));
            }
        }
        else
        {
            if (!TrySelectBoundaryState(states, range.Start, context.IsReversed, out PresenterTargetState current))
                return false;

            selectedEmptyState = current;
            if (current.Target is { } target)
            {
                result.Add(new TimeMappingTargetState(
                    range,
                    current.CompositionRange,
                    target,
                    TimeSpan.Zero,
                    GetNextStateBoundary(current, context.IsReversed),
                    false,
                    context.AffectsOffset));
            }
        }

        AddFutureTargetStates(states, context, selectedEmptyState, result);
        return true;
    }

    private static bool TryGetTargetStateQueryRange(TimeContext context, out TimeRange queryRange)
    {
        TimeRange reachable = context.ReachableRange;
        if (!reachable.IsEmpty)
        {
            queryRange = reachable;
            return TryGetRangeEnd(reachable, out _);
        }

        TimeSpan point = context.Range.Start;
        if (context.IsReversed)
        {
            if (point == TimeSpan.MinValue)
            {
                queryRange = default;
                return false;
            }

            queryRange = new TimeRange(point - TimeSpan.FromTicks(1), TimeSpan.FromTicks(1));
            return true;
        }

        if (point == TimeSpan.MaxValue)
        {
            queryRange = default;
            return false;
        }

        queryRange = new TimeRange(point, TimeSpan.FromTicks(1));
        return true;
    }

    private static bool TrySelectBoundaryState(
        IReadOnlyList<PresenterTargetState> states,
        TimeSpan boundary,
        bool reverse,
        out PresenterTargetState selected)
    {
        if (reverse)
        {
            for (int i = states.Count - 1; i >= 0; i--)
            {
                PresenterTargetState state = states[i];
                if (state.CompositionRange.Start < boundary
                    && state.CompositionRange.End >= boundary)
                {
                    selected = state;
                    return true;
                }
            }
        }
        else
        {
            foreach (PresenterTargetState state in states)
            {
                if (state.CompositionRange.Contains(boundary))
                {
                    selected = state;
                    return true;
                }
            }
        }

        selected = default;
        return false;
    }

    private static bool IsCompleteTargetStatePartition(
        ITargetStatePresenter presenter,
        TimeRange queryRange,
        IReadOnlyList<PresenterTargetState>? states)
    {
        if (states is null || states.Count == 0)
            return false;

        TimeSpan cursor = queryRange.Start;
        foreach (PresenterTargetState state in states)
        {
            TimeRange range = state.CompositionRange;
            if (range.IsEmpty
                || range.Start != cursor
                || !TryGetRangeEnd(range, out TimeSpan end)
                || end > queryRange.End
                || state.Target is { } target && !presenter.TargetType.IsInstanceOfType(target))
            {
                return false;
            }

            cursor = end;
        }

        return cursor == queryRange.End;
    }

    private static bool TryGetRangeEnd(TimeRange range, out TimeSpan end)
    {
        if (range.Duration <= TimeSpan.Zero
            || range.Start.Ticks > TimeSpan.MaxValue.Ticks - range.Duration.Ticks)
        {
            end = default;
            return false;
        }

        end = TimeSpan.FromTicks(range.Start.Ticks + range.Duration.Ticks);
        return true;
    }

    private static TimeSpan? GetNextStateBoundary(
        PresenterTargetState state,
        bool reverse)
    {
        return reverse ? state.CompositionRange.Start : state.CompositionRange.End;
    }

    private static void AddFutureTargetStates(
        IReadOnlyList<PresenterTargetState> states,
        TimeContext context,
        PresenterTargetState? selectedEmptyState,
        List<TimeMappingTargetState> result)
    {
        TimeSpan origin = context.IsReversed ? context.Range.Start : context.Range.End;
        if (context.IsReversed)
        {
            for (int i = states.Count - 1; i >= 0; i--)
            {
                PresenterTargetState state = states[i];
                if (state.CompositionRange.End > origin
                    || state.Target is not { } target
                    || selectedEmptyState is { } selected && state == selected)
                    continue;

                TimeSpan boundary = state.CompositionRange.End;
                result.Add(new TimeMappingTargetState(
                    new TimeRange(boundary, TimeSpan.Zero),
                    state.CompositionRange,
                    target,
                    GetDurationBetween(origin, boundary),
                    GetNextStateBoundary(state, true),
                    false,
                    false));
            }
        }
        else
        {
            foreach (PresenterTargetState state in states)
            {
                if (state.CompositionRange.Start < origin
                    || state.Target is not { } target
                    || selectedEmptyState is { } selected && state == selected)
                    continue;

                TimeSpan boundary = state.CompositionRange.Start;
                result.Add(new TimeMappingTargetState(
                    new TimeRange(boundary, TimeSpan.Zero),
                    state.CompositionRange,
                    target,
                    GetDurationBetween(origin, boundary),
                    GetNextStateBoundary(state, false),
                    false,
                    false));
            }
        }
    }

    private static TimeSpan GetDurationBetween(TimeSpan first, TimeSpan second)
    {
        double ticks = Math.Abs((double)second.Ticks - first.Ticks);
        return TimeSpan.FromTicks((long)Math.Min(TimeSpan.MaxValue.Ticks, ticks));
    }

    private static TimeSpan AddDurationSaturated(TimeSpan left, TimeSpan right)
    {
        return left.Ticks >= TimeSpan.MaxValue.Ticks - right.Ticks
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks(left.Ticks + right.Ticks);
    }

    private static void AddTarget(List<Target> targets, Target target)
    {
        foreach (Target existing in targets)
        {
            if (ReferenceEquals(existing.Offset, target.Offset)
                && existing.AffectsOffset == target.AffectsOffset)
            {
                existing.Merge(target);
                return;
            }
        }

        targets.Add(target);
    }

    private static IEnumerable<Target> CreateVideoTargets(SourceVideo video, TimeContext context)
    {
        TimeSpan? timelineRoom = CalculateVideoTimelineRoom(video, context);
        foreach (TimeRange range in GetVideoStateRanges(video, context.Range))
        {
            TimeSpan sampleTime = range.Start + TimeSpan.FromTicks(range.Duration.Ticks / 2);
            using var resource = (SourceVideo.Resource)video.ToResource(new CompositionContext(sampleTime));
            yield return CreateVideoTarget(video, context with { Range = range }, resource, timelineRoom);
        }
    }

    private static IEnumerable<TimeRange> GetVideoStateRanges(SourceVideo video, TimeRange range)
    {
        if (range.IsEmpty)
        {
            yield return range;
            yield break;
        }

        var boundaries = new List<TimeSpan> { range.Start, range.End };
        foreach (TimeSpan time in GetAnimationTimes(video))
        {
            if (time > range.Start && time < range.End)
                boundaries.Add(time);
        }
        boundaries.Sort();

        for (int i = 1; i < boundaries.Count; i++)
        {
            TimeSpan start = boundaries[i - 1];
            TimeSpan end = boundaries[i];
            if (start < end)
                yield return new TimeRange(start, end - start);
        }
    }

    private static List<TimeSpan> GetAnimationTimes(SourceVideo video)
    {
        var times = new HashSet<TimeSpan>();
        AddAnimationTimes(video.Source.Animation, video, times);
        AddAnimationTimes(video.IsLoop.Animation, video, times);
        return [.. times.Order()];
    }

    private static void AddAnimationTimes(
        IAnimation? animation,
        SourceVideo video,
        HashSet<TimeSpan> times)
    {
        if (animation is not KeyFrameAnimation keyFrameAnimation)
            return;

        foreach (IKeyFrame keyFrame in keyFrameAnimation.KeyFrames)
        {
            TimeSpan time = keyFrameAnimation.UseGlobalClock
                ? keyFrame.KeyTime
                : video.TimeRange.Start + keyFrame.KeyTime;
            times.Add(time);
        }
    }

    private static TimeSpan? CalculateVideoTimelineRoom(SourceVideo video, TimeContext context)
    {
        if (context.HasUnboundedTail)
            return TimeSpan.MaxValue;

        if (context.IsReversed)
            return CalculateReversedVideoTimelineRoom(video, context);

        TimeSpan cursor = context.Range.End;
        TimeSpan accumulated = TimeSpan.Zero;
        TimeSpan[] futureTimes = GetAnimationTimes(video)
            .Where(time => time >= cursor)
            .ToArray();
        int nextIndex = 0;

        while (true)
        {
            TimeSpan? next = nextIndex < futureTimes.Length ? futureTimes[nextIndex] : null;
            if (next is { } nextTime && nextTime <= cursor)
            {
                nextIndex++;
                continue;
            }

            TimeSpan sampleTime = next is { } sampleBoundary
                ? cursor + TimeSpan.FromTicks((sampleBoundary - cursor).Ticks / 2)
                : cursor;
            using var resource = (SourceVideo.Resource)video.ToResource(new CompositionContext(sampleTime));
            if (resource.Source is not { } source || source.Duration <= TimeSpan.Zero)
            {
                if (next is not { } boundary)
                    return MapTimelineDuration(context, accumulated, TimeSpan.MaxValue);

                accumulated += boundary - cursor;
                cursor = boundary;
                nextIndex++;
                continue;
            }

            TimeSpan sourcePosition = GetSourcePositionAt(video, cursor, resource);
            if (resource.IsLoop && source.Duration > TimeSpan.Zero)
                sourcePosition = NormalizeLoopPosition(sourcePosition, source.Duration);

            if (resource.IsLoop && video.OffsetPosition.CurrentValue == TimeSpan.Zero)
            {
                if (next is not { } boundary)
                    return MapTimelineDuration(context, accumulated, TimeSpan.MaxValue);

                accumulated += boundary - cursor;
                cursor = boundary;
                nextIndex++;
                continue;
            }

            TimeSpan sourceRoom = source.Duration - video.OffsetPosition.CurrentValue - sourcePosition;
            if (sourceRoom <= TimeSpan.Zero)
                return MapTimelineDuration(context, accumulated, TimeSpan.Zero);

            TimeSpan timelineRoom = video.CalculateTimelineDuration(
                GetVideoClockStartAt(video, cursor),
                sourceRoom,
                resource);
            if (timelineRoom == TimeSpan.MaxValue)
                return MapTimelineDuration(context, accumulated, TimeSpan.MaxValue);

            if (next is not { } nextBoundary || timelineRoom <= nextBoundary - cursor)
            {
                return MapTimelineDuration(context, accumulated, timelineRoom);
            }

            accumulated += nextBoundary - cursor;
            cursor = nextBoundary;
            nextIndex++;
        }
    }

    private static TimeSpan? CalculateReversedVideoTimelineRoom(SourceVideo video, TimeContext context)
    {
        TimeSpan cursor = context.Range.Start;
        TimeSpan accumulated = TimeSpan.Zero;
        TimeSpan[] earlierTimes = GetAnimationTimes(video)
            .Where(time => time <= cursor)
            .OrderDescending()
            .ToArray();
        int nextIndex = 0;

        while (nextIndex < earlierTimes.Length)
        {
            TimeSpan next = earlierTimes[nextIndex];
            if (next >= cursor)
            {
                nextIndex++;
                continue;
            }

            TimeSpan sampleTime = cursor + TimeSpan.FromTicks((next - cursor).Ticks / 2);
            using var resource = (SourceVideo.Resource)video.ToResource(new CompositionContext(sampleTime));
            TimeSpan stateDuration = cursor - next;
            if (resource.Source is { } source && source.Duration > TimeSpan.Zero)
            {
                TimeSpan sourcePosition = GetSourcePositionAt(video, cursor, resource);
                if (resource.IsLoop)
                    sourcePosition = NormalizeLoopPosition(sourcePosition, source.Duration);

                if (video.OffsetPosition.CurrentValue + sourcePosition >= source.Duration
                    || SpeedMayRunBackward(video, new TimeRange(next, stateDuration)))
                {
                    return MapTimelineDuration(context, accumulated, TimeSpan.Zero);
                }

                if (resource.IsLoop
                    && video.OffsetPosition.CurrentValue > TimeSpan.Zero
                    && TryGetPreviousLoopWrapDuration(
                        video, cursor, stateDuration, source.Duration, resource, out TimeSpan wrapDuration))
                {
                    return MapTimelineDuration(context, accumulated, wrapDuration);
                }
            }

            accumulated = AddDurationSaturated(accumulated, stateDuration);
            cursor = next;
            nextIndex++;
        }

        TimeSpan videoStart = video.TimeRange.Start;
        if (videoStart < cursor)
        {
            TimeSpan sampleTime = cursor + TimeSpan.FromTicks((videoStart - cursor).Ticks / 2);
            using var resource = (SourceVideo.Resource)video.ToResource(new CompositionContext(sampleTime));
            TimeSpan stateDuration = cursor - videoStart;
            if (resource.Source is { } source && source.Duration > TimeSpan.Zero)
            {
                TimeSpan sourcePosition = GetSourcePositionAt(video, cursor, resource);
                if (resource.IsLoop)
                    sourcePosition = NormalizeLoopPosition(sourcePosition, source.Duration);

                if (video.OffsetPosition.CurrentValue + sourcePosition >= source.Duration
                    || SpeedMayRunBackward(video, new TimeRange(videoStart, stateDuration)))
                {
                    return MapTimelineDuration(context, accumulated, TimeSpan.Zero);
                }

                if (resource.IsLoop
                    && video.OffsetPosition.CurrentValue > TimeSpan.Zero
                    && TryGetPreviousLoopWrapDuration(
                        video, cursor, stateDuration, source.Duration, resource, out TimeSpan wrapDuration))
                {
                    return MapTimelineDuration(context, accumulated, wrapDuration);
                }
            }
        }

        return null;
    }

    private static bool TryGetPreviousLoopWrapDuration(
        SourceVideo video,
        TimeSpan cursor,
        TimeSpan maximumDuration,
        TimeSpan sourceDuration,
        SourceVideo.Resource resource,
        out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        TimeSpan rawAtCursor = GetRawSourcePositionAt(video, cursor, resource);
        long remainder = rawAtCursor.Ticks % sourceDuration.Ticks;
        if (remainder < 0)
            remainder += sourceDuration.Ticks;
        if (remainder == 0)
            return true;
        if (rawAtCursor.Ticks < TimeSpan.MinValue.Ticks + remainder)
            return true;

        TimeSpan previousWrap = TimeSpan.FromTicks(rawAtCursor.Ticks - remainder);
        TimeSpan earliest = cursor - maximumDuration;
        if (GetRawSourcePositionAt(video, earliest, resource) > previousWrap)
            return false;

        TimeSpan low = TimeSpan.Zero;
        TimeSpan high = maximumDuration;
        for (int i = 0; i < 50; i++)
        {
            TimeSpan middle = TimeSpan.FromTicks(low.Ticks + (high.Ticks - low.Ticks) / 2);
            TimeSpan position = GetRawSourcePositionAt(video, cursor - middle, resource);
            if (position > previousWrap)
                low = middle;
            else
                high = middle;
        }

        duration = high;
        return true;
    }

    private static TimeSpan MapTimelineDuration(TimeContext context, TimeSpan prefix, TimeSpan tail)
    {
        if (prefix == TimeSpan.MaxValue || tail == TimeSpan.MaxValue)
            return TimeSpan.MaxValue;

        long ticks = prefix.Ticks >= TimeSpan.MaxValue.Ticks - tail.Ticks
            ? TimeSpan.MaxValue.Ticks
            : prefix.Ticks + tail.Ticks;
        TimeSpan duration = TimeSpan.FromTicks(ticks);
        return context.TimelineDurationFromTarget?.Invoke(duration) ?? duration;
    }

    private static Target CreateVideoTarget(
        SourceVideo video,
        TimeContext context,
        SourceVideo.Resource resource,
        TimeSpan? timelineRoomOverride)
    {
        // Slip offsets and media bounds use source time, including speed conversion.
        TimeSpan? total = resource.Source is { } mediaSource && mediaSource.Duration > TimeSpan.Zero
            ? mediaSource.Duration
            : null;
        TimeSpan consumedDuration = GetConsumedDuration(video, context.Range, resource);
        if (consumedDuration < TimeSpan.Zero) consumedDuration = TimeSpan.Zero;
        TimeSpan? zeroConsumptionPadding = null;
        if (resource.Source is { } source
            && source.FrameRate.Numerator > 0
            && source.FrameRate.Denominator > 0)
        {
            double frameTicks = TimeSpan.TicksPerSecond
                * source.FrameRate.Denominator
                / (double)source.FrameRate.Numerator;
            if (frameTicks > 0)
            {
                long roundedTicks = Math.Max(1L, (long)Math.Round(frameTicks));
                TimeSpan frameDuration = TimeSpan.FromTicks(roundedTicks);
                if (consumedDuration < frameDuration)
                    zeroConsumptionPadding = frameDuration;
            }
        }
        TimeSpan sourceEndPosition = GetMaximumSourcePosition(video, context.Range, resource);
        TimeSpan? timelineRoom = timelineRoomOverride;
        TimeSpan? forwardOffsetLimit = null;
        if (total is { } sourceDuration)
        {
            TimeSpan sourceRoom = sourceDuration - video.OffsetPosition.CurrentValue - sourceEndPosition;
            if (sourceRoom < TimeSpan.Zero) sourceRoom = TimeSpan.Zero;
            TimeSpan padding = zeroConsumptionPadding ?? TimeSpan.Zero;
            TimeSpan reservation = sourceEndPosition >= padding ? sourceEndPosition : padding;
            TimeSpan maxOffset = sourceDuration - reservation;
            if (maxOffset < TimeSpan.Zero) maxOffset = TimeSpan.Zero;
            forwardOffsetLimit = maxOffset;
            if (timelineRoomOverride is null
                && (context.HasUnboundedTail
                || resource.IsLoop
                    && video.OffsetPosition.CurrentValue == TimeSpan.Zero))
            {
                timelineRoom = TimeSpan.MaxValue;
            }
            else if (timelineRoomOverride is null && context.IsReversed)
            {
                TimeSpan targetRoom = context.Range.Start - video.TimeRange.Start;
                if (targetRoom < TimeSpan.Zero) targetRoom = TimeSpan.Zero;
                timelineRoom = context.TimelineDurationFromTarget?.Invoke(targetRoom) ?? targetRoom;
            }
            else if (timelineRoomOverride is null)
            {
                TimeSpan targetRoom = video.CalculateTimelineDuration(
                    GetVideoClockStartAt(video, context.Range.End), sourceRoom, resource);
                timelineRoom = context.TimelineDurationFromTarget?.Invoke(targetRoom) ?? targetRoom;
            }
        }

        return new Target(
            video.OffsetPosition,
            total,
            consumedDuration,
            timelineRoom,
            zeroConsumptionPadding,
            sourceEndPosition,
            forwardOffsetLimit,
            context.AffectsOffset);
    }

    private static TimeSpan GetConsumedDuration(
        SourceVideo video, TimeRange range, SourceVideo.Resource resource)
    {
        TimeSpan duration = range.Duration;
        TimeSpan clockStart = GetVideoClockStartAt(video, range.Start);
        return duration > TimeSpan.Zero
            ? video.CalculateVideoDuration(clockStart, duration, resource)
            : TimeSpan.Zero;
    }

    private static TimeSpan GetMaximumSourcePosition(
        SourceVideo video, TimeRange range, SourceVideo.Resource resource)
    {
        if (resource.Source is { } mediaSource
            && mediaSource.Duration > TimeSpan.Zero
            && (SpeedMayRunBackward(video, range)
                || !resource.IsLoop && CrossesSourcePositionZero(video, range, resource)))
        {
            return mediaSource.Duration;
        }

        TimeSpan start = GetSourcePositionAt(video, range.Start, resource);
        TimeSpan end = GetSourcePositionAt(video, range.End, resource);

        if (resource.IsLoop
            && resource.Source is { } source
            && source.Duration > TimeSpan.Zero)
        {
            TimeSpan distance = TimeSpan.FromTicks(
                (long)Math.Min(
                    TimeSpan.MaxValue.Ticks,
                    Math.Abs((double)end.Ticks - start.Ticks)));
            if (distance >= source.Duration
                || GetLoopCycle(start, source.Duration) != GetLoopCycle(end, source.Duration))
            {
                return source.Duration;
            }

            TimeSpan normalizedStart = NormalizeLoopPosition(start, source.Duration);
            TimeSpan normalizedEnd = NormalizeLoopPosition(end, source.Duration);
            return normalizedStart >= normalizedEnd ? normalizedStart : normalizedEnd;
        }

        return start >= end ? start : end;
    }

    private static bool CrossesSourcePositionZero(
        SourceVideo video, TimeRange range, SourceVideo.Resource resource)
    {
        TimeSpan start = GetRawSourcePositionAt(video, range.Start, resource);
        TimeSpan end = GetRawSourcePositionAt(video, range.End, resource);
        return start < TimeSpan.Zero && end >= TimeSpan.Zero
            || end < TimeSpan.Zero && start >= TimeSpan.Zero;
    }

    private static bool SpeedMayRunBackward(SourceVideo video, TimeRange range)
    {
        if (video.Speed.Animation is not KeyFrameAnimation<float> animation
            || animation.KeyFrames.Count == 0)
        {
            return false;
        }

        TimeSpan firstClock = GetVideoClockStartAt(video, range.Start);
        TimeSpan secondClock = GetVideoClockStartAt(video, range.End);
        TimeSpan rangeStart = firstClock <= secondClock ? firstClock : secondClock;
        TimeSpan rangeEnd = firstClock >= secondClock ? firstClock : secondClock;
        if (rangeStart == rangeEnd)
            return animation.Interpolate(rangeStart) < 0;

        if (animation.Interpolate(rangeStart) < 0
            || animation.Interpolate(rangeEnd) < 0
            || animation.KeyFrames[0] is not KeyFrame<float> first)
        {
            return true;
        }

        var previous = first;
        for (int i = 1; i < animation.KeyFrames.Count; i++)
        {
            if (animation.KeyFrames[i] is not KeyFrame<float> next)
                return true;

            if (rangeEnd > previous.KeyTime && rangeStart < next.KeyTime)
            {
                if (!next.Easing.TryGetOutputRange(out float easingMinimum, out float easingMaximum)
                    || !float.IsFinite(easingMinimum)
                    || !float.IsFinite(easingMaximum))
                {
                    return true;
                }

                float delta = next.Value - previous.Value;
                float intervalMinimum = delta >= 0
                    ? previous.Value + easingMinimum * delta
                    : previous.Value + easingMaximum * delta;
                if (!float.IsFinite(intervalMinimum) || intervalMinimum < 0)
                    return true;
            }

            previous = next;
        }

        return false;
    }

    private static TimeSpan NormalizeLoopPosition(TimeSpan value, TimeSpan duration)
    {
        long ticks = value.Ticks % duration.Ticks;
        if (ticks < 0)
            ticks += duration.Ticks;
        return TimeSpan.FromTicks(ticks);
    }

    private static long GetLoopCycle(TimeSpan value, TimeSpan duration)
    {
        long cycle = Math.DivRem(value.Ticks, duration.Ticks, out long remainder);
        return remainder < 0 ? cycle - 1 : cycle;
    }

    private static TimeSpan GetSourcePositionAt(
        SourceVideo video, TimeSpan time, SourceVideo.Resource resource)
    {
        TimeSpan position = GetRawSourcePositionAt(video, time, resource);
        if (!resource.IsLoop
            && resource.Source is { } source
            && source.Duration > TimeSpan.Zero
            && position < TimeSpan.Zero)
        {
            position = TimeSpan.FromTicks(source.Duration.Ticks + position.Ticks);
        }

        return resource.IsLoop || position > TimeSpan.Zero ? position : TimeSpan.Zero;
    }

    private static TimeSpan GetRawSourcePositionAt(
        SourceVideo video, TimeSpan time, SourceVideo.Resource resource)
    {
        TimeSpan sourceClockStart = GetVideoClockStartAt(video, video.TimeRange.Start);
        TimeSpan sourceClock = GetVideoClockStartAt(video, time);
        TimeSpan duration = sourceClock - sourceClockStart;
        if (duration == TimeSpan.Zero)
            return TimeSpan.Zero;

        return video.CalculateVideoDuration(sourceClockStart, duration, resource);
    }

    private static TimeSpan GetVideoClockStartAt(SourceVideo video, TimeSpan time)
    {
        return video.Speed.Animation is KeyFrameAnimation<float> { UseGlobalClock: true }
            ? time
            : time - video.TimeRange.Start;
    }

    private static Target CreateSoundTarget(SourceSound sound, TimeContext context)
    {
        // SourceSound.TryGetOriginalDuration returns the full source duration.
        TimeSpan? total = sound.TryGetOriginalDuration(out TimeSpan duration) ? duration : null;
        return CreateSoundTargetCore(
            sound,
            sound.OffsetPosition,
            sound.Speed.CurrentValue,
            total,
            context);
    }

    private static Target CreateSceneSoundTarget(SceneSound sound, TimeContext context)
    {
        // The referenced scene is the "source": its duration bounds how far the media
        // window can advance. Unresolved references stay unbounded, like a SourceVideo
        // without a loaded source.
        TimeSpan? total = sound.ReferencedScene.CurrentValue?.Duration;
        return CreateSoundTargetCore(
            sound,
            sound.OffsetPosition,
            sound.Speed.CurrentValue,
            total,
            context);
    }

    private static Target CreateSoundTargetCore(
        Sound sound,
        IProperty<TimeSpan> offset,
        float speed,
        TimeSpan? total,
        TimeContext context)
    {
        double speedScale = speed / 100.0;
        TimeSpan sourceStartPosition = GetSoundSourcePosition(
            context.Range.Start, sound.TimeRange.Start, speedScale);
        TimeSpan sourceEndPosition = GetSoundSourcePosition(
            context.Range.End, sound.TimeRange.Start, speedScale);
        TimeSpan consumedDuration = sourceEndPosition - sourceStartPosition;
        if (consumedDuration < TimeSpan.Zero) consumedDuration = TimeSpan.Zero;
        TimeSpan? timelineRoom = null;
        TimeSpan? forwardOffsetLimit = null;
        if (total is { } sourceDuration)
        {
            TimeSpan maxOffset = sourceDuration - sourceEndPosition;
            if (maxOffset < TimeSpan.Zero) maxOffset = TimeSpan.Zero;
            forwardOffsetLimit = maxOffset;

            if (context.HasUnboundedTail || speedScale == 0)
            {
                timelineRoom = TimeSpan.MaxValue;
            }
            else if (context.IsReversed)
            {
                TimeSpan targetRoom = context.Range.Start - sound.TimeRange.Start;
                if (targetRoom < TimeSpan.Zero) targetRoom = TimeSpan.Zero;
                timelineRoom = context.TimelineDurationFromTarget?.Invoke(targetRoom) ?? targetRoom;
            }
            else
            {
                TimeSpan sourceRoom = sourceDuration - offset.CurrentValue - sourceEndPosition;
                TimeSpan targetRoom = TimeSpan.Zero;
                if (sourceRoom > TimeSpan.Zero)
                {
                    TimeSpan leadIn = sound.TimeRange.Start > context.Range.End
                        ? sound.TimeRange.Start - context.Range.End
                        : TimeSpan.Zero;
                    targetRoom = AddDurationSaturated(
                        leadIn,
                        ScaleDuration(sourceRoom, 1 / speedScale));
                }

                timelineRoom = context.TimelineDurationFromTarget?.Invoke(targetRoom) ?? targetRoom;
            }
        }

        return new Target(
            offset,
            total,
            consumedDuration,
            timelineRoom,
            sourceEndPosition: sourceEndPosition,
            forwardOffsetLimit: forwardOffsetLimit,
            affectsOffset: context.AffectsOffset);
    }

    private static TimeSpan GetSoundSourcePosition(
        TimeSpan time,
        TimeSpan soundStart,
        double speedScale)
    {
        TimeSpan localTime = time - soundStart;
        return localTime > TimeSpan.Zero
            ? ScaleDuration(localTime, speedScale)
            : TimeSpan.Zero;
    }

    private static TimeSpan ScaleDuration(TimeSpan duration, double scale)
    {
        if (duration <= TimeSpan.Zero || !(scale > 0))
            return TimeSpan.Zero;

        double ticks = duration.Ticks * scale;
        return ticks >= TimeSpan.MaxValue.Ticks
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks((long)ticks);
    }

    // The largest-magnitude delta (in the requested direction) that every stream can apply
    // without leaving [0, Total - consumed duration]. Applying one shared delta keeps linked
    // streams (e.g. a video + audio pair) in sync even when one hits its source boundary first.
    public static TimeSpan ClampSharedDelta(IReadOnlyList<Target> targets, TimeSpan delta, TimeSpan elementLength)
    {
        if (targets is TargetCollection { IsComplete: false }) return TimeSpan.Zero;
        if (delta == TimeSpan.Zero || targets.Count == 0) return TimeSpan.Zero;

        long magnitude = Math.Abs(delta.Ticks);
        bool found = false;
        foreach (Target target in targets)
        {
            if (!target.AffectsOffset) continue;
            found = true;
            long allowed = delta > TimeSpan.Zero
                ? ForwardHeadroom(target, elementLength)
                : Math.Max(0L, target.Current.Ticks);
            magnitude = Math.Min(magnitude, allowed);
        }

        return found
            ? TimeSpan.FromTicks(delta > TimeSpan.Zero ? magnitude : -magnitude)
            : TimeSpan.Zero;
    }

    private static long ForwardHeadroom(Target target, TimeSpan elementLength)
    {
        if (target.Total is not { } total) return long.MaxValue;

        if (target.ForwardOffsetLimit is { } forwardOffsetLimit)
            return Math.Max(0L, (forwardOffsetLimit - target.Current).Ticks);

        TimeSpan sourcePosition = target.SourceEndPosition
            ?? target.ConsumedDuration
            ?? elementLength;
        TimeSpan padding = target.ZeroConsumptionPadding ?? TimeSpan.Zero;
        TimeSpan reservation = sourcePosition >= padding ? sourcePosition : padding;
        TimeSpan maxOffset = total - reservation;
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
        if (targets is TargetCollection { IsComplete: false }) return;
        if (delta == TimeSpan.Zero) return;

        foreach (Target target in targets)
        {
            if (target.AffectsOffset && (applied is null || applied.Add(target.Offset)))
            {
                target.Current += delta;
            }
        }
    }

    // Room to extend the element's out-point (grow its length while the in-point stays put),
    // bounded by the tightest source tail and by the caller's reachable geometry horizon.
    public static TimeSpan OutPointRoom(
        IReadOnlyList<Target> targets,
        TimeSpan elementLength,
        TimeSpan maximumRoom)
    {
        if (targets is TargetCollection { IsComplete: false }) return TimeSpan.Zero;
        if (maximumRoom < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumRoom));

        TimeSpan room = maximumRoom;
        foreach (Target target in targets)
        {
            TimeSpan available;
            if (target.TimelineRoom is { } timelineRoom)
            {
                available = timelineRoom;
            }
            else if (target.Total is { } total)
            {
                available = total - target.Current - elementLength;
            }
            else
            {
                continue;
            }

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
        if (targets is TargetCollection { IsComplete: false }) return TimeSpan.Zero;

        TimeSpan room = TimeSpan.MaxValue;
        foreach (Target target in targets)
        {
            if (!target.AffectsOffset) continue;
            if (target.Current < room) room = target.Current;
        }

        return room;
    }
}
