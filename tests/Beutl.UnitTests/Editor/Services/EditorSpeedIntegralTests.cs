using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Services;
using Beutl.Media;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class EditorSpeedIntegralTests
{
    [Test]
    public void GlobalWindow_ExcludesTheCommonPrefixFromItsErrorAllowance()
    {
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = true };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100 });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(60000), Value = 300, Easing = new LinearEasing() });
        var editor = new EditorSpeedIntegral(animation, 48000);

        EditorSpeedIntegral.Estimate estimate = editor.Integrate(TimeSpan.FromSeconds(30000), TimeSpan.FromSeconds(30002));

        Assert.Multiple(() =>
        {
            Assert.That(estimate.Seconds, Is.EqualTo(4 + 4d / 60000).Within(0.00001));
            Assert.That(estimate.Error, Is.LessThan(0.0001));
        });
    }

    private static IEnumerable<object[]> Curves()
    {
        foreach (string kind in new[] { "linear", "quadratic", "bounce", "elastic", "hold", "spline" })
            foreach (double time in new[] { 0.0123456, 0.75, 2.3, 4.75 })
                yield return [kind, time];
    }

    [TestCaseSource(nameof(Curves))]
    public void Estimate_CoversPlaybackSumWithinItsNumericalAllowance(string kind, double seconds)
    {
        Easing easing = kind switch
        {
            "quadratic" => new QuadraticEaseInOut(),
            "bounce" => new BounceEaseOut(),
            "elastic" => new ElasticEaseOut(),
            "hold" => new HoldEasing(),
            "spline" => new SplineEasing(0.2f, 0.8f, 0.7f, 0.1f),
            _ => new LinearEasing()
        };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(-0.75), Value = 175 });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(0.4), Value = 50, Easing = easing });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1.75), Value = 300, Easing = easing });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(4.2), Value = 100, Easing = easing });
        var editor = new EditorSpeedIntegral(animation, 44100);
        using var playback = new SpeedIntegrator(44100);
        playback.EnsureCache(animation);
        TimeSpan time = TimeSpan.FromSeconds(seconds);

        EditorSpeedIntegral.Estimate estimate = editor.Integrate(time);
        double actual = playback.Integrate(time, animation).TotalSeconds;

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(estimate.Seconds).Within(estimate.Error + 1e-7));
            Assert.That(estimate.Error, Is.LessThan(0.002), "The allowance must remain useful for interactive trimming.");
        });
    }
}
