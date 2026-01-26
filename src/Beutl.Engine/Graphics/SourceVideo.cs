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

    // 積分単位: 60サンプル/秒（フレームレート相当）
    private const int IntegrationRate = 60;

    private TimeSpan CalculateVideoTime(TimeSpan timeSpan, Resource resource)
    {
        var anm = Speed.Animation;
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation)
            return timeSpan;

        // キーフレームが無い場合は、グローバルな _speed を使って単純変換する
        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return TimeSpan.FromTicks((long)(timeSpan.Ticks * (resource.Speed / 100.0)));
        }

        // キャッシュ初期化・イベント購読
        resource.EnsureSpeedCache(anm);

        // 開始秒数とキャッシュからの累積値取得
        int targetSec = (int)timeSpan.TotalSeconds;
        (int cachedSec, double cachedSum) = resource.TryGetSpeedCache(targetSec);

        // キャッシュから目標秒数まで積分を継続
        double sum = cachedSum;
        int startSec = cachedSec < 0 ? 0 : cachedSec;

        // startSecからtargetSecまで、1秒単位で速度を積分
        for (int sec = startSec; sec < targetSec; sec++)
        {
            // 1秒間をIntegrationRateサンプルに分割して積分
            for (int i = 0; i < IntegrationRate; i++)
            {
                double t = sec + (i / (double)IntegrationRate);
                float speed = keyFrameAnimation.Interpolate(TimeSpan.FromSeconds(t));
                sum += (speed / 100.0) / IntegrationRate;
            }
            resource._speedIntegralCache![sec + 1] = sum;
        }

        // 目標秒数から目標時刻までの残り積分
        int targetInSamples = (int)(timeSpan.TotalSeconds * IntegrationRate);
        int secStartInSamples = targetSec * IntegrationRate;

        for (int i = secStartInSamples; i < targetInSamples; i++)
        {
            double t = i / (double)IntegrationRate;
            float speed = keyFrameAnimation.Interpolate(TimeSpan.FromSeconds(t));
            sum += (speed / 100.0) / IntegrationRate;
        }

        // 最後のサンプルから正確な時刻までの補間（端数処理）
        double fractionalSamples = (timeSpan.TotalSeconds * IntegrationRate) - targetInSamples;
        if (fractionalSamples > 0)
        {
            float speed = keyFrameAnimation.Interpolate(timeSpan);
            sum += (speed / 100.0) * fractionalSamples / IntegrationRate;
        }

        return TimeSpan.FromSeconds(sum);
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
        // 積分キャッシュ: 秒数 -> 累積ビデオ時間（秒単位）
        internal Dictionary<int, double>? _speedIntegralCache;
        private IAnimation<float>? _cachedAnimation;

        public TimeSpan RenderedPosition { get; internal set; }

        public TimeSpan RequestedPosition { get; internal set; }

        internal void InvalidateSpeedCache()
        {
            _speedIntegralCache?.Clear();
        }

        private void OnAnimationEdited(object? sender, EventArgs e)
        {
            InvalidateSpeedCache();
        }

        internal void EnsureSpeedCache(IAnimation<float>? animation)
        {
            _speedIntegralCache ??= new Dictionary<int, double>();

            // アニメーションが変わった場合はキャッシュをクリアしイベントを再登録
            if (!ReferenceEquals(_cachedAnimation, animation))
            {
                _speedIntegralCache.Clear();

                if (_cachedAnimation != null)
                    _cachedAnimation.Edited -= OnAnimationEdited;

                if (animation != null)
                    animation.Edited += OnAnimationEdited;

                _cachedAnimation = animation;
            }
        }

        internal (int Key, double Value) TryGetSpeedCache(int sec)
        {
            if (_speedIntegralCache == null) return (-1, 0);

            // キャッシュヒット確認（指定秒数以下で最大のキャッシュを探す）
            do
            {
                if (_speedIntegralCache.TryGetValue(sec--, out double result))
                {
                    return (sec + 1, result);
                }
            } while (sec >= 0);

            return (-1, 0);
        }

        partial void PostDispose(bool disposing)
        {
            // イベント購読解除（メモリリーク防止）
            if (_cachedAnimation != null)
            {
                _cachedAnimation.Edited -= OnAnimationEdited;
                _cachedAnimation = null;
            }
            _speedIntegralCache = null;
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
