using Beutl.Composition;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;

namespace Beutl.UnitTests.Engine.Graphics;

public sealed class PosterizeTimeTests
{
    [TestCase(0.3, 0.74, 0.55)]
    [TestCase(1.3, 1.74, 1.55)]
    public void Posterize_time_holds_the_input_on_the_elements_frame_grid(double start, double at, double expected)
    {
        RectShape shape = MotionBlurTests.Rectangle();
        shape.FilterEffect.Value = new PosterizeTime { FrameRate = { Value = 4 } };
        _ = MotionBlurTests.Clip(start, 4, 0, shape);
        using var resource = shape.ToResource(new CompositionContext(MediaTime.FromSeconds(at)) { Fonts = TestFonts.Manager });

        var effect = (PosterizeTime.Resource)resource.FilterEffect!;
        Assert.That(effect.Sample!.Time, Is.EqualTo(MediaTime.FromSeconds(expected)));
    }
}
