using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class SourceVideoSpeedTest
{
    private VideoSource? _videoSource;
    private SourceVideo? _sourceVideo;
    private SourceVideo.Resource? _sourceVideoResource;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        TestMediaHelper.RegisterTestDecoder();
    }

    [SetUp]
    public void SetUp()
    {
        // 30fps、300フレーム（10秒）のテストビデオを作成
        var videoPath = TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300);

        _videoSource = new VideoSource();
        _videoSource.ReadFrom(new Uri(videoPath));

        _sourceVideo = new SourceVideo();
        _sourceVideo.Source.CurrentValue = _videoSource;
        _sourceVideo.TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    [TearDown]
    public void TearDown()
    {
        _sourceVideoResource?.Dispose();
        _sourceVideoResource = null;
        _videoSource = null;
        _sourceVideo = null;
    }

    // 静的な速度では、映像時刻 = 再生時刻 × 速度 / 100
    [TestCase(100f, 1.0, 1.0, TestName = "CalculateVideoTime_WithStaticSpeed100_ShouldReturnSameTime")]
    [TestCase(200f, 1.0, 2.0, TestName = "CalculateVideoTime_WithStaticSpeed200_ShouldReturnDoubleTime")]
    [TestCase(50f, 2.0, 1.0, TestName = "CalculateVideoTime_WithStaticSpeed50_ShouldReturnHalfTime")]
    public void CalculateVideoTime_WithStaticSpeed(float speed, double seconds, double expectedSeconds)
    {
        _sourceVideo!.Speed.CurrentValue = speed;

        SourceVideo.Resource resource = EvaluateAt(TimeSpan.FromSeconds(seconds));

        Assert.That(resource.RequestedPosition.TotalSeconds, Is.EqualTo(expectedSeconds).Within(0.1));
    }

    [Test]
    public void CalculateVideoTime_WithLinearSpeedAnimation_ShouldInterpolateSmoothly()
    {
        // Arrange: 0秒で速度100、2秒で速度200の線形アニメーション
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.FromSeconds(2) });
        _sourceVideo!.Speed.Animation = animation;

        SourceVideo.Resource resource = EvaluateAt(TimeSpan.FromSeconds(1));

        // Assert: ∫₀¹(100 + 50t)dt = 125 % なので 1.25 秒
        Assert.That(resource.RequestedPosition.TotalSeconds, Is.EqualTo(1.25).Within(0.1));
    }

    [Test]
    public void CalculateVideoTime_WithLinearSpeedAnimation_AtMidpoint_ShouldInterpolateCorrectly()
    {
        // Arrange: 0秒で速度100、4秒で速度300の線形アニメーション
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 300f, KeyTime = TimeSpan.FromSeconds(4) });
        _sourceVideo!.Speed.Animation = animation;

        SourceVideo.Resource resource = EvaluateAt(TimeSpan.FromSeconds(2));

        // Assert: 0-2秒の積分
        // 速度関数: s(t) = 100 + 50t (線形)
        // 積分: ∫s(t)dt from 0 to 2 = [100t + 25t²] from 0 to 2 = 200 + 100 = 300% → 3秒
        Assert.That(resource.RequestedPosition.TotalSeconds, Is.EqualTo(3.0).Within(0.2));
    }

    // 一定の速度のアニメーションでも、映像時刻 = 再生時刻 × 速度 / 100
    [TestCase(150f, true, 2000.0, 3000.0, 100.0,
        TestName = "CalculateVideoTime_WithConstantSpeedAnimation_ShouldMatchStaticSpeed")]
    [TestCase(200f, false, 1000.0, 2000.0, 100.0,
        TestName = "CalculateVideoTime_WithSingleKeyframe_ShouldUseKeyframeValue")]
    [TestCase(200f, false, 1.0, 2.0, 0.5,
        TestName = "CalculateVideoTime_WithVerySmallTimeSpan_ShouldHandleCorrectly")]
    [TestCase(5f, false, 2000.0, 100.0, 20.0,
        TestName = "CalculateVideoTime_WithVerySlowSpeed_ShouldHandleCorrectly")]
    public void CalculateVideoTime_WithConstantSpeedAnimation(
        float speed, bool keepUntilTheEnd, double milliseconds, double expectedMilliseconds, double tolerance)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = speed, KeyTime = TimeSpan.Zero });
        if (keepUntilTheEnd)
            animation.KeyFrames.Add(new KeyFrame<float> { Value = speed, KeyTime = TimeSpan.FromSeconds(10) });
        _sourceVideo!.Speed.Animation = animation;

        SourceVideo.Resource resource = EvaluateAt(TimeSpan.FromMilliseconds(milliseconds));

        Assert.That(resource.RequestedPosition.TotalMilliseconds, Is.EqualTo(expectedMilliseconds).Within(tolerance));
    }

    [Test]
    public void CalculateVideoTime_CacheShouldBeInvalidated_WhenAnimationChanges()
    {
        // Arrange: 最初のアニメーション（速度200）
        var animation1 = new KeyFrameAnimation<float>();
        animation1.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.Zero });
        _sourceVideo!.Speed.Animation = animation1;

        var firstPosition = EvaluateAt(TimeSpan.FromSeconds(1)).RequestedPosition;

        // Act: アニメーションを変更（速度100）
        var animation2 = new KeyFrameAnimation<float>();
        animation2.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.Zero });
        _sourceVideo.Speed.Animation = animation2;

        // 同じ時点で再度更新
        var secondPosition = ReconcileAt(TimeSpan.FromSeconds(1)).RequestedPosition;

        // Assert: アニメーション変更後は異なる値になるべき
        Assert.That(firstPosition.TotalSeconds, Is.EqualTo(2.0).Within(0.1)); // 速度200
        Assert.That(secondPosition.TotalSeconds, Is.EqualTo(1.0).Within(0.1)); // 速度100
    }

    [Test]
    public void CalculateVideoTime_SequentialUpdates_FollowTheIntegratedSpeed()
    {
        // Arrange: 線形アニメーション
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.FromSeconds(10) });
        _sourceVideo!.Speed.Animation = animation;

        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo.ToResource(CompositionContext.Default);

        // 連続した時間での更新: ∫₀ᵗ(1 + τ/10)dτ = t + t²/20
        for (int t = 0; t <= 5; t++)
        {
            Assert.That(
                ReconcileAt(TimeSpan.FromSeconds(t)).RequestedPosition.TotalSeconds,
                Is.EqualTo(t + t * t / 20.0).Within(1.0 / 60),
                $"Position at {t}s");
        }
    }

    [Test]
    public void CalculateOriginalTime_WithStaticSpeed_ShouldCalculateCorrectly()
    {
        _sourceVideo!.Speed.CurrentValue = 200f;
        SourceVideo.Resource resource = EvaluateAt(TimeSpan.Zero);

        var originalTime = _sourceVideo.CalculateOriginalTime(resource);

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

        SourceVideo.Resource resource = EvaluateAt(TimeSpan.Zero);

        var originalTime = _sourceVideo.CalculateOriginalTime(resource);

        // Assert: T + T²/20 = 10 を解いた T = √300 - 10 ≈ 7.32 秒（二分探索は 1/60 秒の精度）
        Assert.That(originalTime, Is.Not.Null);
        Assert.That(originalTime!.Value.TotalSeconds, Is.EqualTo(Math.Sqrt(300) - 10).Within(1.0 / 60));
    }

    [Test]
    public void CalculateVideoTime_OffsetElement_UseGlobalClockTrue_ShouldStartFromElementZero()
    {
        // Arrange: 要素を 5s 開始、UseGlobalClock=true で speed=100% 一定。
        // 期待: 要素ローカル 1s 時点でビデオ時刻も 1s になること (要素開始前の積分は引かれる)。
        _sourceVideo!.TimeRange = new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
        var animation = new KeyFrameAnimation<float>
        {
            UseGlobalClock = true
        };
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.FromSeconds(15) });
        _sourceVideo.Speed.Animation = animation;

        // グローバル 6s = 要素ローカル 1s
        SourceVideo.Resource resource = EvaluateAt(TimeSpan.FromSeconds(6));

        Assert.That(resource.RequestedPosition.TotalSeconds, Is.EqualTo(1.0).Within(0.1));
    }

    [Test]
    public void CalculateVideoTime_OffsetElement_UseGlobalClockTrue_WithVariableSpeed_ShouldAccumulateFromElementStart()
    {
        // Arrange: 要素を 5s 開始、UseGlobalClock=true。
        // 速度は global 0s..15s で 100% → 200% に線形補間。
        // s(t) = 100 + (100/15)*t [%] を要素開始 (5s) ～ 要素ローカル 1s (=global 6s) で積分。
        _sourceVideo!.TimeRange = new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
        var animation = new KeyFrameAnimation<float>
        {
            UseGlobalClock = true
        };
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.FromSeconds(15) });
        _sourceVideo.Speed.Animation = animation;

        SourceVideo.Resource resource = EvaluateAt(TimeSpan.FromSeconds(6));

        // 解析解: ∫_{5}^{6} (1 + t/15) dt = [t + t²/30]_{5}^{6}
        // = (6 + 36/30) - (5 + 25/30) = 1 + 11/30 ≒ 1.3667 s
        Assert.That(resource.RequestedPosition.TotalSeconds, Is.EqualTo(1.3667).Within(0.05));
    }

    [Test]
    public void CalculateOriginalTime_WithVerySlowSpeed_ShouldExpandUpperBound()
    {
        // Arrange: 非常に遅い速度（5%）でのテスト - 上限拡大が必要
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 5f, KeyTime = TimeSpan.Zero });
        _sourceVideo!.Speed.Animation = animation;

        SourceVideo.Resource resource = EvaluateAt(TimeSpan.Zero);

        var originalTime = _sourceVideo.CalculateOriginalTime(resource);

        // Assert: 10秒の動画を5%速度で再生すると、元の時間は200秒
        Assert.That(originalTime, Is.Not.Null);
        Assert.That(originalTime!.Value.TotalSeconds, Is.EqualTo(200.0).Within(5.0));
    }

    // リソースを作り、time の時点で更新する
    private SourceVideo.Resource EvaluateAt(TimeSpan time)
    {
        _sourceVideoResource = (SourceVideo.Resource)_sourceVideo!.ToResource(CompositionContext.Default);
        return ReconcileAt(time);
    }

    private SourceVideo.Resource ReconcileAt(TimeSpan time)
    {
        var versionBumped = false;
        _sourceVideoResource!.Reconcile(_sourceVideo!, new CompositionContext(time), ref versionBumped);
        return _sourceVideoResource;
    }
}
