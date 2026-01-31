using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics;

[Display(Name = nameof(Strings.Video), ResourceType = typeof(Strings))]
public partial class SourceVideo : Drawable
{
    public SourceVideo()
    {
        ScanProperties<SourceVideo>();
    }

    [Display(Name = nameof(Strings.Offset), ResourceType = typeof(Strings))]
    public IProperty<TimeSpan> OffsetPosition { get; } = Property.Create<TimeSpan>();

    [Display(Name = nameof(Strings.Speed), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(Strings.Source), ResourceType = typeof(Strings))]
    public IProperty<VideoSource?> Source { get; } = Property.CreateAnimatable<VideoSource?>();

    [Display(Name = nameof(Strings.IsLoop), ResourceType = typeof(Strings))]
    public IProperty<bool> IsLoop { get; } = Property.CreateAnimatable<bool>();

    private TimeSpan CalculateVideoTime(TimeSpan timeSpan, Resource resource)
    {
        var anm = Speed.Animation;
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation)
            return timeSpan;

        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return TimeSpan.FromTicks((long)(timeSpan.Ticks * (resource.Speed / 100.0)));
        }

        resource._speedIntegrator.EnsureCache(anm);
        return resource._speedIntegrator.Integrate(timeSpan, keyFrameAnimation);
    }

    public TimeSpan? CalculateOriginalTime(Resource resource)
    {
        if (resource.Source == null) return null;

        var duration = resource.Source.Duration;

        var anm = Speed.Animation;

        // スピードのアニメーションまたはキーフレームが 1 つもない場合は、単純に逆変換する
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation || keyFrameAnimation.KeyFrames.Count == 0)
        {
            return TimeSpan.FromTicks((long)(duration.Ticks / (Speed.CurrentValue / 100.0)));
        }

        // 二分探索で、CalculateVideoTime(t) == duration となる t を求める
        TimeSpan low = TimeSpan.Zero;
        // 上限は、CalculateVideoTime(high) >= duration となるまで徐々に拡大する
        TimeSpan high = duration;
        TimeSpan videoTimeAtHigh = CalculateVideoTime(high, resource);
        const int maxIterations = 50;
        const double toleranceSeconds = 1.0 / 60.0; // 1フレーム以下の精度

        // 速度が非常に遅い場合に備えて high を段階的に拡大する
        const int maxHighExpansions = 20;
        int expansionCount = 0;
        while (videoTimeAtHigh < duration
               && expansionCount < maxHighExpansions
               && high <= TimeSpan.FromTicks(TimeSpan.MaxValue.Ticks / 2))
        {
            high = TimeSpan.FromTicks(high.Ticks * 2);
            videoTimeAtHigh = CalculateVideoTime(high, resource);
            expansionCount++;
        }

        for (int i = 0; i < maxIterations; i++)
        {
            TimeSpan mid = TimeSpan.FromTicks((low.Ticks + high.Ticks) / 2);
            TimeSpan videoTime = CalculateVideoTime(mid, resource);

            if (Math.Abs((videoTime - duration).TotalSeconds) < toleranceSeconds)
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

        return TimeSpan.FromTicks((low.Ticks + high.Ticks) / 2);
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Source?.IsDisposed == false)
        {
            return r.Source.FrameSize.ToSize(1);
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
            TimeSpan pos = r.RequestedPosition + r.OffsetPosition;
            Rational rate = r.Source.FrameRate;
            double frameNum = pos.Ticks * rate.Numerator / (double)(TimeSpan.TicksPerSecond * rate.Denominator);

            context.DrawVideoSource(
                r.Source,
                (int)Math.Round(frameNum, MidpointRounding.AwayFromZero),
                Brushes.Resource.White,
                null);
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

        public TimeSpan RenderedPosition { get; internal set; }

        public TimeSpan RequestedPosition { get; internal set; }

        partial void PostDispose(bool disposing)
        {
            _speedIntegrator.Dispose();
        }

        partial void PostUpdate(SourceVideo obj, RenderContext context)
        {
            var time = context.Time;
            // アニメーションがある場合、前回のキーフレームを引く
            var anm = obj.Speed.Animation;
            if (anm is KeyFrameAnimation<float> keyFrameAnimation)
            {
                RequestedPosition = obj.CalculateVideoTime(
                    keyFrameAnimation.UseGlobalClock
                        ? time
                        : time - obj.TimeRange.Start,
                    this);
            }
            else
            {
                RequestedPosition = (time - obj.TimeRange.Start) * (_speed / 100);
            }

            // ループ処理を追加
            if (IsLoop && Source?.IsDisposed == false && Source.Duration > TimeSpan.Zero)
            {
                // 正の値の場合、動画の長さでモジュロ計算
                if (RequestedPosition >= TimeSpan.Zero)
                {
                    RequestedPosition = TimeSpan.FromTicks(RequestedPosition.Ticks % Source.Duration.Ticks);
                }
                // 負の値の場合、動画の長さを足してからモジュロ計算
                else
                {
                    var positiveTicks = Source.Duration.Ticks + (RequestedPosition.Ticks % Source.Duration.Ticks);
                    RequestedPosition = TimeSpan.FromTicks(positiveTicks % Source.Duration.Ticks);
                }
            }
            else if (RequestedPosition < TimeSpan.Zero)
            {
                RequestedPosition = (Source?.Duration ?? TimeSpan.Zero) + RequestedPosition;
            }

            if (RequestedPosition != RenderedPosition)
            {
                Version++;
            }
        }
    }
}
