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
[SuppressResourceClassGeneration]
public partial class SourceVideo : Drawable, IOriginalDurationProvider, ITimeMappedSource
{
    public SourceVideo()
    {
        ScanProperties<SourceVideo>();
        HideProperties(TransferInterpretation, DeinterlaceMode, DeinterlaceFieldOrder, DeinterlaceMethod, DeinterlaceFrameRateMode);
    }

    [Display(Name = nameof(GraphicsStrings.SourceVideo_OffsetPosition), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<MediaTime> OffsetPosition { get; } = Property.CreateExpression<MediaTime>();

    [Display(Name = nameof(GraphicsStrings.Speed), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<VideoSource?> Source { get; } = Property.CreateAnimatable<VideoSource?>();

    [Display(Name = nameof(GraphicsStrings.SourceVideo_TransferInterpretation), ResourceType = typeof(GraphicsStrings))]
    public IProperty<VideoTransferInterpretation> TransferInterpretation { get; } = Property.CreateValue(VideoTransferInterpretation.Auto);

    [Display(Name = nameof(GraphicsStrings.SourceVideo_Deinterlace), ResourceType = typeof(GraphicsStrings))]
    public IProperty<DeinterlaceMode> DeinterlaceMode { get; } = Property.CreateValue(Media.Decoding.DeinterlaceMode.Off);

    [Display(Name = nameof(GraphicsStrings.SourceVideo_DeinterlaceFieldOrder), ResourceType = typeof(GraphicsStrings))]
    public IProperty<DeinterlaceFieldOrder> DeinterlaceFieldOrder { get; } = Property.CreateValue(Media.Decoding.DeinterlaceFieldOrder.Auto);

    [Display(Name = nameof(GraphicsStrings.SourceVideo_DeinterlaceMethod), ResourceType = typeof(GraphicsStrings))]
    public IProperty<DeinterlaceMethod> DeinterlaceMethod { get; } = Property.CreateValue(Media.Decoding.DeinterlaceMethod.Bwdif);

    [Display(Name = nameof(GraphicsStrings.SourceVideo_DeinterlaceFrameRate), ResourceType = typeof(GraphicsStrings))]
    public IProperty<DeinterlaceFrameRateMode> DeinterlaceFrameRateMode { get; } = Property.CreateValue(Media.Decoding.DeinterlaceFrameRateMode.Frame);

    [Display(Name = nameof(GraphicsStrings.SourceVideo_IsLoop), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> IsLoop { get; } = Property.CreateAnimatable<bool>();

    public VideoDeinterlaceOptions GetDeinterlaceOptions()
        => new VideoDeinterlaceOptions(DeinterlaceMode.Value, DeinterlaceFieldOrder.Value,
            DeinterlaceMethod.Value, DeinterlaceFrameRateMode.Value).Normalize();

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool versionBumped = true;
        try
        {
            resource.Reconcile(this, context, ref versionBumped);
            return resource;
        }
        catch
        {
            resource.Dispose();
            throw;
        }
    }

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
        using var resource = Source.Value?.ToResource(
            new CompositionContext(MediaTime.Zero) { Decoders = decoders }, GetDeinterlaceOptions());
        // An offline reader reports a zero duration; treat it as unknown so a placeholder clip keeps
        // the length the user asked for and stays resizable.
        return resource is { Duration: var duration } && duration > MediaTime.Zero ? duration : null;
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

    public new partial class Resource : Drawable.Resource
    {
        internal readonly SpeedIntegrator _speedIntegrator = new(60);
        private MediaTime _offsetPosition;
        private float _speed;
        private VideoTransferInterpretation _transferInterpretation;
        private bool _isLoop;

        public MediaTime OffsetPosition { get => _offsetPosition; set => _offsetPosition = value; }

        public float Speed { get => _speed; set => _speed = value; }

        public VideoSource.Resource? Source { get; set; }

        public VideoTransferInterpretation TransferInterpretation { get => _transferInterpretation; set => _transferInterpretation = value; }

        public bool IsLoop { get => _isLoop; set => _isLoop = value; }

        public new SourceVideo? GetOriginal() => (SourceVideo?)base.GetOriginal();

        public new SourceVideo RequireOriginal() => (SourceVideo)base.RequireOriginal();

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
        {
            if (Source?.IsDisposed == false)
            {
                MediaTime pos = RequestedPosition + OffsetPosition;
                int frame = Source.GetFrameIndex(pos);

                context.DrawVideoSource(Source, frame, Brushes.Resource.White, null, TransferInterpretation);
                RenderedPosition = RequestedPosition;
            }
        }

        internal void DrawInternal(GraphicsContext2D context)
        {
            OnDraw(context);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Source?.Dispose();
                Source = null;
            }

            _speedIntegrator.Dispose();
            base.Dispose(disposing);
        }

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);
            var video = (SourceVideo)obj;
            ResourceReconciler.ReconcileValue(this, context, video.OffsetPosition, ref _offsetPosition, ref versionBumped);
            ResourceReconciler.ReconcileValue(this, context, video.Speed, ref _speed, ref versionBumped);
            ResourceReconciler.ReconcileValue(this, context, video.TransferInterpretation, ref _transferInterpretation, ref versionBumped);
            ResourceReconciler.ReconcileValue(this, context, video.IsLoop, ref _isLoop, ref versionBumped);

            var source = context.Get(video.Source);
            VideoDeinterlaceOptions deinterlace = video.GetDeinterlaceOptions();
            if (source == null ? Source != null : source != Source?.GetOriginal())
            {
                var previous = Source;
                Source = source?.ToResource(context, deinterlace);
                previous?.Dispose();
                BumpVersion(ref versionBumped);
            }
            else if (source != null && Source is { } resource)
            {
                bool sourceChanged = false;
                resource.Reconcile(source, context, deinterlace, ref sourceChanged);
                if (sourceChanged)
                    BumpVersion(ref versionBumped);
            }

            UpdatePosition(video, context.Time);
            if (GetType() == typeof(Resource))
                CompleteReconciliation(context);
        }

        private void UpdatePosition(SourceVideo obj, MediaTime time)
        {
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
                // A frame is read at RequestedPosition + OffsetPosition, so the loop wraps that sum; wrapping the
                // requested position alone read past the end of the source for the last OffsetPosition of each loop.
                RequestedPosition = SpeedAdjustedTime.WrapIntoDuration(RequestedPosition + OffsetPosition, Source.Duration)
                                    - OffsetPosition;
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
