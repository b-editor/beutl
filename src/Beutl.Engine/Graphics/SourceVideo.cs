using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics;

[Display(Name = nameof(Strings.Video), ResourceType = typeof(Strings))]
public class SourceVideo : Drawable
{
    public static readonly CoreProperty<TimeSpan> OffsetPositionProperty;
    public static readonly CoreProperty<float> SpeedProperty;
    public static readonly CoreProperty<IVideoSource?> SourceProperty;
    public static readonly CoreProperty<bool> IsLoopProperty;
    private TimeSpan _offsetPosition;
    private float _speed = 100;
    private IVideoSource? _source;
    private bool _isLoop;
    private TimeSpan _requestedPosition;
    private TimeSpan? _renderedPosition;

    static SourceVideo()
    {
        OffsetPositionProperty = ConfigureProperty<TimeSpan, SourceVideo>(nameof(OffsetPosition))
            .Accessor(o => o.OffsetPosition, (o, v) => o.OffsetPosition = v)
            .DefaultValue(TimeSpan.Zero)
            .Register();

        SpeedProperty = ConfigureProperty<float, SourceVideo>(nameof(Speed))
            .Accessor(o => o.Speed, (o, v) => o.Speed = v)
            .DefaultValue(100)
            .Register();

        SourceProperty = ConfigureProperty<IVideoSource?, SourceVideo>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .Register();

        IsLoopProperty = ConfigureProperty<bool, SourceVideo>(nameof(IsLoop))
            .Accessor(o => o.IsLoop, (o, v) => o.IsLoop = v)
            .DefaultValue(false)
            .Register();

        AffectsRender<SourceVideo>(
            OffsetPositionProperty,
            SpeedProperty,
            SourceProperty,
            IsLoopProperty);
    }

    [Display(Name = nameof(Strings.Offset), ResourceType = typeof(Strings))]
    public TimeSpan OffsetPosition
    {
        get => _offsetPosition;
        set => SetAndRaise(OffsetPositionProperty, ref _offsetPosition, value);
    }

    [Display(Name = nameof(Strings.Speed), ResourceType = typeof(Strings))]
    public float Speed
    {
        get => _speed;
        set => SetAndRaise(SpeedProperty, ref _speed, value);
    }

    [Display(Name = nameof(Strings.Source), ResourceType = typeof(Strings))]
    public IVideoSource? Source
    {
        get => _source;
        set => SetAndRaise(SourceProperty, ref _source, value);
    }

    [Display(Name = nameof(Strings.IsLoop), ResourceType = typeof(Strings))]
    public bool IsLoop
    {
        get => _isLoop;
        set => SetAndRaise(IsLoopProperty, ref _isLoop, value);
    }

    private TimeSpan CalculateVideoTime(TimeSpan timeSpan)
    {
        // 最初のキーフレームが100, 00:00
        // 2のキーフレームが100, 00:10
        // 3のキーフレームが200, 00:10.033
        // 3の時点では動画の時間は00:10.033が自然
        // でも実際には00:10.033 * 2倍 = 00:20.066になってしまう

        // 手前（KeyTime <= timeSpan）のキーフレームを取得しその時点での自然な動画時間を取得、(CurrentTime - KeyTime) * Speedを加算したものをRequestedPositionに設定

        var anm = Animations.FirstOrDefault(i => i.Property.Id == SpeedProperty.Id);
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation)
            return timeSpan;

        // キーフレームが無い場合は、グローバルな _speed を使って単純変換する
        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return TimeSpan.FromTicks((long)(timeSpan.Ticks * (_speed / 100.0)));
        }

        int kfi = keyFrameAnimation.KeyFrames.IndexAt(timeSpan);
        var kf = (KeyFrame<float>)keyFrameAnimation.KeyFrames[kfi];

        // 前のキーフレームが存在する場合は、その区間分を再帰的に累積計算する
        if (kfi > 0 &&
            keyFrameAnimation.KeyFrames[kfi - 1] is KeyFrame<float> prevKf)
        {
            var baseVideoTime = CalculateVideoTime(prevKf.KeyTime);
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
        if (Source?.IsDisposed != false) return null;

        var duration = Source.Duration;

        var anm = Animations.FirstOrDefault(i => i.Property.Id == SpeedProperty.Id);

        // スピードのアニメーションまたはキーフレームが 1 つもない場合は、単純に逆変換する
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation || keyFrameAnimation.KeyFrames.Count == 0)
        {
            return TimeSpan.FromTicks((long)(duration.Ticks / (_speed / 100.0)));
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

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        // アニメーションがある場合、前回のキーフレームを引く
        var anm = Animations.FirstOrDefault(i => i.Property.Id == SpeedProperty.Id);
        if (anm is KeyFrameAnimation<float> keyFrameAnimation)
        {
            _requestedPosition = CalculateVideoTime(keyFrameAnimation.UseGlobalClock
                ? clock.GlobalClock.CurrentTime
                : clock.CurrentTime);
        }
        else
        {
            _requestedPosition = clock.CurrentTime * (_speed / 100);
        }

        // ループ処理を追加
        if (IsLoop && Source?.IsDisposed == false && Source.Duration > TimeSpan.Zero)
        {
            // 正の値の場合、動画の長さでモジュロ計算
            if (_requestedPosition >= TimeSpan.Zero)
            {
                _requestedPosition = TimeSpan.FromTicks(_requestedPosition.Ticks % Source.Duration.Ticks);
            }
            // 負の値の場合、動画の長さを足してからモジュロ計算
            else
            {
                var positiveTicks = Source.Duration.Ticks + (_requestedPosition.Ticks % Source.Duration.Ticks);
                _requestedPosition = TimeSpan.FromTicks(positiveTicks % Source.Duration.Ticks);
            }
        }
        else if (_requestedPosition < TimeSpan.Zero)
        {
            _requestedPosition = (Source?.Duration ?? TimeSpan.Zero) + _requestedPosition;
        }

        if (_requestedPosition != _renderedPosition)
        {
            Invalidate();
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        if (_source?.IsDisposed == false)
        {
            return _source.FrameSize.ToSize(1);
        }
        else
        {
            return Size.Empty;
        }
    }

    protected override void OnDraw(GraphicsContext2D context)
    {
        if (_source?.IsDisposed == false)
        {
            TimeSpan pos = _requestedPosition + _offsetPosition;
            Rational rate = _source.FrameRate;
            double frameNum = pos.Ticks * rate.Numerator / (double)(TimeSpan.TicksPerSecond * rate.Denominator);

            context.DrawVideoSource(
                _source,
                (int)Math.Round(frameNum, MidpointRounding.AwayFromZero),
                Brushes.White,
                null);
            _renderedPosition = _requestedPosition;
        }
    }
}
