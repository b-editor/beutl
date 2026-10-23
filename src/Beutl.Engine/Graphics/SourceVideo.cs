using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics;

[Display(Name = nameof(GraphicsStrings.SourceVideo), ResourceType = typeof(GraphicsStrings))]
public partial class SourceVideo : Drawable, IOriginalDurationProvider, ISplittable
{
    public SourceVideo()
    {
        ScanProperties<SourceVideo>();
    }

    [Display(Name = nameof(GraphicsStrings.SourceVideo_OffsetPosition), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<MediaTime> OffsetPosition { get; } = Property.CreateExpression<MediaTime>();

    [Display(Name = nameof(GraphicsStrings.Speed), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<VideoSource?> Source { get; } = Property.CreateAnimatable<VideoSource?>();

    [Display(Name = nameof(GraphicsStrings.SourceVideo_IsLoop), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> IsLoop { get; } = Property.CreateAnimatable<bool>();

    public bool HasOriginalDuration()
    {
        return Source.Value != null;
    }

    public bool TryGetOriginalDuration(out MediaTime timeSpan)
    {
        using var resource = ToResource(CompositionContext.Default);
        var ts = CalculateOriginalTime((Resource)resource);
        if (ts.HasValue && ts.Value - OffsetPosition.Value > MediaTime.Zero)
        {
            timeSpan = ts.Value - OffsetPosition.Value;
            return true;
        }
        else
        {
            timeSpan = MediaTime.Zero;
            return false;
        }
    }

    public void NotifySplitted(bool backward, MediaTime startDelta, MediaTime durationDelta)
    {
        if (backward)
        {
            OffsetPosition.Value += startDelta;
        }
    }

    private MediaTime CalculateVideoTime(MediaTime timeSpan, Resource resource)
        => SpeedAdjustedTime.Map(Speed, resource.Speed, resource._speedIntegrator, timeSpan);

    public MediaTime? CalculateOriginalTime(Resource resource)
    {
        if (resource.Source == null) return null;

        var duration = resource.Source.Duration;

        var anm = Speed.GetKeyFrameAnimation();

        // スピードのアニメーションまたはキーフレームが 1 つもない場合は、単純に逆変換する
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation || keyFrameAnimation.KeyFrames.Count == 0)
        {
            return new MediaTime((long)(duration.Flicks / (Speed.Value / 100.0)));
        }

        // 二分探索で、CalculateVideoTime(t) == duration となる t を求める
        MediaTime low = MediaTime.Zero;
        // 上限は、CalculateVideoTime(high) >= duration となるまで徐々に拡大する
        MediaTime high = duration;
        MediaTime videoTimeAtHigh = CalculateVideoTime(high, resource);
        const int maxIterations = 50;
        const double toleranceSeconds = 1.0 / 60.0; // 1フレーム以下の精度

        // 速度が非常に遅い場合に備えて high を段階的に拡大する
        const int maxHighExpansions = 20;
        int expansionCount = 0;
        while (videoTimeAtHigh < duration
               && expansionCount < maxHighExpansions
               && high <= new MediaTime(MediaTime.MaxValue.Flicks / 2))
        {
            high = new MediaTime(high.Flicks * 2);
            videoTimeAtHigh = CalculateVideoTime(high, resource);
            expansionCount++;
        }

        for (int i = 0; i < maxIterations; i++)
        {
            var mid = new MediaTime(low.Flicks + (high.Flicks - low.Flicks) / 2);
            MediaTime videoTime = CalculateVideoTime(mid, resource);

            if (Math.Abs((videoTime - duration).ToSeconds()) < toleranceSeconds)
            {
                return mid;
            }

            if (videoTime < duration)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return new MediaTime(low.Flicks + (high.Flicks - low.Flicks) / 2);
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Source?.IsDisposed == false)
        {
            return r.Source.LogicalFrameSize.ToSize(1);
        }
        else
        {
            return Size.Empty;
        }
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Source?.IsDisposed == false)
        {
            MediaTime pos = r.RequestedPosition + r.OffsetPosition;
            Rational rate = r.Source.FrameRate;
            // A reader that reports no frame rate has one frame to show.
            int frame = rate.Numerator > 0 && rate.Denominator > 0
                ? (int)pos.ToFrame(rate, MidpointRounding.AwayFromZero)
                : 0;

            context.DrawVideoSource(r.Source, frame, Brushes.Resource.White, null);
            r.RenderedPosition = r.RequestedPosition;
        }
    }

    internal void DrawInternal(GraphicsContext2D context, Drawable.Resource resource)
    {
        OnDraw(context, resource);
    }

    public partial class Resource
    {
        internal readonly SpeedIntegrator _speedIntegrator = new(60);

        public MediaTime RenderedPosition { get; internal set; }

        public MediaTime RequestedPosition { get; internal set; }

        partial void PostDispose(bool disposing)
        {
            _speedIntegrator.Dispose();
        }

        partial void PostReconcile(SourceVideo obj, CompositionContext context)
        {
            var time = context.Time;
            // SpeedIntegrator.Integrate は「時刻 0 から入力時刻までの累積積分」を返すため、
            // UseGlobalClock=true でグローバル時刻を渡す場合は要素開始 (obj.TimeRange.Start) 時点の
            // 積分を差し引いて要素ローカルから見た累積に揃える。
            var anm = obj.Speed.GetKeyFrameAnimation();
            if (anm is KeyFrameAnimation<float> keyFrameAnimation)
            {
                if (keyFrameAnimation.UseGlobalClock.Value)
                {
                    RequestedPosition = obj.CalculateVideoTime(time, this)
                                      - obj.CalculateVideoTime(obj.TimeRange.Value.Start, this);
                }
                else
                {
                    RequestedPosition = obj.CalculateVideoTime(time - obj.TimeRange.Value.Start, this);
                }
            }
            else
            {
                RequestedPosition = new MediaTime((long)Math.Round((time - obj.TimeRange.Value.Start).Flicks * (double)(_speed / 100)));
            }

            if (IsLoop && Source?.IsDisposed == false && Source.Duration > MediaTime.Zero)
            {
                RequestedPosition = SpeedAdjustedTime.WrapIntoDuration(RequestedPosition, Source.Duration);
            }
            else if (RequestedPosition < MediaTime.Zero)
            {
                RequestedPosition = (Source?.Duration ?? MediaTime.Zero) + RequestedPosition;
            }

            if (RequestedPosition != RenderedPosition)
            {
                Version++;
            }
        }
    }
}
