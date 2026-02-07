using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class SourceVideoSpeedTest
{
    private VideoSource? _videoSource;
    private VideoSource.Resource? _videoSourceResource;
    private SourceVideo? _sourceVideo;
    private SourceVideo.Resource? _sourceVideoResource;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // テストデコーダを登録
        TestMediaHelper.RegisterTestDecoder();
    }

    [SetUp]
    public void SetUp()
    {
        // 30fps、300フレーム（10秒）のテストビデオを作成
        var videoPath = TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300);

        _videoSource = new VideoSource();
        _videoSource.ReadFrom(new Uri(videoPath));
        _videoSourceResource = _videoSource.ToResource(RenderContext.Default);

        _sourceVideo = new SourceVideo();
        _sourceVideo.Source.CurrentValue = _videoSource;
        _sourceVideo.TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    [TearDown]
    public void TearDown()
    {
        _sourceVideoResource?.Dispose();
        _sourceVideoResource = null;
        _videoSourceResource?.Dispose();
        _videoSourceResource = null;
        _videoSource = null;
        _sourceVideo = null;
    }

    [Test]
    public void CalculateVideoTime_WithStaticSpeed100_ShouldReturnSameTime()
    {
        // Arrange
        _sourceVideo!.Speed.CurrentValue = 100f;
        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 1秒時点でリソースを更新
        var context = new RenderContext(TimeSpan.FromSeconds(1));
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, context, ref updateOnly);

        // Assert: Speed=100なので、1秒の再生時刻 = 1秒の映像時刻
        Assert.That(_sourceVideoResource.RequestedPosition.TotalSeconds, Is.EqualTo(1.0).Within(0.1));
    }

    [Test]
    public void CalculateVideoTime_WithStaticSpeed200_ShouldReturnDoubleTime()
    {
        // Arrange
        _sourceVideo!.Speed.CurrentValue = 200f;
        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 1秒時点でリソースを更新
        var context = new RenderContext(TimeSpan.FromSeconds(1));
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, context, ref updateOnly);

        // Assert: Speed=200なので、1秒の再生時刻 = 2秒の映像時刻
        Assert.That(_sourceVideoResource.RequestedPosition.TotalSeconds, Is.EqualTo(2.0).Within(0.1));
    }

    [Test]
    public void CalculateVideoTime_WithStaticSpeed50_ShouldReturnHalfTime()
    {
        // Arrange
        _sourceVideo!.Speed.CurrentValue = 50f;
        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 2秒時点でリソースを更新
        var context = new RenderContext(TimeSpan.FromSeconds(2));
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, context, ref updateOnly);

        // Assert: Speed=50なので、2秒の再生時刻 = 1秒の映像時刻
        Assert.That(_sourceVideoResource.RequestedPosition.TotalSeconds, Is.EqualTo(1.0).Within(0.1));
    }

    [Test]
    public void CalculateVideoTime_WithLinearSpeedAnimation_ShouldInterpolateSmoothly()
    {
        // Arrange: 0秒で速度100、2秒で速度200の線形アニメーション
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.FromSeconds(2) });
        _sourceVideo!.Speed.Animation = animation;

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 1秒時点でリソースを更新
        var context = new RenderContext(TimeSpan.FromSeconds(1));
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, context, ref updateOnly);

        // Assert: 0-1秒の平均速度は150%なので、映像時刻は約1.5秒
        // 積分計算: ∫(100 + 50t)dt from 0 to 1 = [100t + 25t²] = 100 + 25 = 125% → 1.25秒
        // ただし線形補間なので実際は: (100 + 150) / 2 * 1 = 125% → 1.25秒
        Assert.That(_sourceVideoResource.RequestedPosition.TotalSeconds, Is.EqualTo(1.25).Within(0.1));
    }

    [Test]
    public void CalculateVideoTime_WithLinearSpeedAnimation_AtMidpoint_ShouldInterpolateCorrectly()
    {
        // Arrange: 0秒で速度100、4秒で速度300の線形アニメーション
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 300f, KeyTime = TimeSpan.FromSeconds(4) });
        _sourceVideo!.Speed.Animation = animation;

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 2秒時点でリソースを更新
        var context = new RenderContext(TimeSpan.FromSeconds(2));
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, context, ref updateOnly);

        // Assert: 0-2秒の積分
        // 速度関数: s(t) = 100 + 50t (線形)
        // 積分: ∫s(t)dt from 0 to 2 = [100t + 25t²] from 0 to 2 = 200 + 100 = 300% → 3秒
        Assert.That(_sourceVideoResource.RequestedPosition.TotalSeconds, Is.EqualTo(3.0).Within(0.2));
    }

    [Test]
    public void CalculateVideoTime_WithConstantSpeedAnimation_ShouldMatchStaticSpeed()
    {
        // Arrange: 一定速度150のアニメーション
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 150f, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 150f, KeyTime = TimeSpan.FromSeconds(10) });
        _sourceVideo!.Speed.Animation = animation;

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 2秒時点でリソースを更新
        var context = new RenderContext(TimeSpan.FromSeconds(2));
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, context, ref updateOnly);

        // Assert: Speed=150なので、2秒の再生時刻 = 3秒の映像時刻
        Assert.That(_sourceVideoResource.RequestedPosition.TotalSeconds, Is.EqualTo(3.0).Within(0.1));
    }

    [Test]
    public void CalculateVideoTime_CacheShouldBeInvalidated_WhenAnimationChanges()
    {
        // Arrange: 最初のアニメーション（速度200）
        var animation1 = new KeyFrameAnimation<float>();
        animation1.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.Zero });
        _sourceVideo!.Speed.Animation = animation1;

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 最初の更新
        var context1 = new RenderContext(TimeSpan.FromSeconds(1));
        var updateOnly1 = false;
        _sourceVideoResource.Update(_sourceVideo, context1, ref updateOnly1);

        var firstPosition = _sourceVideoResource.RequestedPosition;

        // Act: アニメーションを変更（速度100）
        var animation2 = new KeyFrameAnimation<float>();
        animation2.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.Zero });
        _sourceVideo.Speed.Animation = animation2;

        // 同じ時点で再度更新
        var updateOnly2 = false;
        _sourceVideoResource.Update(_sourceVideo, context1, ref updateOnly2);

        var secondPosition = _sourceVideoResource.RequestedPosition;

        // Assert: アニメーション変更後は異なる値になるべき
        Assert.That(firstPosition.TotalSeconds, Is.EqualTo(2.0).Within(0.1)); // 速度200
        Assert.That(secondPosition.TotalSeconds, Is.EqualTo(1.0).Within(0.1)); // 速度100
    }

    [Test]
    public void CalculateVideoTime_WithNoKeyframes_ShouldUseResourceSpeed()
    {
        // Arrange: キーフレームなしのアニメーションで、Speed.CurrentValueを設定
        // キーフレームが0個の場合、CalculateVideoTimeはresource.Speed（=_speed）を使用する
        // _speedはソースジェネレーターで生成され、Speed.GetValue(context)から取得される
        _sourceVideo!.Speed.CurrentValue = 150f;
        // アニメーションを設定しない（PostUpdateはelseブランチを通る）

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 2秒時点でリソースを更新
        var context = new RenderContext(TimeSpan.FromSeconds(2));
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, context, ref updateOnly);

        // Assert: Speed=150なので、2秒の再生時刻 = 3秒の映像時刻
        Assert.That(_sourceVideoResource.RequestedPosition.TotalSeconds, Is.EqualTo(3.0).Within(0.1));
    }

    [Test]
    public void CalculateVideoTime_WithSingleKeyframe_ShouldUseKeyframeValue()
    {
        // Arrange: 単一キーフレーム（速度200）
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.Zero });
        _sourceVideo!.Speed.Animation = animation;

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 1秒時点でリソースを更新
        var context = new RenderContext(TimeSpan.FromSeconds(1));
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, context, ref updateOnly);

        // Assert: Speed=200なので、1秒の再生時刻 = 2秒の映像時刻
        Assert.That(_sourceVideoResource.RequestedPosition.TotalSeconds, Is.EqualTo(2.0).Within(0.1));
    }

    [Test]
    public void CalculateVideoTime_SequentialUpdates_ShouldUseCacheEfficiently()
    {
        // Arrange: 線形アニメーション
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.FromSeconds(10) });
        _sourceVideo!.Speed.Animation = animation;

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 連続した時間での更新
        var positions = new List<double>();
        for (int i = 0; i <= 5; i++)
        {
            var context = new RenderContext(TimeSpan.FromSeconds(i));
            var updateOnly = false;
            _sourceVideoResource.Update(_sourceVideo, context, ref updateOnly);
            positions.Add(_sourceVideoResource.RequestedPosition.TotalSeconds);
        }

        // Assert: 位置が単調増加していることを確認
        for (int i = 1; i < positions.Count; i++)
        {
            Assert.That(positions[i], Is.GreaterThan(positions[i - 1]),
                $"Position at {i}s should be greater than position at {i - 1}s");
        }
    }

    [Test]
    public void CalculateOriginalTime_WithStaticSpeed_ShouldCalculateCorrectly()
    {
        // Arrange
        _sourceVideo!.Speed.CurrentValue = 200f;
        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 初期化のための更新
        var initContext = new RenderContext(TimeSpan.Zero);
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, initContext, ref updateOnly);

        // Act
        var originalTime = _sourceVideo.CalculateOriginalTime(_sourceVideoResource);

        // Assert: 10秒の動画を2倍速で再生すると、元の時間は5秒
        Assert.That(originalTime, Is.Not.Null);
        Assert.That(originalTime!.Value.TotalSeconds, Is.EqualTo(5.0).Within(0.5));
    }

    [Test]
    public void CalculateOriginalTime_WithAnimatedSpeed_ShouldCalculateUsingBinarySearch()
    {
        // Arrange: 線形アニメーション
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.FromSeconds(10) });
        _sourceVideo!.Speed.Animation = animation;

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 初期化のための更新
        var initContext = new RenderContext(TimeSpan.Zero);
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, initContext, ref updateOnly);

        // Act
        var originalTime = _sourceVideo.CalculateOriginalTime(_sourceVideoResource);

        // Assert: 二分探索で適切な値が返されることを確認（nullでないこと）
        Assert.That(originalTime, Is.Not.Null);
        Assert.That(originalTime!.Value.TotalSeconds, Is.GreaterThan(0));
    }

    [Test]
    public void CalculateVideoTime_WithVerySmallTimeSpan_ShouldHandleCorrectly()
    {
        // Arrange: 非常に小さいtimeSpan（1/60秒未満）でのテスト
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.Zero });
        _sourceVideo!.Speed.Animation = animation;

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 1ミリ秒時点でリソースを更新（1/60秒 ≈ 16.67ミリ秒より小さい）
        var context = new RenderContext(TimeSpan.FromMilliseconds(1));
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, context, ref updateOnly);

        // Assert: 非常に小さい値でも正しく計算される
        // Speed=200なので、1ミリ秒の再生時刻 = 2ミリ秒の映像時刻
        Assert.That(_sourceVideoResource.RequestedPosition.TotalMilliseconds, Is.EqualTo(2.0).Within(0.5));
    }

    [Test]
    public void CalculateVideoTime_WithVerySlowSpeed_ShouldHandleCorrectly()
    {
        // Arrange: 非常に遅い速度（5%）でのテスト
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 5f, KeyTime = TimeSpan.Zero });
        _sourceVideo!.Speed.Animation = animation;

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 2秒時点でリソースを更新
        var context = new RenderContext(TimeSpan.FromSeconds(2));
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, context, ref updateOnly);

        // Assert: Speed=5なので、2秒の再生時刻 = 0.1秒の映像時刻
        Assert.That(_sourceVideoResource.RequestedPosition.TotalSeconds, Is.EqualTo(0.1).Within(0.02));
    }

    [Test]
    public void CalculateOriginalTime_WithVerySlowSpeed_ShouldExpandUpperBound()
    {
        // Arrange: 非常に遅い速度（5%）でのテスト - 上限拡大が必要
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 5f, KeyTime = TimeSpan.Zero });
        _sourceVideo!.Speed.Animation = animation;

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(RenderContext.Default);

        // 初期化のための更新
        var initContext = new RenderContext(TimeSpan.Zero);
        var updateOnly = false;
        _sourceVideoResource.Update(_sourceVideo, initContext, ref updateOnly);

        // Act
        var originalTime = _sourceVideo.CalculateOriginalTime(_sourceVideoResource);

        // Assert: 10秒の動画を5%速度で再生すると、元の時間は200秒
        Assert.That(originalTime, Is.Not.Null);
        Assert.That(originalTime!.Value.TotalSeconds, Is.EqualTo(200.0).Within(5.0));
    }
}
