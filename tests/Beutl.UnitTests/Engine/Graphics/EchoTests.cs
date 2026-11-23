using Beutl.Composition;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;

namespace Beutl.UnitTests.Engine.Graphics;

public sealed class EchoTests
{
    [TestCase(-0.25f, 0.5, 0.25)]
    [TestCase(0.25f, 1, 1.25)]
    public void Echo_samples_in_both_time_directions_and_decays_each_copy(float interval, double second, double third)
    {
        RectShape shape = MotionBlurTests.Rectangle();
        shape.FilterEffect.Value = new Echo
        {
            Interval = { Value = interval },
            Count = { Value = 3 },
            StartingIntensity = { Value = 80 },
            Decay = { Value = 50 },
        };
        using var resource = shape.ToResource(new CompositionContext(MediaTime.FromSeconds(0.75)) { Fonts = TestFonts.Manager });

        var effect = (Echo.Resource)resource.FilterEffect!;
        Assert.That(effect.TimeSamples.Select(sample => sample.Sample.Time.ToSeconds()), Is.EqualTo(new[] { 0.75, second, third }));
        Assert.That(effect.TimeSamples.Select(sample => sample.Opacity), Is.EqualTo(new[] { 0.8f, 0.4f, 0.2f }));
    }
}
