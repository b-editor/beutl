using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public class ScaleOneContentCoverageTests
{
    private static readonly PixelSize Frame = new(180, 120);

    [Test]
    public void TextBlock_ScaleOne_IsDeterministicByteIdentical()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap a = GoldenImageHarness.RenderAtScale(MakeTextBlock(), Frame, 1f);
            using Bitmap b = GoldenImageHarness.RenderAtScale(MakeTextBlock(), Frame, 1f);
            GoldenImageHarness.AssertByteIdentical(a, b);
        });
    }

    [Test]
    public void UnscaledSourceImage_ScaleOne_IsDeterministicByteIdentical()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap a = GoldenImageHarness.RenderAtScale(MakeUnscaledSourceImage(), Frame, 1f);
            using Bitmap b = GoldenImageHarness.RenderAtScale(MakeUnscaledSourceImage(), Frame, 1f);
            GoldenImageHarness.AssertByteIdentical(a, b);
        });
    }

    private static Drawable.Resource MakeTextBlock()
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var text = new TextBlock();
        text.FontFamily.CurrentValue = typeface.FontFamily;
        text.FontStyle.CurrentValue = typeface.Style;
        text.FontWeight.CurrentValue = typeface.Weight;
        text.Size.CurrentValue = 32;
        text.Fill.CurrentValue = Brushes.White;
        text.Text.CurrentValue = "Scale 1.0 text";
        return text.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource MakeUnscaledSourceImage()
    {
        Uri uri = TestMediaHelper.CreateTestImageUri(64, 64, Colors.White);
        var imageSource = new ImageSource();
        imageSource.ReadFrom(uri);

        var image = new SourceImage();
        image.Source.CurrentValue = imageSource;
        return image.ToResource(CompositionContext.Default);
    }
}
