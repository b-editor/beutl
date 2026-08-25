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
    public IProperty<TimeSpan> OffsetPosition { get; } = Property.Create<TimeSpan>();

    [Display(Name = nameof(GraphicsStrings.Speed), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IProperty<VideoSource?> Source { get; } = Property.CreateAnimatable<VideoSource?>();

    [Display(Name = nameof(GraphicsStrings.SourceVideo_IsLoop), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> IsLoop { get; } = Property.CreateAnimatable<bool>();

    public bool HasOriginalDuration()
    {
        return Source.CurrentValue != null;
    }

    public bool TryGetOriginalDuration(out TimeSpan timeSpan)
    {
        using var resource = ToResource(CompositionContext.Default);
        var ts = CalculateOriginalTime((Resource)resource);
        // Offset past the media end leaves nothing to restore; match SourceSound's positive guard.
        if (ts.HasValue && ts.Value - OffsetPosition.CurrentValue > TimeSpan.Zero)
        {
            timeSpan = ts.Value - OffsetPosition.CurrentValue;
            return true;
        }
        else
        {
            timeSpan = TimeSpan.Zero;
            return false;
        }
    }

    public void NotifySplitted(bool backward, TimeSpan startDelta, TimeSpan durationDelta)
    {
        if (backward)
        {
            OffsetPosition.CurrentValue += startDelta;
        }
    }

    private TimeSpan CalculateVideoTime(TimeSpan timeSpan, Resource resource)
    {
        var anm = Speed.Animation;
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation)
            return TimeSpan.FromTicks((long)(timeSpan.Ticks * (resource.Speed / 100.0)));

        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return TimeSpan.FromTicks((long)(timeSpan.Ticks * (resource.Speed / 100.0)));
        }

        resource._speedIntegrator.EnsureCache(anm);
        return resource._speedIntegrator.Integrate(timeSpan, keyFrameAnimation);
    }

    /// <summary>
    /// Calculates the source-time consumption for an interval in the speed animation's clock.
    /// For local-clock animations, <paramref name="start"/> is local elapsed time; for
    /// global-clock animations, it is the absolute timeline time.
    /// </summary>
    public TimeSpan CalculateVideoDuration(TimeSpan start, TimeSpan duration, Resource resource)
    {
        if (Speed.Animation is KeyFrameAnimation<float> { KeyFrames.Count: > 0 })
        {
            return CalculateVideoTime(start + duration, resource) - CalculateVideoTime(start, resource);
        }

        return CalculateVideoTime(duration, resource);
    }

    /// <summary>
    /// Calculates how much timeline time can consume the specified source duration.
    /// The start uses the same speed-animation clock as <see cref="CalculateVideoDuration"/>.
    /// </summary>
    public TimeSpan CalculateTimelineDuration(TimeSpan start, TimeSpan sourceDuration, Resource resource)
    {
        if (sourceDuration <= TimeSpan.Zero) return TimeSpan.Zero;

        if (Speed.Animation is not KeyFrameAnimation<float> { KeyFrames.Count: > 0 })
        {
            double speed = resource.Speed / 100.0;
            if (speed <= 0) return TimeSpan.MaxValue;

            double ticks = sourceDuration.Ticks / speed;
            return ticks >= TimeSpan.MaxValue.Ticks
                ? TimeSpan.MaxValue
                : TimeSpan.FromTicks((long)ticks);
        }

        TimeSpan high = sourceDuration;
        TimeSpan consumed = CalculateVideoDuration(start, high, resource);
        for (int i = 0; consumed < sourceDuration && high < TimeSpan.MaxValue; i++)
        {
            long nextTicks = high.Ticks > TimeSpan.MaxValue.Ticks / 2
                ? TimeSpan.MaxValue.Ticks
                : high.Ticks * 2;
            if (nextTicks == high.Ticks) break;

            high = TimeSpan.FromTicks(nextTicks);
            consumed = CalculateVideoDuration(start, high, resource);
            if (i == 20) break;
        }

        if (consumed < sourceDuration) return TimeSpan.MaxValue;

        TimeSpan low = TimeSpan.Zero;
        for (int i = 0; i < 50; i++)
        {
            long middleTicks = low.Ticks + (high.Ticks - low.Ticks) / 2;
            TimeSpan middle = TimeSpan.FromTicks(middleTicks);
            if (CalculateVideoDuration(start, middle, resource) <= sourceDuration)
                low = middle;
            else
                high = middle;
        }

        return low;
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

        partial void PostUpdate(SourceVideo obj, CompositionContext context)
        {
            var time = context.Time;
            // アニメーションがある場合、前回のキーフレームを引く
            // SpeedIntegrator.Integrate は「時刻 0 から入力時刻までの累積積分」を返すため、
            // UseGlobalClock=true でグローバル時刻を渡す場合は要素開始 (obj.TimeRange.Start) 時点の
            // 積分を差し引いて要素ローカルから見た累積に揃える。
            var anm = obj.Speed.Animation;
            if (anm is KeyFrameAnimation<float> keyFrameAnimation)
            {
                if (keyFrameAnimation.UseGlobalClock)
                {
                    RequestedPosition = obj.CalculateVideoTime(time, this)
                                      - obj.CalculateVideoTime(obj.TimeRange.Start, this);
                }
                else
                {
                    RequestedPosition = obj.CalculateVideoTime(time - obj.TimeRange.Start, this);
                }
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
