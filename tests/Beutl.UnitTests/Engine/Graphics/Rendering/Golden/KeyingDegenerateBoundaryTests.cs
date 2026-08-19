using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// Pins that keying a solid fill against its own colour removes it whatever the boundary is.
/// </summary>
/// <remarks>
/// A solid fill reaches the shader quantized onto an 8-bit colour grid, so its luma sits up to one 8-bit
/// step away from the CPU-computed key uniform. Comparing on exact equality made the whole mask hinge on
/// that step - and at Boundary 0 it hinged on it through a zero-width smoothstep, which the shading
/// languages leave undefined. Boundary 0 is the natural authoring choice for a hard key, so the shaders
/// carry a match tolerance instead.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class KeyingDegenerateBoundaryTests
{
    private static readonly Color s_key = Color.FromRgb(206, 92, 42);
    private static readonly PixelSize s_frame = new(64, 48);

    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(2f)]
    [Category("GpuPassFusionGpu")]
    public void ColorKey_OnItsOwnFlatFill_RemovesEverything(float boundary)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var key = new ColorKey();
            key.Color.CurrentValue = s_key;
            key.Range.CurrentValue = 0f;
            key.Boundary.CurrentValue = boundary;

            AssertKeyedAway(key, $"ColorKey Boundary={boundary:R}");
        });
    }

    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(2f)]
    [Category("GpuPassFusionGpu")]
    public void ChromaKey_OnItsOwnFlatFill_RemovesEverything(float boundary)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var key = new ChromaKey();
            key.Color.CurrentValue = s_key;
            key.HueRange.CurrentValue = 0f;
            key.SaturationRange.CurrentValue = 0f;
            key.Boundary.CurrentValue = boundary;

            AssertKeyedAway(key, $"ChromaKey Boundary={boundary:R}");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ColorKey_LeavesAColourTheKeyDoesNotMatch()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var key = new ColorKey();
            key.Color.CurrentValue = Colors.Blue;
            key.Range.CurrentValue = 0f;
            key.Boundary.CurrentValue = 0f;

            using Drawable.Resource resource = CreateFlatRect(key);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                resource, s_frame, 1f, clearColor: Colors.Transparent);

            Assert.That(
                MaximumAlpha(rendered),
                Is.GreaterThan(0.5f),
                "A tolerance that swallowed an unrelated key colour would make the effect useless.");
        });
    }

    private static void AssertKeyedAway(FilterEffect key, string label)
    {
        using Drawable.Resource resource = CreateFlatRect(key);
        using Bitmap rendered = GoldenImageHarness.RenderAtScale(
            resource, s_frame, 1f, clearColor: Colors.Transparent);

        Assert.That(
            MaximumAlpha(rendered),
            Is.Zero,
            $"{label}: keying a solid fill against its own colour must remove every pixel.");
    }

    private static Drawable.Resource CreateFlatRect(FilterEffect? effect = null)
    {
        var rect = new RectShape();
        rect.Width.CurrentValue = 40f;
        rect.Height.CurrentValue = 30f;
        rect.Fill.CurrentValue = new SolidColorBrush(s_key);
        if (effect is not null)
            rect.FilterEffect.CurrentValue = effect;
        return (Drawable.Resource)rect.ToResource(CompositionContext.Default);
    }

    private static float MaximumAlpha(Bitmap bitmap)
    {
        float maximum = 0f;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            for (int x = 0; x < bitmap.Width; x++)
                maximum = MathF.Max(maximum, (float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]));
        }

        return maximum;
    }
}
