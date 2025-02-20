using Beutl.Animation;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics;

public class DrawableVideo : Drawable
{
    public static readonly CoreProperty<TimeSpan> OffsetPositionProperty;
    public static readonly CoreProperty<float> SpeedProperty;
    public static readonly CoreProperty<IVideoSource?> SourceProperty;
    private TimeSpan _offsetPosition;
    private float _speed;
    private IVideoSource? _source;
    private TimeSpan _requestedPosition;
    private TimeSpan? _renderedPosition;

    static DrawableVideo()
    {
        OffsetPositionProperty = ConfigureProperty<TimeSpan, DrawableVideo>(nameof(OffsetPosition))
            .Accessor(o => o.OffsetPosition, (o, v) => o.OffsetPosition = v)
            .DefaultValue(TimeSpan.Zero)
            .Register();

        SpeedProperty = ConfigureProperty<float, DrawableVideo>(nameof(Speed))
            .Accessor(o => o.Speed, (o, v) => o.Speed = v)
            .DefaultValue(100)
            .Register();

        SourceProperty = ConfigureProperty<IVideoSource?, DrawableVideo>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .Register();

        AffectsRender<DrawableVideo>(
            OffsetPositionProperty,
            SpeedProperty,
            SourceProperty);
    }

    public TimeSpan OffsetPosition
    {
        get => _offsetPosition;
        set => SetAndRaise(OffsetPositionProperty, ref _offsetPosition, value);
    }

    public float Speed
    {
        get => _speed;
        set => SetAndRaise(SpeedProperty, ref _speed, value);
    }

    public IVideoSource? Source
    {
        get => _source;
        set => SetAndRaise(SourceProperty, ref _source, value);
    }

    private TimeSpan CalculateVideoTime(TimeSpan timeSpan)
    {
        var anm = Animations.FirstOrDefault(i => i.Property.Id == SpeedProperty.Id);
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation) return timeSpan;

        // 最初のキーフレームが100, 00:00
        // 2のキーフレームが100, 00:10
        // 3のキーフレームが200, 00:10.033
        // 3の時点では動画の時間は00:10.033が自然
        // でも実際には00:10.033 * 2倍 = 00:20.066になってしまう

        // 手前（KeyTime <= timeSpan）のキーフレームを取得しその時点での自然な動画時間を取得、(CurrentTime - KeyTime) * Speedを加算したものをRequestedPositionに設定

        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return timeSpan * (_speed / 100);
        }

        int kfi = keyFrameAnimation.KeyFrames.IndexAt(timeSpan);
        var kf = (KeyFrame<float>)keyFrameAnimation.KeyFrames[kfi];
        if (kfi > 0 &&
            keyFrameAnimation.KeyFrames[kfi - 1] is KeyFrame<float> prevKf)
        {
            var videoTime = CalculateVideoTime(prevKf.KeyTime);
            return videoTime + (timeSpan - prevKf.KeyTime) * (kf.Value / 100);
        }

        if (kfi >= 0)
        {
            return timeSpan * (kf.Value / 100);
        }

        return timeSpan;
    }

    public TimeSpan? CalculateOriginalTime()
    {
        if (Source?.IsDisposed != false) return null;

        var duration = Source.Duration;

        // Speed プロパティに対応するアニメーションを取得
        var anm = Animations.FirstOrDefault(i => i.Property.Id == SpeedProperty.Id);
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation)
            return duration / (_speed / 100);

        // キーフレームがない場合は単純な逆変換
        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return duration / (_speed / 100);
        }

        // 各キーフレームまでの動画時間（CalculateVideoTime の出力）を順次計算する
        var videoTimeList = new List<TimeSpan>();
        TimeSpan prevOriginal = TimeSpan.Zero; // 各区間の開始時間（実時間）
        TimeSpan prevVideo = TimeSpan.Zero; // 各区間の開始時点の動画時間

        foreach (var frame in keyFrameAnimation.KeyFrames)
        {
            var kf = (KeyFrame<float>)frame;
            // この区間（前回キーフレームから現在のキーフレームまで）の実時間
            TimeSpan segmentOriginal = kf.KeyTime - prevOriginal;
            // この区間における動画時間の進み具合は、実時間に対してスピード係数 (kf.Value / 100) を乗算
            TimeSpan segmentVideo = segmentOriginal * (kf.Value / 100);
            // 現在のキーフレームにおける動画時間は前回までの累積に加算
            TimeSpan currentVideo = prevVideo + segmentVideo;
            videoTimeList.Add(currentVideo);

            // 次の区間の開始時間を更新
            prevOriginal = kf.KeyTime;
            prevVideo = currentVideo;
        }

        // 与えられた duration が最初のキーフレームより前の場合
        if (duration < videoTimeList[0])
        {
            // 初回区間は、CalculateVideoTime 内では timeSpan * (firstKF.Value/100) としているため
            var speedFactor = ((KeyFrame<float>)keyFrameAnimation.KeyFrames[0]).Value / 100;
            return duration / speedFactor;
        }

        // duration がどの区間に属するかを順次チェック
        for (int i = 1; i < videoTimeList.Count; i++)
        {
            if (duration < videoTimeList[i])
            {
                // duration は前区間の終わり（＝前キーフレーム時点）とこのキーフレームの区間内にある
                var prevKeyTime = ((KeyFrame<float>)keyFrameAnimation.KeyFrames[i - 1]).KeyTime;
                var speedFactor = ((KeyFrame<float>)keyFrameAnimation.KeyFrames[i]).Value / 100;
                // 前区間終了時の動画時間
                TimeSpan videoStart = videoTimeList[i - 1];
                // この区間内での追加の動画時間から元の実時間への変換（逆算）
                TimeSpan offset = (duration - videoStart) / speedFactor;
                return prevKeyTime + offset;
            }
        }

        // duration がすべてのキーフレーム区間を超えている場合は、最後のキーフレーム以降の区間とする
        var lastKeyTime = ((KeyFrame<float>)keyFrameAnimation.KeyFrames.Last()).KeyTime;
        var lastSpeedFactor = ((KeyFrame<float>)keyFrameAnimation.KeyFrames.Last()).Value / 100;
        TimeSpan offsetAfterLast = (duration - videoTimeList.Last()) / lastSpeedFactor;
        return lastKeyTime + offsetAfterLast;
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
            double frameNum = pos.TotalSeconds * (rate.Numerator / (double)rate.Denominator);

            context.DrawVideoSource(_source, (int)frameNum, Brushes.White, null);
            _renderedPosition = _requestedPosition;
        }
    }
}
