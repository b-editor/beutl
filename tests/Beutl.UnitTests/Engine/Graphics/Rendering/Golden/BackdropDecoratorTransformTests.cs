using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class BackdropDecoratorTransformTests
{
    private static readonly PixelSize s_frame = new(256, 144);

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void DecoratorTransformAroundBackdrop_MatchesTransformOnBackdrop(float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource expectedResource = CreateScene(decorateBackdrop: false);
            using Drawable.Resource actualResource = CreateScene(decorateBackdrop: true);
            using Bitmap expected = GoldenImageHarness.RenderAtScale(expectedResource, s_frame, scale);
            using Bitmap actual = GoldenImageHarness.RenderAtScale(actualResource, s_frame, scale);

            double ssim = ImageMetrics.Ssim(expected, actual);
            double mae = ImageMetrics.MeanAbsoluteError(expected, actual);
            Assert.That(
                actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()),
                Is.True,
                $"The transformed backdrop differs at scale {scale}: SSIM={ssim:F6}, MAE={mae:F6}.");
        });
    }

    private static Drawable.Resource CreateScene(bool decorateBackdrop)
    {
        var scene = new DrawableGroup();
        scene.Children.Add(CreateRectangle(s_frame.Width, s_frame.Height, Colors.DimGray));
        scene.Children.Add(CreateRectangle(130, 95, Colors.Navy));

        var backdrop = new SourceBackdrop();
        backdrop.FilterEffect.CurrentValue = new Invert();
        if (decorateBackdrop)
        {
            var decorator = new DrawableDecorator();
            decorator.Children.Add(backdrop);
            decorator.Transform.CurrentValue = new RotationTransform(24);
            scene.Children.Add(decorator);
        }
        else
        {
            backdrop.Transform.CurrentValue = new RotationTransform(24);
            scene.Children.Add(backdrop);
        }

        return scene.ToResource(CompositionContext.Default);
    }

    private static RectShape CreateRectangle(float width, float height, Color color)
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = width;
        shape.Height.CurrentValue = height;
        shape.Fill.CurrentValue = new SolidColorBrush(color);
        return shape;
    }
}
