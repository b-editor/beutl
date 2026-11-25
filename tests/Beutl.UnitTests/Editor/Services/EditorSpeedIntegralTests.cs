using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Services;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class EditorSpeedIntegralTests
{
    [Test]
    public void GlobalWindow_ExcludesTheCommonPrefixFromItsErrorAllowance()
    {
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = { Value = true } };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 100 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(60000) }, Value = { Value = 300 }, Easing = { Value = new LinearEasing() } });
        var editor = new EditorSpeedIntegral(animation, 48000);

        EditorSpeedIntegral.Estimate estimate = editor.Integrate(MediaTime.FromSeconds(30000), MediaTime.FromSeconds(30002));

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
    public void Estimate_CoversTheDiscreteAudioSumWithinItsNumericalAllowance(string kind, double seconds)
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
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(-0.75) }, Value = { Value = 175 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(0.4) }, Value = { Value = 50 }, Easing = { Value = easing } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1.75) }, Value = { Value = 300 }, Easing = { Value = easing } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(4.2) }, Value = { Value = 100 }, Easing = { Value = easing } });
        const int SampleRate = 44100;
        var editor = new EditorSpeedIntegral(animation, SampleRate);
        MediaTime time = MediaTime.FromSeconds(seconds);

        EditorSpeedIntegral.Estimate estimate = editor.Integrate(time);
        double samples = time.ToSeconds() * SampleRate;
        long whole = (long)samples;
        double sum = 0;
        for (long i = 0; i < whole; i++)
            sum += animation.Interpolate(MediaTime.FromSamples(i, SampleRate)) / (100d * SampleRate);
        sum += animation.Interpolate(time) * (samples - whole) / (100d * SampleRate);

        Assert.Multiple(() =>
        {
            Assert.That(sum, Is.EqualTo(estimate.Seconds).Within(estimate.Error + 1e-7));
            Assert.That(estimate.Error, Is.LessThan(0.002), "The allowance must remain useful for interactive trimming.");
        });
    }
}
