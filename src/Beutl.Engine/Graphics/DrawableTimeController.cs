using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics;

[Display(Name = nameof(GraphicsStrings.DrawableTimeController), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DrawableTimeController : Drawable, IPresenter<Drawable>, IFlowOperator
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
        if (targetDrawable.TimeRange.Duration <= TimeSpan.Zero)
            return TimeSpan.MaxValue;

        if (resource.Loop || IsHeldAtTail(resource, reverse))
        {
            TimeSpan boundary = CalculateTargetBoundary(start, targetDrawable, resource, reverse);
            if (boundary <= TimeSpan.Zero || targetDuration >= boundary)
                return TimeSpan.MaxValue;
        }

        double speed = resource.Speed / 100.0;
        KeyFrameAnimation<float>? animation = Speed.Animation as KeyFrameAnimation<float>;
        if (animation is not { KeyFrames.Count: > 0 })
        {
            if (speed <= 0)
                return TimeSpan.MaxValue;
            if (resource.FrameRate <= 0)
                return ScaleDuration(targetDuration, 1 / speed);
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
        TimeSpan high = targetDuration;
        if (animation is { KeyFrames.Count: > 0 }
            && !TryGetTimelineUpperBound(
                start, targetDuration, targetAtStart, resource, targetDrawable, animation, reverse, out high))
        {
            return TimeSpan.MaxValue;
        }

        TimeSpan consumed = CalculateTargetDistance(start, high, targetAtStart, resource, targetDrawable, reverse);
        for (int i = 0; consumed < targetDuration
            && high < GetMaximumDurationFrom(start, reverse) && i < 20; i++)
        {
            long maximumTicks = GetMaximumDurationFrom(start, reverse).Ticks;
            long nextTicks = high.Ticks > maximumTicks / 2 ? maximumTicks : high.Ticks * 2;
            if (nextTicks == high.Ticks)
                break;

            high = TimeSpan.FromTicks(nextTicks);
            consumed = CalculateTargetDistance(start, high, targetAtStart, resource, targetDrawable, reverse);
        }

        if (consumed < targetDuration)
            return TimeSpan.MaxValue;

        TimeSpan low = TimeSpan.Zero;
        for (int i = 0; i < 50; i++)
        {
            long middleTicks = low.Ticks + (high.Ticks - low.Ticks) / 2;
            TimeSpan middle = TimeSpan.FromTicks(middleTicks);
            if (CalculateTargetDistance(start, middle, targetAtStart, resource, targetDrawable, reverse)
                <= targetDuration)
                low = middle;
            else
                high = middle;
        }

        return low;
    }

    private static bool IsHeldAtTail(Resource resource, bool reverse)
    {
        return (reverse ^ resource.Reverse)
            ? resource.HoldFirstFrame
            : resource.HoldLastFrame;
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
        bool reverse)
    {
        if (duration == TimeSpan.MaxValue || duration > GetMaximumDurationFrom(start, reverse))
            return TimeSpan.MaxValue;

        TimeSpan endpoint = MoveTime(start, duration, reverse);
        TimeSpan targetAtEnd = resource.Loop
            ? CalculateTargetBaseTime(endpoint, resource, targetDrawable)
            : resource.FrameRate > 0
                ? CalculateTargetTime(endpoint, resource, targetDrawable)
                : CalculateTargetBaseTime(endpoint, resource, targetDrawable);
        double ticks = Math.Abs((double)targetAtEnd.Ticks - targetAtStart.Ticks);
        return ticks >= TimeSpan.MaxValue.Ticks
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks((long)ticks);
    }

    private TimeSpan CalculateTargetDistanceEndpoint(TimeSpan time, Resource resource, Drawable targetDrawable)
    {
        return resource.Loop
            ? CalculateTargetBaseTime(time, resource, targetDrawable)
            : resource.FrameRate > 0
            ? CalculateTargetTime(time, resource, targetDrawable)
            : CalculateTargetBaseTime(time, resource, targetDrawable);
    }

    private bool TryGetTimelineUpperBound(
        TimeSpan start,
        TimeSpan targetDuration,
        TimeSpan targetAtStart,
        Resource resource,
        Drawable targetDrawable,
        KeyFrameAnimation<float> animation,
        bool reverse,
        out TimeSpan high)
    {
        TimeSpan animationStart = CalculateSpeedClockTime(start, resource, targetDrawable, animation);
        high = TimeSpan.Zero;

        KeyFrame<float>? terminalFrame;
        TimeSpan elapsed;
        if (reverse)
        {
            if (animation.KeyFrames[0] is not KeyFrame<float> first)
                return false;

            terminalFrame = first;
            TimeSpan terminal = first.KeyTime < animationStart ? first.KeyTime : animationStart;
            elapsed = animationStart - terminal;
        }
        else
        {
            if (animation.KeyFrames[^1] is not KeyFrame<float> last)
                return false;

            terminalFrame = last;
            TimeSpan terminal = last.KeyTime > animationStart ? last.KeyTime : animationStart;
            elapsed = terminal - animationStart;
        }

        TimeSpan consumed = CalculateTargetDistance(
            start, elapsed, targetAtStart, resource, targetDrawable, reverse);
        if (consumed >= targetDuration)
        {
            high = elapsed;
            return true;
        }

        if (terminalFrame.Value <= 0)
            return false;

        double remainingTicks = (targetDuration - consumed).Ticks / (terminalFrame.Value / 100.0);
        long maximumTicks = GetMaximumDurationFrom(start, reverse).Ticks;
        if (remainingTicks >= maximumTicks - elapsed.Ticks)
            high = TimeSpan.FromTicks(maximumTicks);
        else
            high = TimeSpan.FromTicks(elapsed.Ticks + (long)remainingTicks);

        return true;
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
    /// Calculates the target-time interval produced while this controller evaluates
    /// <paramref name="timeRange"/>.
    /// </summary>
    public TimeRange CalculateTargetTimeRange(TimeRange timeRange, Drawable targetDrawable, Resource resource)
    {
        ArgumentNullException.ThrowIfNull(targetDrawable);
        ArgumentNullException.ThrowIfNull(resource);

        if (targetDrawable.TimeRange.Duration <= TimeSpan.Zero)
            return timeRange;

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
