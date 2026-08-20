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
/// A solid fill reaches the shaders quantized onto an 8-bit grid in the render target's colour space, which is
/// linear light, so it arrives up to half a linear code away from the CPU-computed key uniform. Half a linear
/// code spans about ten sRGB levels near black, so a tolerance carried after the transfer curve cannot absorb
/// it: the dark cases below collapse onto an exact grey, and their saturation then disagrees with the key by
/// two orders of magnitude more than a one-8-bit-step tolerance. Only an axis-aligned rectangle ever gave Skia
/// a full-coverage quad, so the ellipse cases pin a shape that was wrong before the fused pipeline as well.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class KeyingDegenerateBoundaryTests
{
    private static readonly Color s_key = Color.FromRgb(206, 92, 42);
    private static readonly PixelSize s_frame = new(64, 48);

    private static readonly float[] s_boundaries = [0f, 0.5f, 2f];

    private static readonly Color[] s_chromaKeys =
    [
        s_key,
        Color.FromRgb(20, 18, 22),
        Color.FromRgb(10, 40, 20),
        Color.FromRgb(12, 12, 12),
    ];

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

            AssertKeyedAway(key, s_key, ellipse: false, $"ColorKey Boundary={boundary:R}");
        });
    }

    [TestCaseSource(nameof(ChromaKeySelfKeyCases))]
    [Category("GpuPassFusionGpu")]
    public void ChromaKey_OnItsOwnFlatFill_RemovesEverything(Color key, float boundary, bool ellipse)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new ChromaKey();
            effect.Color.CurrentValue = key;
            effect.HueRange.CurrentValue = 0f;
            effect.SaturationRange.CurrentValue = 0f;
            effect.Boundary.CurrentValue = boundary;

            AssertKeyedAway(
                effect,
                key,
                ellipse,
                $"ChromaKey rgb({key.R},{key.G},{key.B}) Boundary={boundary:R} Ellipse={ellipse}");
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

            AssertSurvives(key, s_key, ellipse: false, "ColorKey Blue");
        });
    }

    [TestCaseSource(nameof(ChromaKeyNonMatchCases))]
    [Category("GpuPassFusionGpu")]
    public void ChromaKey_LeavesAColourTheKeyDoesNotMatch(Color fill, Color key, bool ellipse)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new ChromaKey();
            effect.Color.CurrentValue = key;
            effect.HueRange.CurrentValue = 0f;
            effect.SaturationRange.CurrentValue = 0f;
            effect.Boundary.CurrentValue = 0f;

            AssertSurvives(
                effect,
                fill,
                ellipse,
                $"ChromaKey rgb({key.R},{key.G},{key.B}) over rgb({fill.R},{fill.G},{fill.B}) Ellipse={ellipse}");
        });
    }

    private static IEnumerable<object[]> ChromaKeySelfKeyCases()
    {
        foreach (Color key in s_chromaKeys)
        {
            foreach (float boundary in s_boundaries)
            {
                foreach (bool ellipse in new[] { false, true })
                    yield return [key, boundary, ellipse];
            }
        }
    }

    private static IEnumerable<object[]> ChromaKeyNonMatchCases()
    {
        (Color Fill, Color Key)[] pairs =
        [
            (s_key, Colors.Blue),
            (Color.FromRgb(20, 18, 22), Colors.Lime),
            (Color.FromRgb(10, 40, 20), Color.FromRgb(60, 10, 10)),
            (Color.FromRgb(12, 12, 12), Colors.Lime),
        ];

        foreach ((Color fill, Color key) in pairs)
        {
            foreach (bool ellipse in new[] { false, true })
                yield return [fill, key, ellipse];
        }
    }

    private static void AssertKeyedAway(FilterEffect key, Color fill, bool ellipse, string label)
    {
        Assert.That(
            RenderMaximumAlpha(key, fill, ellipse),
            Is.Zero,
            $"{label}: keying a solid fill against its own colour must remove every pixel.");
    }

    private static void AssertSurvives(FilterEffect key, Color fill, bool ellipse, string label)
    {
        Assert.That(
            RenderMaximumAlpha(key, fill, ellipse),
            Is.GreaterThan(0.5f),
            $"{label}: a tolerance that swallowed an unrelated key colour would make the effect useless.");
    }

    private static float RenderMaximumAlpha(FilterEffect key, Color fill, bool ellipse)
    {
        using Drawable.Resource resource = CreateFlatShape(fill, ellipse, key);
        using Bitmap rendered = GoldenImageHarness.RenderAtScale(
            resource, s_frame, 1f, clearColor: Colors.Transparent);

        return MaximumAlpha(rendered);
    }

    private static Drawable.Resource CreateFlatShape(Color fill, bool ellipse, FilterEffect? effect = null)
    {
        Shape shape;
        if (ellipse)
        {
            var ellipseShape = new EllipseShape();
            ellipseShape.Width.CurrentValue = 40f;
            ellipseShape.Height.CurrentValue = 30f;
            shape = ellipseShape;
        }
        else
        {
            var rect = new RectShape();
            rect.Width.CurrentValue = 40f;
            rect.Height.CurrentValue = 30f;
            shape = rect;
        }

        shape.Fill.CurrentValue = new SolidColorBrush(fill);
        if (effect is not null)
            shape.FilterEffect.CurrentValue = effect;
        return (Drawable.Resource)shape.ToResource(CompositionContext.Default);
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
