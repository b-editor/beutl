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
        => SpeedAdjustedTime.Map(Speed, resource.Speed, resource.SpeedIntegrator, timeSpan);

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

        // 5. Loop: time = time % targetDuration
        if (resource.Loop && targetDuration > TimeSpan.Zero)
        {
            baseTime = SpeedAdjustedTime.WrapIntoDuration(baseTime, targetDuration);
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
        r.Target?.RequireOriginal().Render(context, r.Target);
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        return r.Target?.RequireOriginal().MeasureInternal(availableSize, r.Target) ?? Size.Empty;
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public partial class Resource
    {
        internal readonly Media.SpeedIntegrator SpeedIntegrator = new(60);
        private Drawable.Resource? _target;
        private FlowNode? _flowTarget;
        private IReadOnlyList<FlowNode> _flowInputs = [];

        internal override IReadOnlyList<FlowNode> FlowInputs => _flowInputs;

        public Drawable.Resource? Target => _target;

        partial void PostUpdate(DrawableTimeController obj, CompositionContext context)
        {
            Drawable? targetDrawable = null;
            FlowNode? flowTarget = null;
            if (context.ReplayedFlow is { } replay && ReferenceEquals(replay.Object, obj))
            {
                flowTarget = replay.Inputs.FirstOrDefault(input => input.Object is Drawable);
                targetDrawable = flowTarget?.Object as Drawable;
            }
            else if (context.Flow != null)
            {
                for (int i = 0; i < context.Flow.Count; i++)
                {
                    if (context.Flow[i] is Drawable.Resource d)
                    {
                        targetDrawable = d.GetOriginal();
                        flowTarget = FlowNode.Capture(d);
                        context.Flow.RemoveAt(i);
                        break;
                    }
                }
            }
            else
            {
                targetDrawable = context.Get(obj.Target);
            }
            if (!ReferenceEquals(flowTarget, _flowTarget))
            {
                _flowTarget = flowTarget;
                _flowInputs = flowTarget == null ? [] : [flowTarget];
            }

            // Save the original Time
            var originalContextTime = context.Time;
            try
            {
                context.Time = obj.CalculateTargetTime(context.Time, this, targetDrawable);
                bool changed = false;
                if (_flowTarget != null)
                    _flowTarget.Reconcile(context, ref _target, ref changed);
                else
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
            _flowTarget = null;
            _flowInputs = [];
            SpeedIntegrator.Dispose();
        }
    }
}
