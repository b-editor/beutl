using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

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
        var anm = Speed.Animation;
        if (anm is KeyFrameAnimation<float> keyFrameAnimation && keyFrameAnimation.KeyFrames.Count > 0)
        {
            baseTime = CalculateTimeWithSpeed(
                keyFrameAnimation.UseGlobalClock
                    ? baseTime + TimeRange.Start
                    : baseTime,
                resource);
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
