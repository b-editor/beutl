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
    public IProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(Strings.Source), ResourceType = typeof(Strings))]
    public IProperty<IVideoSource?> Source { get; } = Property.CreateAnimatable<IVideoSource?>();

    [Display(Name = nameof(Strings.IsLoop), ResourceType = typeof(Strings))]
    public IProperty<bool> IsLoop { get; } = Property.CreateAnimatable<bool>();

    private TimeSpan CalculateVideoTime(TimeSpan timeSpan, Resource resource)
    {
        // 最初のキーフレームが100, 00:00
        // 2のキーフレームが100, 00:10
        // 3のキーフレームが200, 00:10.033
        // 3の時点では動画の時間は00:10.033が自然
        // でも実際には00:10.033 * 2倍 = 00:20.066になってしまう

        // 手前（KeyTime <= timeSpan）のキーフレームを取得しその時点での自然な動画時間を取得、(CurrentTime - KeyTime) * Speedを加算したものをRequestedPositionに設定

        var anm = Speed.Animation;
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation)
            return timeSpan;

        // キーフレームが無い場合は、グローバルな _speed を使って単純変換する
        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return TimeSpan.FromTicks((long)(timeSpan.Ticks * (resource.Speed / 100.0)));
        }

        int kfi = keyFrameAnimation.KeyFrames.IndexAt(timeSpan);
        var kf = (KeyFrame<float>)keyFrameAnimation.KeyFrames[kfi];

        // 前のキーフレームが存在する場合は、その区間分を再帰的に累積計算する
        if (kfi > 0 &&
            keyFrameAnimation.KeyFrames[kfi - 1] is KeyFrame<float> prevKf)
        {
            var baseVideoTime = CalculateVideoTime(prevKf.KeyTime, resource);
            var deltaTicks = (timeSpan - prevKf.KeyTime).Ticks;
            // kf.Value がマイナスの場合は、deltaTicks に対して乗算すると符号反転し、逆再生となる
            long videoTicks = (long)(deltaTicks * (kf.Value / 100.0));
            return baseVideoTime + TimeSpan.FromTicks(videoTicks);
        }

        // 最初のキーフレームの場合
        return TimeSpan.FromTicks((long)(timeSpan.Ticks * (kf.Value / 100.0)));
    }

    public TimeSpan? CalculateOriginalTime()
    {
        if (Source.CurrentValue?.IsDisposed != false) return null;

        var duration = Source.CurrentValue.Duration;

        var anm = Speed.Animation;

        // スピードのアニメーションまたはキーフレームが 1 つもない場合は、単純に逆変換する
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation || keyFrameAnimation.KeyFrames.Count == 0)
        {
            return TimeSpan.FromTicks((long)(duration.Ticks / (Speed.CurrentValue / 100.0)));
        }

        // 0時点を起点とした各セグメントを構築する
        // セグメントごとに、[startOriginal, endOriginal] と [startVideo, endVideo] の線形関係がある
        var segments =
            new List<(TimeSpan startOriginal, TimeSpan endOriginal, TimeSpan startVideo, TimeSpan endVideo)>();

        TimeSpan currentOriginal = TimeSpan.Zero;
        TimeSpan currentVideo = TimeSpan.Zero;

        // 最初のキーフレームのセグメント
        var firstKf = (KeyFrame<float>)keyFrameAnimation.KeyFrames[0];
        double firstSpeed = firstKf.Value / 100.0;
        TimeSpan firstEndOriginal = firstKf.KeyTime;
        TimeSpan firstEndVideo = currentVideo +
                                 TimeSpan.FromTicks((long)((firstEndOriginal - currentOriginal).Ticks * firstSpeed));
        segments.Add((currentOriginal, firstEndOriginal, currentVideo, firstEndVideo));
        currentOriginal = firstEndOriginal;
        currentVideo = firstEndVideo;

        // 残りのキーフレームのセグメントを構築
        for (int i = 1; i < keyFrameAnimation.KeyFrames.Count; i++)
        {
            var kf = (KeyFrame<float>)keyFrameAnimation.KeyFrames[i];
            double speed = kf.Value / 100.0;
            TimeSpan endOriginal = kf.KeyTime;
            TimeSpan endVideo =
                currentVideo + TimeSpan.FromTicks((long)((endOriginal - currentOriginal).Ticks * speed));
            segments.Add((currentOriginal, endOriginal, currentVideo, endVideo));
            currentOriginal = endOriginal;
            currentVideo = endVideo;
        }

        // 各セグメント内で、duration がどの区間に位置するかを線形補間で求める
        foreach (var seg in segments)
        {
            // セグメント内の映像時間の範囲（正順・逆順の両方に対応）
            TimeSpan minVideo = seg.startVideo < seg.endVideo ? seg.startVideo : seg.endVideo;
            TimeSpan maxVideo = seg.startVideo > seg.endVideo ? seg.startVideo : seg.endVideo;
            if (duration >= minVideo && duration <= maxVideo)
            {
                // 線形補間により、元の実時間を逆算する
                long videoRange = (seg.endVideo - seg.startVideo).Ticks;
                if (videoRange == 0) return seg.startOriginal;
                double ratio = (double)(duration - seg.startVideo).Ticks / videoRange;
                long originalRange = (seg.endOriginal - seg.startOriginal).Ticks;
                long offset = (long)(ratio * originalRange);
                return seg.startOriginal + TimeSpan.FromTicks(offset);
            }
        }

        // duration が全セグメントを外れる場合（最後のキーフレーム以降）
        var lastKf = (KeyFrame<float>)keyFrameAnimation.KeyFrames.Last();
        double lastSpeed = lastKf.Value / 100.0;
        if (lastSpeed == 0) return currentOriginal;
        // 最後の区間は線形関係： duration = currentVideo + (original - currentOriginal) * lastSpeed
        long offsetTicks = (long)((duration - currentVideo).Ticks / lastSpeed);
        return currentOriginal + TimeSpan.FromTicks(offsetTicks);
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

    public partial class Resource
    {
        public TimeSpan RenderedPosition { get; internal set; }

        public TimeSpan RequestedPosition { get; internal set; }

        partial void PostUpdate(SourceVideo obj, RenderContext context)
        {
            var clock = context.Clock;
            // アニメーションがある場合、前回のキーフレームを引く
            var anm = obj.Speed.Animation;
            if (anm is KeyFrameAnimation<float> keyFrameAnimation)
            {
                RequestedPosition = obj.CalculateVideoTime(
                    keyFrameAnimation.UseGlobalClock
                        ? clock.GlobalClock.CurrentTime
                        : clock.CurrentTime,
                    this);
            }
            else
            {
                RequestedPosition = clock.CurrentTime * (_speed / 100);
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
