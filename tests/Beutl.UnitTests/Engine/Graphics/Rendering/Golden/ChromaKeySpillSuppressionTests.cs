using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[TestFixture]
[NonParallelizable]
public sealed class ChromaKeySpillSuppressionTests
{
    private const float Tolerance = 0.001f;
    private static readonly PixelSize s_frame = new(16, 16);
    private static readonly Color s_greenishFill = Color.FromArgb(160, 120, 170, 110);

    [Test]
    public void LimeKey_BlendsGreenTowardTheOtherChannelsInLinearLight()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap baseline = Render(s_greenishFill, Colors.Lime);
            using Bitmap zero = Render(s_greenishFill, Colors.Lime, 0f);
            using Bitmap half = Render(s_greenishFill, Colors.Lime, 50f);
            using Bitmap full = Render(s_greenishFill, Colors.Lime, 100f);

            GoldenImageHarness.AssertByteIdentical(baseline, zero, requireVisibleContent: true);

            Rgba original = ReadCenter(baseline);
            Rgba halfSuppressed = ReadCenter(half);
            Rgba fullySuppressed = ReadCenter(full);
            // The readback is linear and premultiplied, so the common alpha factors out of the average.
            float greenLimit = (original.Red + original.Blue) * 0.5f;
            Assert.Multiple(() =>
            {
                Assert.That(original.Alpha, Is.EqualTo(s_greenishFill.A / 255f).Within(Tolerance));
                Assert.That(original.Green, Is.GreaterThan(greenLimit));
                Assert.That(halfSuppressed.Green, Is.EqualTo((original.Green + greenLimit) * 0.5f).Within(Tolerance));
                Assert.That(fullySuppressed.Green, Is.EqualTo(greenLimit).Within(Tolerance));
                Assert.That(fullySuppressed.Red, Is.EqualTo(original.Red).Within(Tolerance));
                Assert.That(fullySuppressed.Blue, Is.EqualTo(original.Blue).Within(Tolerance));
                Assert.That(halfSuppressed.Alpha, Is.EqualTo(original.Alpha));
                Assert.That(fullySuppressed.Alpha, Is.EqualTo(original.Alpha));
            });
        });
    }

    [TestCase(0, 0, 0)]
    [TestCase(128, 128, 128)]
    [TestCase(12, 13, 12)]
    public void KeyWithLowChroma_FullSuppression_PreservesTheKeptColor(byte red, byte green, byte blue)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Color key = Color.FromRgb(red, green, blue);
            using Bitmap baseline = Render(s_greenishFill, key);
            using Bitmap suppressed = Render(s_greenishFill, key, 100f);

            GoldenImageHarness.AssertByteIdentical(baseline, suppressed, requireVisibleContent: true);
        });
    }

    [Test]
    public void BlueKey_LimitsBlueAndPreservesGreen()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var fill = Color.FromArgb(160, 80, 160, 170);
            using Bitmap baseline = Render(fill, Colors.Blue);
            using Bitmap suppressed = Render(fill, Colors.Blue, 100f);

            Rgba original = ReadCenter(baseline);
            Rgba result = ReadCenter(suppressed);
            float blueLimit = (original.Red + original.Green) * 0.5f;
            Assert.Multiple(() =>
            {
                Assert.That(original.Alpha, Is.EqualTo(fill.A / 255f).Within(Tolerance));
                Assert.That(original.Blue, Is.GreaterThan(blueLimit));
                Assert.That(result.Blue, Is.EqualTo(blueLimit).Within(Tolerance));
                Assert.That(result.Green, Is.EqualTo(original.Green).Within(Tolerance));
                Assert.That(result.Red, Is.EqualTo(original.Red).Within(Tolerance));
                Assert.That(result.Alpha, Is.EqualTo(original.Alpha));
            });
        });
    }

    [Test]
    public void FullSuppression_LeavesAKeyedAwayPixelTransparent()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = Render(Colors.Lime, Colors.Lime, 100f);
            Assert.That(ReadCenter(rendered), Is.EqualTo(default(Rgba)));
        });
    }

    private static Bitmap Render(Color fill, Color key, float? spillSuppression = null)
    {
        var effect = new ChromaKey { Color = { Value = key } };
        if (spillSuppression.HasValue)
            effect.SpillSuppression.Value = spillSuppression.Value;

        var shape = new RectShape
        {
            Width = { Value = s_frame.Width },
            Height = { Value = s_frame.Height },
            Fill = { Value = new SolidColorBrush(fill) },
            FilterEffect = { Value = effect },
        };
        using Drawable.Resource resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
        return GoldenImageHarness.RenderAtScale(resource, s_frame, 1f, clearColor: Colors.Transparent);
    }

    private static Rgba ReadCenter(Bitmap bitmap)
        => ImageMetrics.ReadPixel(bitmap, bitmap.Width / 2, bitmap.Height / 2);
}
