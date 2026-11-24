using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;

namespace Beutl.Graphics;

[Display(Name = nameof(GraphicsStrings.SourceVideo), ResourceType = typeof(GraphicsStrings))]
public partial class SourceVideo : Drawable, IOriginalDurationProvider, ITimeMappedSource
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

    public bool TryGetOriginalDuration(out MediaTime timeSpan, DecoderRegistry? decoders = null)
    {
        using var resource = ToResource(new CompositionContext(MediaTime.Zero) { Decoders = decoders });
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

    // Rendering evaluates OffsetPosition and IsLoop expressions, and it wraps the position by the
    // file's length, so a file that Source switches changes the mapping as well.
    bool ITimeMappedSource.HasDynamicMapping => SwitchesFiles || IsLoop.HasExpression || OffsetPosition.HasExpression;

    bool ITimeMappedSource.HasDynamicTimeline => SwitchesFiles;

    private bool SwitchesFiles => AnimationSampler.IsAnimated(Source);

    MediaTime? ITimeMappedSource.GetSourceDuration(DecoderRegistry? decoders)
    {
        using var resource = Source.Value?.ToResource(new CompositionContext(MediaTime.Zero) { Decoders = decoders });
        // An offline reader reports a zero duration; treat it as unknown so a placeholder clip keeps
        // the length the user asked for and stays resizable.
        return resource is { Duration: var duration } && duration > MediaTime.Zero ? duration : null;
    }

    private MediaTime CalculateVideoTime(MediaTime timeSpan, Resource resource)
        => SpeedAdjustedTime.Map(Speed, resource.Speed, resource._speedIntegrator, timeSpan);

    private MediaTime CalculateRequestedPosition(MediaTime time, Resource resource)
    {
        MediaTime start = TimeRange.Value.Start;
        if (Speed.GetKeyFrameAnimation() is KeyFrameAnimation<float> animation)
        {
            return animation.UseGlobalClock.Value
                ? CalculateVideoTime(time, resource) - CalculateVideoTime(start, resource)
                : CalculateVideoTime(time - start, resource);
        }

        return new MediaTime((long)Math.Round((time - start).Flicks * (double)(resource.Speed / 100f)));
    }

    private static MediaTime NormalizeRequestedPosition(Resource resource, MediaTime requested, MediaTime offset, bool normalizeReverse)
    {
        // Loop boundaries are measured in source time, including the offset.
        if (resource.IsLoop && resource.Source is { IsDisposed: false } source && source.Duration > MediaTime.Zero)
            return SpeedAdjustedTime.WrapIntoDuration(requested + offset, source.Duration) - offset;

        return normalizeReverse && requested < MediaTime.Zero
            ? (resource.Source?.Duration ?? MediaTime.Zero) + requested
            : requested;
    }

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

    public partial class Resource
    {
        internal readonly SpeedIntegrator _speedIntegrator = new(60);

        public MediaTime RenderedPosition { get; internal set; }

        public MediaTime RequestedPosition { get; internal set; }

        protected override Size MeasureCore(Size availableSize)
        {
            if (Source?.IsDisposed == false)
            {
                return Source.LogicalFrameSize.ToSize(1);
            }
            else
            {
                return Size.Empty;
            }
        }

        protected override void OnDraw(GraphicsContext2D context)
            => DrawInternal(context);

        internal void DrawInternal(GraphicsContext2D context, MediaTime? previewSourcePosition = null)
        {
            if (Source?.IsDisposed == false)
            {
                MediaTime sourcePosition = previewSourcePosition ?? RequestedPosition + OffsetPosition;
                int frame = Source.GetFrameIndex(sourcePosition);
                context.DrawVideoSource(Source, frame, Brushes.Resource.White, null);
                RenderedPosition = previewSourcePosition.HasValue ? sourcePosition - OffsetPosition : RequestedPosition;
            }
        }

        partial void PostDispose(bool disposing)
        {
            _speedIntegrator.Dispose();
        }

        partial void PostReconcile(SourceVideo obj, CompositionContext context)
        {
            RequestedPosition = NormalizeRequestedPosition(this, obj.CalculateRequestedPosition(context.Time, this),
                OffsetPosition, normalizeReverse: true);

            if (RequestedPosition != RenderedPosition)
            {
                Version++;
            }
        }
    }
}
