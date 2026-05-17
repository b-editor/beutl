using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

public class AudioProcessContextTests
{
    [Test]
    public void GetSampleCount_Static_IntegerSeconds_NoCeiling()
    {
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.That(AudioProcessContext.GetSampleCount(range, 44100), Is.EqualTo(44100));
    }

    [Test]
    public void GetSampleCount_Static_ZeroDuration_ReturnsZero()
    {
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.Zero);
        Assert.That(AudioProcessContext.GetSampleCount(range, 44100), Is.EqualTo(0));
    }

    [Test]
    public void GetSampleCount_Static_FractionalDuration_CeilingsUp()
    {
        // 44100Hz では 1 サンプル ≒ 226.7575 ticks。TimeSpan.TicksPerSecond / 44100 は整数除算で 226 となり、
        // +1 した 227 ticks は 1 サンプル相当 (226.7575) をわずかに (0.2425 ticks) 超える。
        // この duration は truncation だと 1 サンプル、Ceiling だと 2 サンプルになり、両者の差を確実に踏ませる。
        var oneSampleTicksFloor = TimeSpan.TicksPerSecond / 44100; // 226 (true boundary is ~226.7575)
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromTicks(oneSampleTicksFloor + 1));

        var truncated = (int)(range.Duration.TotalSeconds * 44100);
        var ceiled = AudioProcessContext.GetSampleCount(range, 44100);

        Assert.That(truncated, Is.EqualTo(1), "前提: 旧 truncation ロジックでは 1 サンプル");
        Assert.That(ceiled, Is.EqualTo(2), "修正後: Ceiling で 2 サンプルになるべき");
    }

    [Test]
    public void GetSampleCount_Static_MatchesInstanceMethod()
    {
        var range = new TimeRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromMilliseconds(123.456));
        const int sampleRate = 48000;

        var instance = new AudioProcessContext(range, sampleRate, new AnimationSampler(), null);

        Assert.That(AudioProcessContext.GetSampleCount(range, sampleRate),
            Is.EqualTo(instance.GetSampleCount()));
    }

    [Test]
    public void GetSampleCount_Static_DifferentSampleRates()
    {
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2));

        Assert.That(AudioProcessContext.GetSampleCount(range, 44100), Is.EqualTo(88200));
        Assert.That(AudioProcessContext.GetSampleCount(range, 48000), Is.EqualTo(96000));
    }

    [Test]
    public void GetSampleCount_Static_OneTick_ReturnsOne()
    {
        // 1 tick (100 ns) は 0 ではない最小の duration。Composer の silence fallback が
        // 「サンプルが必要なら最低 1 サンプル確保する」という不変条件に依存しているため、
        // 微小な正 duration がゼロにならないことを直接ピン留めする。
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromTicks(1));

        Assert.That(AudioProcessContext.GetSampleCount(range, 44100), Is.EqualTo(1));
    }

    [Test]
    public void GetSampleCount_Static_JustBelowSampleBoundary_DoesNotOverCount()
    {
        // 整数除算で 226 ticks 取ると真の境界 (~226.7575 ticks) を 0.7575 下回るため、
        // Ceiling でも 1 サンプル止まり。境界の "下側" を踏むことで Ceiling が
        // 過剰サンプル化していないことを補強する (FractionalDuration テストの裏側)。
        var oneSampleTicksFloor = TimeSpan.TicksPerSecond / 44100; // 226
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromTicks(oneSampleTicksFloor));

        Assert.That(AudioProcessContext.GetSampleCount(range, 44100), Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void GetSampleCount_Static_NonPositiveSampleRate_Throws(int sampleRate)
    {
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AudioProcessContext.GetSampleCount(range, sampleRate));
        Assert.That(ex!.ParamName, Is.EqualTo("sampleRate"));
    }

    [Test]
    public void GetSampleCount_Static_OverflowsInt32_Throws()
    {
        // double で計算してから (int) キャストすると、Int32.MaxValue を超える値は
        // 暗黙に未定義動作 (-2147483648 になる実装が多い) になり後段で気付けない。
        // 長時間 × 高サンプルレートで int をはみ出すケースを早期に弾けることを担保する。
        // 例: ~14 時間 @ 48000Hz は 2.4e9 サンプルで Int32.MaxValue (~2.15e9) を超える。
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromHours(14));

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AudioProcessContext.GetSampleCount(range, 48000));
        Assert.That(ex!.ParamName, Is.EqualTo("range"));
    }

    [Test]
    public void GetSampleCount_Static_NegativeDuration_Throws()
    {
        // TimeRange は構造体で Duration の不変条件を保証しない (コンストラクタや WithDuration /
        // SubtractStart などで負値が入り得る)。負の duration を Math.Ceiling に渡すと負の int が返り、
        // 後段 AudioBuffer のコンストラクタで "sampleCount" の例外として出るため、ここで早期に弾いて
        // エラーの帰属を正しく保つ。
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(-1));

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AudioProcessContext.GetSampleCount(range, 44100));
        Assert.That(ex!.ParamName, Is.EqualTo("range"));
    }
}
