using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics;

[Display(Name = nameof(GraphicsStrings.DrawableTimeController), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DrawableTimeController : Drawable, ITimeMappingPresenter<Drawable>, IFlowOperator
{
    public DrawableTimeController()
    {
        ScanProperties<DrawableTimeController>();
    }

    [Display(Name = nameof(GraphicsStrings.Target), ResourceType = typeof(GraphicsStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<Drawable?> Target { get; } = Property.Create<Drawable?>();

    [Display(Name = nameof(GraphicsStrings.DrawableTimeController_OffsetPosition), ResourceType = typeof(GraphicsStrings))]
    public IProperty<TimeSpan> OffsetPosition { get; } = Property.Create<TimeSpan>();

    [Display(Name = nameof(GraphicsStrings.Speed), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.DrawableTimeController_AdjustTimeRange), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> AdjustTimeRange { get; } = Property.Create<bool>();

    [Display(Name = nameof(GraphicsStrings.DrawableTimeController_FrameRate), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> FrameRate { get; } = Property.Create<float>();

    [Display(Name = nameof(GraphicsStrings.DrawableTimeController_Loop), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> Loop { get; } = Property.Create<bool>();

    [Display(Name = nameof(GraphicsStrings.DrawableTimeController_Reverse), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> Reverse { get; } = Property.Create<bool>();

    [Display(Name = nameof(GraphicsStrings.DrawableTimeController_HoldFirstFrame), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> HoldFirstFrame { get; } = Property.Create<bool>();

    [Display(Name = nameof(GraphicsStrings.DrawableTimeController_HoldLastFrame), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> HoldLastFrame { get; } = Property.Create<bool>();

    /// <inheritdoc />
    public bool TryGetTargetStates(
        TimeRange compositionRange,
        out IReadOnlyList<PresenterTargetState> states)
    {
        if (compositionRange.IsEmpty)
        {
            states = [];
            return true;
        }

        if (Target.HasExpression || Target.Animation != null)
        {
            states = [];
            return false;
        }

        states = [new PresenterTargetState(compositionRange, Target.CurrentValue)];
        return true;
    }

    /// <inheritdoc />
    public bool CanProvideCompleteTimeMapping(
        TimeRange timeRange,
        Drawable target,
        bool reverse = false)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (OffsetPosition.HasExpression
            || OffsetPosition.Animation != null
            || AdjustTimeRange.HasExpression
            || AdjustTimeRange.Animation != null
            || FrameRate.HasExpression
            || FrameRate.Animation != null
            || Loop.HasExpression
            || Loop.Animation != null
            || Reverse.HasExpression
            || Reverse.Animation != null
            || HoldFirstFrame.HasExpression
            || HoldFirstFrame.Animation != null
            || HoldLastFrame.HasExpression
            || HoldLastFrame.Animation != null)
        {
            return false;
        }

        return !HasUnsupportedSpeedState(timeRange, target);
    }

    private bool HasUnsupportedSpeedState(TimeRange timeRange, Drawable target)
    {
        if (Speed.HasExpression)
            return true;

        if (Speed.Animation is not KeyFrameAnimation<float> animation)
        {
            return Speed.Animation != null
                || !float.IsFinite(Speed.CurrentValue)
                || Speed.CurrentValue < 0;
        }

        using var resource = (Resource)ToResource(new CompositionContext(GetSampleTime(timeRange)));
        return !float.IsFinite(resource.Speed)
            || resource.Speed < 0
            || SpeedMayRunBackward(timeRange, target, resource);
    }

    private static bool SpeedAnimationMayRunBackwardAnywhere(KeyFrameAnimation<float> animation)
    {
        if (animation.KeyFrames.Count == 0)
            return false;
        if (animation.KeyFrames[0] is not KeyFrame<float> first
            || !float.IsFinite(first.Value)
            || first.Value < 0)
        {
            return true;
        }

        var previous = first;
        for (int i = 1; i < animation.KeyFrames.Count; i++)
        {
            if (animation.KeyFrames[i] is not KeyFrame<float> next
                || !float.IsFinite(next.Value)
                || next.Value < 0
                || !next.Easing.TryGetOutputRange(out float easingMinimum, out float easingMaximum)
                || !float.IsFinite(easingMinimum)
                || !float.IsFinite(easingMaximum)
                || easingMinimum > easingMaximum)
            {
                return true;
            }

            float delta = next.Value - previous.Value;
            float intervalMinimum = delta >= 0
                ? previous.Value + easingMinimum * delta
                : previous.Value + easingMaximum * delta;
            if (!float.IsFinite(intervalMinimum) || intervalMinimum < 0)
                return true;

            previous = next;
        }

        return false;
    }

    private TimeSpan CalculateTimeWithSpeed(TimeSpan timeSpan, Resource resource)
    {
        var anm = Speed.Animation;
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation)
            return timeSpan;

        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return TimeSpan.FromTicks((long)(timeSpan.Ticks * (resource.Speed / 100.0)));
        }

        resource.SpeedIntegrator.EnsureCache(anm);
        return resource.SpeedIntegrator.Integrate(timeSpan, keyFrameAnimation);
    }

    private TimeSpan CalculateTargetBaseTime(TimeSpan currentTime, Resource resource, Drawable? targetDrawable)
    {
        if (targetDrawable == null)
            return currentTime;

        TimeSpan targetStart = targetDrawable.TimeRange.Start;
        TimeSpan targetDuration = targetDrawable.TimeRange.Duration;

        if (targetDuration <= TimeSpan.Zero)
            return currentTime;

        // 相対的な時間
        TimeSpan baseTime = currentTime - TimeRange.Start;

        // 1. AdjustTimeRange: baseTime = currentTime - Target's Start
        if (resource.AdjustTimeRange)
        {
            baseTime = currentTime - targetStart;
        }

        // 2. OffsetPosition
        baseTime += resource.OffsetPosition;

        // 3. Speed: reflect speed changes via integration
        // SpeedIntegrator.Integrate(t) は「時刻 0 から t までの累積積分」を返すため、
        // UseGlobalClock=true でグローバル時刻を渡す場合は要素開始 (TimeRange.Start) 時点の
        // 積分を差し引いて要素ローカルから見た累積に揃える。
        var anm = Speed.Animation;
        if (anm is KeyFrameAnimation<float> keyFrameAnimation && keyFrameAnimation.KeyFrames.Count > 0)
        {
            TimeSpan speedTime = CalculateSpeedClockTime(currentTime, resource, targetDrawable, keyFrameAnimation);
            if (keyFrameAnimation.UseGlobalClock)
            {
                baseTime = CalculateTimeWithSpeed(speedTime, resource)
                         - CalculateTimeWithSpeed(TimeRange.Start, resource);
            }
            else
            {
                baseTime = CalculateTimeWithSpeed(speedTime, resource);
            }
        }
        else
        {
            baseTime = TimeSpan.FromTicks((long)(baseTime.Ticks * (resource.Speed / 100.0)));
        }

        // 4. Reverse: time = targetDuration - time
        if (resource.Reverse)
        {
            baseTime = targetDuration - baseTime;
        }

        return baseTime;
    }

    private TimeSpan CalculateSpeedClockTime(
        TimeSpan currentTime, Resource resource, Drawable targetDrawable, KeyFrameAnimation<float> animation)
    {
        TimeSpan baseTime = currentTime - TimeRange.Start;
        if (resource.AdjustTimeRange)
            baseTime = currentTime - targetDrawable.TimeRange.Start;

        baseTime += resource.OffsetPosition;
        return animation.UseGlobalClock ? baseTime + TimeRange.Start : baseTime;
    }

    /// <summary>
    /// Main time calculation (follows the order defined in the design document).
    /// </summary>
    private TimeSpan CalculateTargetTime(TimeSpan currentTime, Resource resource, Drawable? targetDrawable)
    {
        if (targetDrawable == null)
            return currentTime;

        TimeSpan targetStart = targetDrawable.TimeRange.Start;
        TimeSpan targetDuration = targetDrawable.TimeRange.Duration;
        if (targetDuration <= TimeSpan.Zero)
            return currentTime;

        TimeSpan baseTime = CalculateTargetBaseTime(currentTime, resource, targetDrawable);

        return CalculateTargetTimeFromBaseTime(baseTime, targetStart, targetDuration, resource);
    }

    private static TimeSpan CalculateTargetTimeFromBaseTime(
        TimeSpan baseTime,
        TimeSpan targetStart,
        TimeSpan targetDuration,
        Resource resource)
    {

        // 5. Loop: time = time % targetDuration
        if (resource.Loop && targetDuration > TimeSpan.Zero)
        {
            if (baseTime >= TimeSpan.Zero)
            {
                baseTime = TimeSpan.FromTicks(baseTime.Ticks % targetDuration.Ticks);
            }
            else
            {
                // For negative values, add the duration before applying modulo
                var positiveTicks = targetDuration.Ticks + (baseTime.Ticks % targetDuration.Ticks);
                baseTime = TimeSpan.FromTicks(positiveTicks % targetDuration.Ticks);
            }
        }

        // 6. HoldFirstFrame/HoldLastFrame: clamp out-of-range time
        if (resource.HoldFirstFrame && baseTime < TimeSpan.Zero)
        {
            baseTime = TimeSpan.Zero;
        }

        if (resource.HoldLastFrame && baseTime > targetDuration)
        {
            baseTime = targetDuration;
        }

        // 7. FrameRate: quantize (0 = disabled)
        if (resource.FrameRate > 0)
        {
            double frameNum = baseTime.TotalSeconds * resource.FrameRate;
            baseTime = TimeSpan.FromSeconds(Math.Floor(frameNum) / resource.FrameRate);
        }

        // Convert to absolute time by adding Target's Start
        return targetStart + baseTime;
    }

    /// <summary>
    /// Calculates the timeline duration required to traverse a target-time interval.
    /// </summary>
    public TimeSpan CalculateTimelineDuration(
        TimeSpan start,
        TimeSpan targetDuration,
        TimeSpan maximumTimelineDuration,
        Drawable targetDrawable,
        Resource resource,
        bool reverse = false)
    {
        ArgumentNullException.ThrowIfNull(targetDrawable);
        ArgumentNullException.ThrowIfNull(resource);

        if (targetDuration <= TimeSpan.Zero)
            return TimeSpan.Zero;
        if (targetDuration == TimeSpan.MaxValue)
            return TimeSpan.MaxValue;
        if (maximumTimelineDuration <= TimeSpan.Zero)
            return TimeSpan.MaxValue;
        if (targetDrawable.TimeRange.Duration <= TimeSpan.Zero)
            return TimeSpan.MaxValue;

        TimeSpan traversalLimit = GetMaximumDurationFrom(start, reverse);
        if (maximumTimelineDuration < traversalLimit)
            traversalLimit = maximumTimelineDuration;

        bool heldAtTail = IsValidHeldTail(resource, reverse)
            && CanKeepTraversalDirection(
                new TimeRange(start, TimeSpan.Zero),
                targetDrawable,
                resource,
                reverse);
        if (resource.Loop || heldAtTail)
        {
            TimeSpan boundary = CalculateTargetBoundary(start, targetDrawable, resource, reverse);
            if (boundary <= TimeSpan.Zero
                || resource.Loop && targetDuration >= boundary
                || heldAtTail && targetDuration > boundary)
                return TimeSpan.MaxValue;
        }

        double speed = resource.Speed / 100.0;
        KeyFrameAnimation<float>? animation = Speed.Animation as KeyFrameAnimation<float>;
        if (animation is not { KeyFrames.Count: > 0 })
        {
            if (speed <= 0)
                return TimeSpan.MaxValue;
            if (resource.FrameRate <= 0
                && !resource.HoldFirstFrame
                && !resource.HoldLastFrame)
            {
                TimeSpan duration = ScaleDuration(targetDuration, 1 / speed);
                return duration <= traversalLimit ? duration : TimeSpan.MaxValue;
            }
        }

        if (animation is { KeyFrames.Count: > 0 })
        {
            TimeSpan animationStart = CalculateSpeedClockTime(start, resource, targetDrawable, animation);
            bool hasPositiveSpeed = reverse
                ? HasPositiveSpeedAtOrBefore(animation, animationStart)
                : HasPositiveSpeedAtOrAfter(animation, animationStart);
            if (!hasPositiveSpeed)
                return TimeSpan.MaxValue;
        }

        TimeSpan targetAtStart = CalculateTargetDistanceEndpoint(start, resource, targetDrawable);
        TimeSpan high = animation is not { KeyFrames.Count: > 0 } && resource.FrameRate > 0
            ? ScaleDuration(
                AddDurationSaturated(targetDuration, GetFrameDuration(resource.FrameRate)),
                1 / speed)
            : targetDuration;
        if (high <= TimeSpan.Zero)
            high = TimeSpan.FromTicks(1);
        if (high > traversalLimit)
            high = traversalLimit;

        if (animation is { KeyFrames.Count: > 0 }
            && !TryGetTimelineUpperBound(
                start,
                targetDuration,
                targetAtStart,
                traversalLimit,
                resource,
                targetDrawable,
                animation,
                reverse,
                out high))
        {
            return TimeSpan.MaxValue;
        }

        TimeSpan consumed = CalculateTargetDistance(
            start, high, targetAtStart, resource, targetDrawable, reverse, animation);
        for (int i = 0;
            (consumed < targetDuration || resource.FrameRate > 0 && consumed == targetDuration)
            && high < traversalLimit && i < 20; i++)
        {
            long maximumTicks = traversalLimit.Ticks;
            long nextTicks = high.Ticks > maximumTicks / 2 ? maximumTicks : high.Ticks * 2;
            if (nextTicks == high.Ticks)
                break;

            high = TimeSpan.FromTicks(nextTicks);
            consumed = CalculateTargetDistance(
                start, high, targetAtStart, resource, targetDrawable, reverse, animation);
        }

        if (consumed < targetDuration)
            return TimeSpan.MaxValue;

        TimeSpan low = TimeSpan.Zero;
        for (int i = 0; i < 50; i++)
        {
            long middleTicks = low.Ticks + (high.Ticks - low.Ticks) / 2;
            TimeSpan middle = TimeSpan.FromTicks(middleTicks);
            if (CalculateTargetDistance(start, middle, targetAtStart, resource, targetDrawable, reverse, animation)
                <= targetDuration)
                low = middle;
            else
                high = middle;
        }

        return low;
    }

    public TimeSpan CalculateTimelineDuration(
        TimeSpan start,
        TimeSpan targetDuration,
        TimeSpan maximumTimelineDuration,
        Drawable targetDrawable,
        bool reverse = false)
    {
        using var resource = (Resource)ToResource(new CompositionContext(start));
        return CalculateTimelineDuration(
            start,
            targetDuration,
            maximumTimelineDuration,
            targetDrawable,
            resource,
            reverse);
    }

    private static bool IsValidHeldTail(Resource resource, bool reverse)
    {
        return (reverse ^ resource.Reverse) && resource.HoldFirstFrame;
    }

    private TimeSpan CalculateTargetBoundary(
        TimeSpan start,
        Drawable targetDrawable,
        Resource resource,
        bool reverse)
    {
        TimeSpan targetDuration = targetDrawable.TimeRange.Duration;
        TimeSpan baseTime = CalculateTargetBaseTime(start, resource, targetDrawable);
        bool outputReversed = reverse ^ resource.Reverse;

        if (resource.Loop)
        {
            TimeSpan normalized = NormalizeLoopTime(baseTime, targetDuration);
            return outputReversed ? normalized : targetDuration - normalized;
        }

        return outputReversed
            ? baseTime <= TimeSpan.Zero ? TimeSpan.Zero : baseTime
            : baseTime >= targetDuration ? TimeSpan.Zero : targetDuration - baseTime;
    }

    private TimeSpan CalculateTargetDistance(
        TimeSpan start,
        TimeSpan duration,
        TimeSpan targetAtStart,
        Resource resource,
        Drawable targetDrawable,
        bool reverse,
        KeyFrameAnimation<float>? animation = null)
    {
        if (duration == TimeSpan.MaxValue || duration > GetMaximumDurationFrom(start, reverse))
            return TimeSpan.MaxValue;

        TimeSpan targetAtEnd = animation is { KeyFrames.Count: > 0 }
            ? CalculateTargetEndpointBounded(
                start, duration, resource, targetDrawable, animation, reverse)
            : CalculateTargetDistanceEndpoint(
                MoveTime(start, duration, reverse), resource, targetDrawable);
        double ticks = Math.Abs((double)targetAtEnd.Ticks - targetAtStart.Ticks);
        return ticks >= TimeSpan.MaxValue.Ticks
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks((long)ticks);
    }

    private TimeSpan CalculateTargetEndpointBounded(
        TimeSpan start,
        TimeSpan duration,
        Resource resource,
        Drawable targetDrawable,
        KeyFrameAnimation<float> animation,
        bool reverse)
    {
        TimeSpan baseTime = CalculateTargetBaseTimeBounded(
            start, duration, resource, targetDrawable, animation, reverse);
        return CalculateTargetDistanceEndpointFromBaseTime(
            baseTime,
            targetDrawable.TimeRange.Start,
            targetDrawable.TimeRange.Duration,
            resource);
    }

    private TimeSpan CalculateTargetBaseTimeBounded(
        TimeSpan start,
        TimeSpan duration,
        Resource resource,
        Drawable targetDrawable,
        KeyFrameAnimation<float> animation,
        bool reverse)
    {
        TimeSpan animationStart = CalculateSpeedClockTime(start, resource, targetDrawable, animation);
        if (reverse)
        {
            // SpeedIntegrator integrates only from clock zero. Once a reverse traversal
            // reaches that origin, earlier clock values remain at the same accumulated time.
            TimeSpan clockOriginDuration = animationStart > TimeSpan.Zero
                ? animationStart
                : TimeSpan.Zero;
            if (duration > clockOriginDuration)
            {
                return CalculateTargetBaseTime(
                    MoveTime(start, clockOriginDuration, reverse),
                    resource,
                    targetDrawable);
            }
        }

        TimeSpan terminalDuration;
        KeyFrame<float> terminalFrame;
        if (reverse)
        {
            if (animation.KeyFrames[0] is not KeyFrame<float> first)
                return CalculateTargetBaseTime(MoveTime(start, duration, reverse), resource, targetDrawable);

            terminalFrame = first;
            TimeSpan terminal = first.KeyTime < animationStart ? first.KeyTime : animationStart;
            terminalDuration = animationStart - terminal;
        }
        else
        {
            if (animation.KeyFrames[^1] is not KeyFrame<float> last)
                return CalculateTargetBaseTime(MoveTime(start, duration, reverse), resource, targetDrawable);

            terminalFrame = last;
            TimeSpan terminal = last.KeyTime > animationStart ? last.KeyTime : animationStart;
            terminalDuration = terminal - animationStart;
        }

        TimeSpan endpoint = MoveTime(start, duration, reverse);
        if (duration <= terminalDuration)
            return CalculateTargetBaseTime(endpoint, resource, targetDrawable);

        TimeSpan terminalTime = MoveTime(start, terminalDuration, reverse);
        TimeSpan terminalClock = CalculateSpeedClockTime(
            terminalTime, resource, targetDrawable, animation);
        TimeSpan endpointClock = CalculateSpeedClockTime(
            endpoint, resource, targetDrawable, animation);
        TimeSpan terminalBase = CalculateTargetBaseTime(terminalTime, resource, targetDrawable);
        double tailTicks = (endpointClock - terminalClock).Ticks * (terminalFrame.Value / 100.0);
        double baseTicks = terminalBase.Ticks + (resource.Reverse ? -tailTicks : tailTicks);
        return FromTicksSaturated(baseTicks);
    }

    private TimeSpan CalculateTargetDistanceEndpoint(TimeSpan time, Resource resource, Drawable targetDrawable)
    {
        TimeSpan baseTime = CalculateTargetBaseTime(time, resource, targetDrawable);
        return CalculateTargetDistanceEndpointFromBaseTime(
            baseTime,
            targetDrawable.TimeRange.Start,
            targetDrawable.TimeRange.Duration,
            resource);
    }

    private static TimeSpan CalculateTargetDistanceEndpointFromBaseTime(
        TimeSpan baseTime,
        TimeSpan targetStart,
        TimeSpan targetDuration,
        Resource resource)
    {
        if (resource.Loop)
        {
            return resource.FrameRate > 0
                ? QuantizeLoopedBaseTime(baseTime, targetDuration, resource)
                : baseTime;
        }

        return resource.FrameRate > 0
            || resource.HoldFirstFrame
            || resource.HoldLastFrame
            ? CalculateTargetTimeFromBaseTime(baseTime, targetStart, targetDuration, resource)
            : baseTime;
    }

    private static TimeSpan QuantizeLoopedBaseTime(
        TimeSpan baseTime,
        TimeSpan targetDuration,
        Resource resource)
    {
        long cycles = Math.DivRem(baseTime.Ticks, targetDuration.Ticks, out long phaseTicks);
        if (phaseTicks < 0)
        {
            cycles--;
            phaseTicks += targetDuration.Ticks;
        }

        TimeSpan quantizedPhase = CalculateTargetTimeFromBaseTime(
            TimeSpan.FromTicks(phaseTicks),
            TimeSpan.Zero,
            targetDuration,
            resource);
        double unwrappedTicks = cycles * (double)targetDuration.Ticks + quantizedPhase.Ticks;
        return FromTicksSaturated(unwrappedTicks);
    }

    private bool TryGetTimelineUpperBound(
        TimeSpan start,
        TimeSpan targetDuration,
        TimeSpan targetAtStart,
        TimeSpan maximumTimelineDuration,
        Resource resource,
        Drawable targetDrawable,
        KeyFrameAnimation<float> animation,
        bool reverse,
        out TimeSpan high)
    {
        TimeSpan animationStart = CalculateSpeedClockTime(start, resource, targetDrawable, animation);
        high = TimeSpan.Zero;

        KeyFrame<float>? terminalFrame;
        TimeSpan terminalDuration;
        TimeSpan elapsed;
        if (reverse)
        {
            if (animation.KeyFrames[0] is not KeyFrame<float> first)
                return false;

            terminalFrame = first;
            TimeSpan terminal = first.KeyTime < animationStart ? first.KeyTime : animationStart;
            terminalDuration = animationStart - terminal;
            TimeSpan clockOriginDuration = animationStart > TimeSpan.Zero
                ? animationStart
                : TimeSpan.Zero;
            if (terminalDuration > clockOriginDuration)
                terminalDuration = clockOriginDuration;

            elapsed = terminalDuration;
        }
        else
        {
            if (animation.KeyFrames[^1] is not KeyFrame<float> last)
                return false;

            terminalFrame = last;
            TimeSpan terminal = last.KeyTime > animationStart ? last.KeyTime : animationStart;
            terminalDuration = terminal - animationStart;
            elapsed = terminalDuration;
        }

        TimeSpan probeLimit = terminalDuration < maximumTimelineDuration
            ? terminalDuration
            : maximumTimelineDuration;
        elapsed = GetInitialProbe(
            targetDuration,
            start,
            resource,
            targetDrawable,
            animation,
            probeLimit);
        TimeSpan consumed = CalculateTargetDistance(
            start, elapsed, targetAtStart, resource, targetDrawable, reverse, animation);
        while (consumed < targetDuration && elapsed < probeLimit)
        {
            elapsed = GrowProbe(elapsed, probeLimit);
            consumed = CalculateTargetDistance(
                start, elapsed, targetAtStart, resource, targetDrawable, reverse, animation);
        }

        if (consumed >= targetDuration)
        {
            high = elapsed;
            return true;
        }

        if (probeLimit < terminalDuration)
            return false;

        if (terminalFrame.Value <= 0)
            return false;

        double remainingTicks = (targetDuration - consumed).Ticks / (terminalFrame.Value / 100.0);
        long maximumTicks = maximumTimelineDuration.Ticks;
        if (reverse)
        {
            TimeSpan clockOriginDuration = animationStart > TimeSpan.Zero
                ? animationStart
                : TimeSpan.Zero;
            maximumTicks = Math.Min(maximumTicks, clockOriginDuration.Ticks);
        }

        if (remainingTicks >= maximumTicks - elapsed.Ticks)
        {
            if (reverse)
                return false;

            high = TimeSpan.FromTicks(maximumTicks);
        }
        else
        {
            high = TimeSpan.FromTicks(elapsed.Ticks + (long)remainingTicks);
        }

        return true;
    }

    private TimeSpan GetInitialProbe(
        TimeSpan targetDuration,
        TimeSpan start,
        Resource resource,
        Drawable targetDrawable,
        KeyFrameAnimation<float> animation,
        TimeSpan maximum)
    {
        TimeSpan estimate = EstimateTimelineDuration(
            targetDuration,
            start,
            resource,
            targetDrawable,
            animation);
        TimeSpan probe = TimeSpan.FromSeconds(1);
        if (estimate < probe)
            probe = estimate;
        return probe < maximum ? probe : maximum;
    }

    private static TimeSpan GrowProbe(TimeSpan current, TimeSpan maximum)
    {
        if (current >= maximum)
            return maximum;

        long nextTicks = current.Ticks > maximum.Ticks / 2
            ? maximum.Ticks
            : current.Ticks * 2;
        return TimeSpan.FromTicks(nextTicks);
    }

    private TimeSpan EstimateTimelineDuration(
        TimeSpan targetDuration,
        TimeSpan start,
        Resource resource,
        Drawable targetDrawable,
        KeyFrameAnimation<float> animation)
    {
        TimeSpan animationStart = CalculateSpeedClockTime(start, resource, targetDrawable, animation);
        float speed = animation.Interpolate(animationStart);
        if (!(speed > 0))
            return targetDuration;

        TimeSpan estimatedTargetDuration = resource.FrameRate > 0
            ? AddDurationSaturated(targetDuration, GetFrameDuration(resource.FrameRate))
            : targetDuration;
        TimeSpan estimate = ScaleDuration(estimatedTargetDuration, 100.0 / speed);
        return estimate > TimeSpan.Zero ? estimate : TimeSpan.FromTicks(1);
    }

    private static TimeSpan GetMaximumDurationFrom(TimeSpan start, bool reverse)
    {
        if (!reverse)
        {
            return start.Ticks <= 0
                ? TimeSpan.MaxValue
                : TimeSpan.FromTicks(TimeSpan.MaxValue.Ticks - start.Ticks);
        }

        return start.Ticks >= 0
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks(start.Ticks - TimeSpan.MinValue.Ticks);
    }

    private static TimeSpan MoveTime(TimeSpan start, TimeSpan duration, bool reverse)
    {
        if (!reverse)
            return TimeSpan.FromTicks(start.Ticks + duration.Ticks);

        if (start.Ticks < 0 && duration.Ticks > start.Ticks - TimeSpan.MinValue.Ticks)
            return TimeSpan.MinValue;

        return TimeSpan.FromTicks(start.Ticks - duration.Ticks);
    }

    private static TimeSpan ScaleDuration(TimeSpan duration, double scale)
    {
        double ticks = duration.Ticks * scale;
        return ticks >= TimeSpan.MaxValue.Ticks
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks((long)ticks);
    }

    private static TimeSpan AddDurationSaturated(TimeSpan left, TimeSpan right)
    {
        if (right.Ticks >= TimeSpan.MaxValue.Ticks - left.Ticks)
            return TimeSpan.MaxValue;

        return TimeSpan.FromTicks(left.Ticks + right.Ticks);
    }

    private static TimeSpan GetFrameDuration(float frameRate)
    {
        double ticks = TimeSpan.TicksPerSecond / frameRate;
        if (ticks >= TimeSpan.MaxValue.Ticks)
            return TimeSpan.MaxValue;

        return TimeSpan.FromTicks(Math.Max(1L, (long)Math.Ceiling(ticks)));
    }

    private static TimeSpan FromTicksSaturated(double ticks)
    {
        if (ticks >= TimeSpan.MaxValue.Ticks)
            return TimeSpan.MaxValue;
        if (ticks <= TimeSpan.MinValue.Ticks)
            return TimeSpan.MinValue;

        return TimeSpan.FromTicks((long)ticks);
    }

    private static bool HasPositiveSpeedAtOrAfter(KeyFrameAnimation<float> animation, TimeSpan start)
    {
        if (animation.Interpolate(start) is float current && current > 0)
            return true;

        foreach (IKeyFrame keyFrame in animation.KeyFrames)
        {
            if (keyFrame is KeyFrame<float> speed && speed.KeyTime > start && speed.Value > 0)
                return true;
        }

        return false;
    }

    private static bool HasPositiveSpeedAtOrBefore(KeyFrameAnimation<float> animation, TimeSpan start)
    {
        if (animation.Interpolate(start) is float current && current > 0)
            return true;

        foreach (IKeyFrame keyFrame in animation.KeyFrames)
        {
            if (keyFrame is KeyFrame<float> speed && speed.KeyTime < start && speed.Value > 0)
                return true;
        }

        return false;
    }

    private static TimeSpan NormalizeLoopTime(TimeSpan value, TimeSpan duration)
    {
        long ticks = value.Ticks % duration.Ticks;
        if (ticks < 0)
            ticks += duration.Ticks;
        return TimeSpan.FromTicks(ticks);
    }

    /// <summary>
    /// Gets whether this controller reverses the target timeline over the requested interval.
    /// </summary>
    public bool IsReversed(TimeRange timeRange, Drawable targetDrawable)
    {
        ArgumentNullException.ThrowIfNull(targetDrawable);
        TimeSpan sampleTime = timeRange.IsEmpty
            ? timeRange.Start
            : timeRange.Start + TimeSpan.FromTicks(timeRange.Duration.Ticks / 2);
        return Reverse.GetValue(new CompositionContext(sampleTime));
    }

    /// <summary>
    /// Reports whether the mapped interval can continue to provide target content without a
    /// finite tail bound.
    /// </summary>
    public bool HasUnboundedTail(TimeRange timeRange, Drawable targetDrawable, bool reverse = false)
    {
        using var resource = (Resource)ToResource(new CompositionContext(GetSampleTime(timeRange)));
        return HasUnboundedTail(timeRange, targetDrawable, resource, reverse);
    }

    private bool HasUnboundedTail(
        TimeRange timeRange,
        Drawable targetDrawable,
        Resource resource,
        bool reverse)
    {
        ArgumentNullException.ThrowIfNull(targetDrawable);
        ArgumentNullException.ThrowIfNull(resource);

        if (targetDrawable.TimeRange.Duration <= TimeSpan.Zero)
            return false;

        if (resource.Loop)
        {
            if (!CanKeepTraversalDirection(timeRange, targetDrawable, resource, reverse))
                return false;

            if (HasStationaryTail(timeRange, targetDrawable, resource, reverse))
                return true;

            return CalculateTargetTimeRange(timeRange, targetDrawable, resource) == targetDrawable.TimeRange;
        }

        if (!IsValidHeldTail(resource, reverse)
            || !CanKeepTraversalDirection(timeRange, targetDrawable, resource, reverse))
            return false;

        TimeSpan tail = reverse ? timeRange.Start : timeRange.End;
        TimeSpan targetAtTail = CalculateTargetTime(tail, resource, targetDrawable);
        return targetAtTail <= targetDrawable.TimeRange.Start;
    }

    private bool HasStationaryTail(
        TimeRange timeRange,
        Drawable targetDrawable,
        Resource resource,
        bool reverse)
    {
        TimeSpan tail = reverse ? timeRange.Start : timeRange.End;
        if (Speed.Animation is KeyFrameAnimation<float> animation)
        {
            if (animation.KeyFrames.Count == 0)
                return resource.Speed == 0;

            TimeSpan clock = CalculateSpeedClockTime(tail, resource, targetDrawable, animation);
            if (reverse && clock <= TimeSpan.Zero)
                return true;

            TimeSpan lastKeyTime = animation.KeyFrames[^1].KeyTime;
            TimeSpan first = reverse ? TimeSpan.Zero : clock;
            TimeSpan last = reverse
                ? clock
                : lastKeyTime >= clock ? lastKeyTime : clock;
            return animation.TryGetOutputRange(
                    new TimeRange(first, last - first),
                    out float minimum,
                    out float maximum)
                && minimum == 0
                && maximum == 0;
        }

        return Speed.Animation == null && resource.Speed == 0;
    }

    private bool CanKeepTraversalDirection(
        TimeRange timeRange,
        Drawable targetDrawable,
        Resource resource,
        bool reverse)
    {
        if (Speed.HasExpression)
            return false;

        if (Speed.Animation is KeyFrameAnimation<float> animation)
        {
            if (animation.KeyFrames.Count == 0)
                return float.IsFinite(resource.Speed) && resource.Speed >= 0;

            TimeSpan tail = reverse ? timeRange.Start : timeRange.End;
            TimeSpan clock = CalculateSpeedClockTime(tail, resource, targetDrawable, animation);
            return !SpeedAnimationMayRunBackwardFrom(animation, clock, reverse);
        }

        return Speed.Animation == null
            && float.IsFinite(resource.Speed)
            && resource.Speed >= 0;
    }

    private static bool SpeedAnimationMayRunBackwardFrom(
        KeyFrameAnimation<float> animation,
        TimeSpan clock,
        bool reverse)
    {
        if (clock < TimeSpan.Zero)
            clock = TimeSpan.Zero;
        if (reverse && clock == TimeSpan.Zero)
            return false;

        TimeSpan first = reverse ? TimeSpan.Zero : clock;
        TimeSpan lastKeyTime = animation.KeyFrames[^1].KeyTime;
        TimeSpan last = reverse
            ? clock
            : lastKeyTime >= clock ? lastKeyTime : clock;
        return !animation.TryGetOutputRange(
                new TimeRange(first, last - first),
                out float minimum,
                out _)
            || !float.IsFinite(minimum)
            || minimum < 0;
    }

    /// <summary>
    /// Calculates the target-time interval produced while this controller evaluates
    /// <paramref name="timeRange"/>.
    /// </summary>
    public TimeRange CalculateTargetTimeRange(TimeRange timeRange, Drawable targetDrawable, Resource resource)
    {
        ArgumentNullException.ThrowIfNull(targetDrawable);
        ArgumentNullException.ThrowIfNull(resource);

        if (targetDrawable.TimeRange.Duration <= TimeSpan.Zero)
            return timeRange;

        if (SpeedMayRunBackward(timeRange, targetDrawable, resource))
            return targetDrawable.TimeRange;

        if (resource.Loop)
        {
            TimeSpan unwrappedStart = CalculateTargetBaseTime(timeRange.Start, resource, targetDrawable);
            TimeSpan unwrappedEnd = CalculateTargetBaseTime(timeRange.End, resource, targetDrawable);
            double traversedTicks = Math.Abs((double)unwrappedEnd.Ticks - unwrappedStart.Ticks);
            if (traversedTicks >= targetDrawable.TimeRange.Duration.Ticks)
                return targetDrawable.TimeRange;

            TimeSpan normalizedStart = NormalizeLoopTime(unwrappedStart, targetDrawable.TimeRange.Duration);
            TimeSpan normalizedEnd = NormalizeLoopTime(unwrappedEnd, targetDrawable.TimeRange.Duration);
            bool forward = unwrappedEnd >= unwrappedStart;
            bool wraps = forward ? normalizedEnd < normalizedStart : normalizedEnd > normalizedStart;
            if (wraps)
                return targetDrawable.TimeRange;
        }

        TimeSpan start = CalculateTargetTime(timeRange.Start, resource, targetDrawable);
        TimeSpan end = CalculateTargetTime(timeRange.End, resource, targetDrawable);

        TimeSpan rangeStart = start <= end ? start : end;
        TimeSpan rangeEnd = start >= end ? start : end;
        return new TimeRange(rangeStart, rangeEnd - rangeStart);
    }

    public TimeRange CalculateTargetTimeRange(TimeRange timeRange, Drawable targetDrawable)
    {
        using var resource = (Resource)ToResource(new CompositionContext(GetSampleTime(timeRange)));
        return CalculateTargetTimeRange(timeRange, targetDrawable, resource);
    }

    private static TimeSpan GetSampleTime(TimeRange timeRange)
    {
        return timeRange.IsEmpty
            ? timeRange.Start
            : timeRange.Start + TimeSpan.FromTicks(timeRange.Duration.Ticks / 2);
    }

    private bool SpeedMayRunBackward(
        TimeRange timeRange,
        Drawable targetDrawable,
        Resource resource)
    {
        if (Speed.Animation is not KeyFrameAnimation<float> animation
            || animation.KeyFrames.Count == 0)
        {
            return false;
        }

        TimeSpan firstClock = CalculateSpeedClockTime(
            timeRange.Start, resource, targetDrawable, animation);
        TimeSpan secondClock = CalculateSpeedClockTime(
            timeRange.End, resource, targetDrawable, animation);
        TimeSpan rangeStart = firstClock <= secondClock ? firstClock : secondClock;
        TimeSpan rangeEnd = firstClock >= secondClock ? firstClock : secondClock;
        return !animation.TryGetOutputRange(
                new TimeRange(rangeStart, rangeEnd - rangeStart),
                out float minimum,
                out _)
            || !float.IsFinite(minimum)
            || minimum < 0;
    }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        r.Target?.GetOriginal().Render(context, r.Target);
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        return r.Target?.GetOriginal().MeasureInternal(availableSize, r.Target) ?? Size.Empty;
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public partial class Resource
    {
        internal readonly Media.SpeedIntegrator SpeedIntegrator = new(60);
        private Drawable.Resource? _target;

        public Drawable.Resource? Target => _target;

        partial void PostUpdate(DrawableTimeController obj, CompositionContext context)
        {
            Drawable? targetDrawable = null;
            if (context.Flow != null)
            {
                for (int i = 0; i < context.Flow.Count; i++)
                {
                    if (context.Flow[i] is Drawable.Resource d)
                    {
                        targetDrawable = d.GetOriginal();
                        context.Flow.RemoveAt(i);
                        break;
                    }
                }
            }
            else
            {
                targetDrawable = context.Get(obj.Target);
            }

            // Save the original Time
            var originalContextTime = context.Time;
            try
            {
                context.Time = obj.CalculateTargetTime(context.Time, this, targetDrawable);
                bool changed = false;
                ResourceReconciler.ReconcileResource(
                    context: context,
                    value: targetDrawable,
                    field: ref _target,
                    changed: ref changed);
                if (changed)
                    Version++;
            }
            finally
            {
                context.Time = originalContextTime;
            }
        }

        partial void PostDispose(bool disposing)
        {
            _target?.Dispose();
            SpeedIntegrator.Dispose();
        }
    }
}
