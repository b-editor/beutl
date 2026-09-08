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
        return Source.CurrentValue != null && TryGetOriginalDuration(out _);
    }

    public bool TryGetOriginalDuration(out TimeSpan timeSpan)
    {
        using var resource = ToResource(CompositionContext.Default);
        Resource sourceResource = (Resource)resource;
        if (sourceResource.Source is not { } source)
        {
            timeSpan = TimeSpan.Zero;
            return false;
        }

        TimeSpan remainingSourceDuration = source.Duration - OffsetPosition.CurrentValue;
        if (remainingSourceDuration <= TimeSpan.Zero)
        {
            timeSpan = TimeSpan.Zero;
            return false;
        }

        var ts = CalculateOriginalTime(sourceResource, remainingSourceDuration);
        if (ts is { } duration && duration > TimeSpan.Zero)
        {
            timeSpan = duration;
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
            return ScaleStaticVideoTime(timeSpan, resource.Speed);

        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return ScaleStaticVideoTime(timeSpan, resource.Speed);
        }

        resource._speedIntegrator.EnsureCache(anm);
        try
        {
            return resource._speedIntegrator.Integrate(timeSpan, keyFrameAnimation);
        }
        catch (OverflowException)
        {
            return timeSpan < TimeSpan.Zero ? TimeSpan.MinValue : TimeSpan.MaxValue;
        }
    }

    private static TimeSpan ScaleStaticVideoTime(TimeSpan timeSpan, float speed)
    {
        if (!float.IsFinite(speed) || speed < 0)
            return TimeSpan.MaxValue;

        double ticks = timeSpan.Ticks * (speed / 100d);
        if (ticks >= TimeSpan.MaxValue.Ticks)
            return TimeSpan.MaxValue;
        if (ticks <= TimeSpan.MinValue.Ticks)
            return TimeSpan.MinValue;

        return TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>
    /// Calculates the source-time consumption for an interval in the speed animation's clock.
    /// For local-clock animations, <paramref name="start"/> is local elapsed time; for
    /// global-clock animations, it is the absolute timeline time.
    /// </summary>
    public TimeSpan CalculateVideoDuration(TimeSpan start, TimeSpan duration, Resource resource)
    {
        if (Speed.Animation is KeyFrameAnimation<float> { KeyFrames.Count: > 0 } animation)
        {
            if (!TryAddTime(start, duration, out TimeSpan end))
                return duration > TimeSpan.Zero ? TimeSpan.MaxValue : TimeSpan.MinValue;
            TimeSpan earliest = TimeSpan.FromTicks(Math.Min(0, Math.Min(start.Ticks, end.Ticks)));
            TimeSpan latest = TimeSpan.FromTicks(Math.Max(0, Math.Max(start.Ticks, end.Ticks)));
            if ((decimal)latest.Ticks - earliest.Ticks > long.MaxValue
                || SpeedIntegrator.HasInvalidSpeed(animation, new TimeRange(earliest, latest - earliest)))
                return TimeSpan.MaxValue;

            TimeSpan endTime = CalculateVideoTime(end, resource);
            TimeSpan startTime = CalculateVideoTime(start, resource);
            if (endTime == TimeSpan.MaxValue || startTime == TimeSpan.MinValue)
                return TimeSpan.MaxValue;
            if (endTime == TimeSpan.MinValue || startTime == TimeSpan.MaxValue)
                return TimeSpan.MinValue;
            decimal ticks = (decimal)endTime.Ticks - startTime.Ticks;
            return ticks >= long.MaxValue ? TimeSpan.MaxValue
                : ticks <= long.MinValue ? TimeSpan.MinValue
                : TimeSpan.FromTicks((long)ticks);
        }

        return CalculateVideoTime(duration, resource);
    }

    private static bool TryAddTime(TimeSpan left, TimeSpan right, out TimeSpan result)
    {
        if (right > TimeSpan.Zero && left.Ticks > TimeSpan.MaxValue.Ticks - right.Ticks)
        {
            result = TimeSpan.MaxValue;
            return false;
        }
        if (right < TimeSpan.Zero && left.Ticks < TimeSpan.MinValue.Ticks - right.Ticks)
        {
            result = TimeSpan.MinValue;
            return false;
        }

        result = left + right;
        return true;
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
            if (!double.IsFinite(speed) || speed <= 0) return TimeSpan.MaxValue;

            double ticks = sourceDuration.Ticks / speed;
            return ticks >= TimeSpan.MaxValue.Ticks
                ? TimeSpan.MaxValue
                : TimeSpan.FromTicks((long)ticks);
        }

        var animation = (KeyFrameAnimation<float>)Speed.Animation!;
        if (SpeedIntegrator.HasInvalidSpeed(animation, new TimeRange(start, TimeSpan.Zero)))
            return TimeSpan.MaxValue;
        if (!TryGetTimelineUpperBound(start, sourceDuration, resource, animation, out TimeSpan high))
            return TimeSpan.MaxValue;

        if (!TryAddTime(start, high, out TimeSpan end))
            return TimeSpan.MaxValue;
        TimeSpan earliest = TimeSpan.FromTicks(Math.Min(0, Math.Min(start.Ticks, end.Ticks)));
        TimeSpan latest = TimeSpan.FromTicks(Math.Max(0, Math.Max(start.Ticks, end.Ticks)));
        if ((decimal)latest.Ticks - earliest.Ticks > long.MaxValue
            || SpeedIntegrator.HasInvalidSpeed(animation, new TimeRange(earliest, latest - earliest)))
            return TimeSpan.MaxValue;

        TimeSpan consumed = CalculateVideoDurationBounded(start, high, resource, animation);

        if (consumed < sourceDuration) return TimeSpan.MaxValue;

        TimeSpan low = TimeSpan.Zero;
        for (int i = 0; i < 50; i++)
        {
            long middleTicks = low.Ticks + (high.Ticks - low.Ticks) / 2;
            TimeSpan middle = TimeSpan.FromTicks(middleTicks);
            if (CalculateVideoDurationBounded(start, middle, resource, animation) <= sourceDuration)
                low = middle;
            else
                high = middle;
        }

        return low;
    }

    private TimeSpan CalculateVideoDurationBounded(
        TimeSpan start,
        TimeSpan duration,
        Resource resource,
        KeyFrameAnimation<float> animation)
    {
        if (animation.KeyFrames[^1] is not KeyFrame<float> last)
            return CalculateVideoDuration(start, duration, resource);

        TimeSpan prefix = last.KeyTime > start ? last.KeyTime - start : TimeSpan.Zero;
        if (duration <= prefix || last.Value <= 0)
            return CalculateVideoDuration(start, duration, resource);

        TimeSpan consumed = CalculateVideoDuration(start, prefix, resource);
        double tailTicks = (duration - prefix).Ticks * (last.Value / 100.0);
        if (tailTicks >= TimeSpan.MaxValue.Ticks - consumed.Ticks)
            return TimeSpan.MaxValue;

        return TimeSpan.FromTicks(consumed.Ticks + (long)tailTicks);
    }

    private bool TryGetTimelineUpperBound(
        TimeSpan start,
        TimeSpan sourceDuration,
        Resource resource,
        KeyFrameAnimation<float> animation,
        out TimeSpan high)
    {
        high = TimeSpan.Zero;
        if (animation.KeyFrames[^1] is not KeyFrame<float> last)
        {
            return false;
        }
        float terminalSpeed = last.Value;

        TimeSpan terminal = last.KeyTime > start ? last.KeyTime : start;
        TimeSpan terminalDuration = terminal - start;
        TimeSpan elapsed = GetInitialProbe(sourceDuration, start, animation, terminalDuration);
        TimeSpan consumed = CalculateVideoDuration(start, elapsed, resource);
        while (consumed < sourceDuration && elapsed < terminalDuration)
        {
            elapsed = GrowProbe(elapsed, terminalDuration);
            consumed = CalculateVideoDuration(start, elapsed, resource);
        }

        if (consumed >= sourceDuration)
        {
            high = elapsed;
            return true;
        }

        if (terminalSpeed <= 0)
            return false;

        double remainingTicks = (sourceDuration - consumed).Ticks / (terminalSpeed / 100.0);
        if (remainingTicks >= TimeSpan.MaxValue.Ticks - elapsed.Ticks)
        {
            high = start.Ticks <= 0
                ? TimeSpan.MaxValue
                : TimeSpan.FromTicks(TimeSpan.MaxValue.Ticks - start.Ticks);
        }
        else
        {
            high = TimeSpan.FromTicks(elapsed.Ticks + (long)remainingTicks);
        }

        return true;
    }

    private static TimeSpan GetInitialProbe(
        TimeSpan sourceDuration,
        TimeSpan start,
        KeyFrameAnimation<float> animation,
        TimeSpan maximum)
    {
        TimeSpan estimate = EstimateTimelineDuration(sourceDuration, start, animation);
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

    private static TimeSpan EstimateTimelineDuration(
        TimeSpan sourceDuration,
        TimeSpan start,
        KeyFrameAnimation<float> animation)
    {
        float speed = animation.Interpolate(start);
        if (!(speed > 0))
            return sourceDuration;

        double ticks = sourceDuration.Ticks / (speed / 100.0);
        if (ticks >= TimeSpan.MaxValue.Ticks)
            return TimeSpan.MaxValue;

        return TimeSpan.FromTicks(Math.Max(1L, (long)ticks));
    }

    public TimeSpan? CalculateOriginalTime(Resource resource)
    {
        if (resource.Source == null) return null;

        return CalculateOriginalTime(resource, resource.Source.Duration);
    }

    private TimeSpan? CalculateOriginalTime(Resource resource, TimeSpan duration)
    {
        if (resource.Source == null) return null;

        TimeSpan start = Speed.Animation is KeyFrameAnimation<float> { UseGlobalClock: true }
            ? TimeRange.Start
            : TimeSpan.Zero;
        TimeSpan result = CalculateTimelineDuration(start, duration, resource);
        return result > TimeSpan.Zero && result != TimeSpan.MaxValue ? result : null;
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
