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
            if (keyFrameAnimation.UseGlobalClock)
            {
                baseTime = CalculateTimeWithSpeed(baseTime + TimeRange.Start, resource)
                         - CalculateTimeWithSpeed(TimeRange.Start, resource);
            }
            else
            {
                baseTime = CalculateTimeWithSpeed(baseTime, resource);
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
        TimeSpan start, TimeSpan targetDuration, Drawable targetDrawable, Resource resource)
    {
        ArgumentNullException.ThrowIfNull(targetDrawable);
        ArgumentNullException.ThrowIfNull(resource);

        if (targetDuration <= TimeSpan.Zero)
            return TimeSpan.Zero;
        if (targetDrawable.TimeRange.Duration <= TimeSpan.Zero)
            return TimeSpan.MaxValue;
        if (resource.Loop || resource.HoldFirstFrame || resource.HoldLastFrame)
            return TimeSpan.MaxValue;

        double speed = resource.Speed / 100.0;
        if (Speed.Animation is not KeyFrameAnimation<float> { KeyFrames.Count: > 0 } animation)
        {
            return speed > 0 ? ScaleDuration(targetDuration, 1 / speed) : TimeSpan.MaxValue;
        }

        TimeSpan animationStart = animation.UseGlobalClock
            ? start
            : start - TimeRange.Start;
        if (!HasPositiveSpeedAtOrAfter(animation, animationStart))
            return TimeSpan.MaxValue;

        TimeSpan targetAtStart = CalculateTargetBaseTime(start, resource, targetDrawable);
        TimeSpan high = targetDuration;
        TimeSpan consumed = CalculateTargetDistance(start, high, targetAtStart, resource, targetDrawable);
        for (int i = 0; consumed < targetDuration && high < TimeSpan.MaxValue; i++)
        {
            long nextTicks = high.Ticks > TimeSpan.MaxValue.Ticks / 2
                ? TimeSpan.MaxValue.Ticks
                : high.Ticks * 2;
            if (nextTicks == high.Ticks)
                break;

            high = TimeSpan.FromTicks(nextTicks);
            consumed = CalculateTargetDistance(start, high, targetAtStart, resource, targetDrawable);
            if (i == 20)
                break;
        }

        if (consumed < targetDuration)
            return TimeSpan.MaxValue;

        TimeSpan low = TimeSpan.Zero;
        for (int i = 0; i < 50; i++)
        {
            long middleTicks = low.Ticks + (high.Ticks - low.Ticks) / 2;
            TimeSpan middle = TimeSpan.FromTicks(middleTicks);
            if (CalculateTargetDistance(start, middle, targetAtStart, resource, targetDrawable) <= targetDuration)
                low = middle;
            else
                high = middle;
        }

        return low;
    }

    private TimeSpan CalculateTargetDistance(
        TimeSpan start,
        TimeSpan duration,
        TimeSpan targetAtStart,
        Resource resource,
        Drawable targetDrawable)
    {
        TimeSpan targetAtEnd = CalculateTargetBaseTime(start + duration, resource, targetDrawable);
        double ticks = Math.Abs((double)targetAtEnd.Ticks - targetAtStart.Ticks);
        return ticks >= TimeSpan.MaxValue.Ticks
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks((long)ticks);
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
