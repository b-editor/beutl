using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[NonParallelizable]
public sealed class FilterEffectCrashSafetyTests
{
    private static readonly PixelSize Frame = new(320, 180);

    // Each direction samples a different edge of the source.
    [TestCase(100, 0)]
    [TestCase(-100, 0)]
    [TestCase(0, 100)]
    [TestCase(0, -100)]
    public void ColorShift_offsets_beyond_source_have_no_decal_fringe_at_quarter_scale(
        int offsetX,
        int offsetY)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new ColorShift
            {
                RedOffset = { Value = new PixelPoint(offsetX, offsetY) },
            };
            var shape = new RectShape
            {
                Width = { Value = 100 },
                Height = { Value = 100 },
                AlignmentX = { Value = AlignmentX.Left },
                AlignmentY = { Value = AlignmentY.Top },
                Fill = { Value = Brushes.Red },
                FilterEffect = { Value = effect },
            };
            using Drawable.Resource drawable = shape.ToResource(CompositionContext.Default);
            using Bitmap bitmap = GoldenImageHarness.RenderAtScale(
                drawable,
                new PixelSize(200, 100),
                0.25f,
                clearColor: Colors.Transparent);

            for (int y = 0; y < 25; y++)
            {
                for (int x = 0; x < 25; x++)
                {
                    SKColor pixel = bitmap.SKBitmap.GetPixel(x, y);
                    Assert.That(
                        pixel.Red,
                        Is.Zero,
                        $"The red sample is outside the source domain at device pixel ({x}, {y}).");
                }
            }
        });
    }

    [Test]
    public void ColorShift_split_character_text_with_empty_targets_does_not_throw()
    {
        Assert.DoesNotThrow(() =>
        {
            using Bitmap _ = GoldenImageHarness.RenderAtScale(CreateColorShiftText(), Frame, 1f);
        });
    }

    [Test]
    public void ShakeEffect_extreme_values_keep_target_bounds_finite()
    {
        using var source = RenderTarget.Create(100, 60);
        Assert.That(source, Is.Not.Null, "A CPU RenderTarget is required for this test.");

        using var targets = new EffectTargets
        {
            new EffectTarget(source!, new Rect(0, 0, 100, 60)),
        };
        using var feCtx = new FilterEffectContext(new Rect(0, 0, 100, 60));
        var effect = new ShakeEffect
        {
            Speed = { Value = float.PositiveInfinity },
            StrengthX = { Value = float.NaN },
            StrengthY = { Value = float.MaxValue }
        };
        feCtx.ApplyTransactional(effect.ToResource(new CompositionContext(MediaTime.Zero)));

        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: null);
        Assert.DoesNotThrow(() => executor.Apply(feCtx));

        foreach (EffectTarget target in executor.CurrentTargets)
        {
            Assert.That(IsFinite(target.Bounds), Is.True, $"Shaken bounds must stay finite; got {target.Bounds}.");
            Assert.That(IsFinite(target.OriginalBounds), Is.True, $"Original bounds must stay finite; got {target.OriginalBounds}.");
        }
    }

    private static bool IsFinite(Rect rect)
        => double.IsFinite(rect.X)
           && double.IsFinite(rect.Y)
           && double.IsFinite(rect.Width)
           && double.IsFinite(rect.Height);

    [Test]
    public void PixelSort_half_initialized_gpu_path_degrades_to_noop()
    {
        if (GraphicsContextFactory.IsVulkanValidationEnabled())
        {
            Assert.Ignore(
                "This test creates a zero-extent attachment on purpose, which validation reports as the "
                + "error it is; the degrade-to-no-op behaviour is covered without validation.");
        }

        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var sourceRenderTarget = RenderTarget.Create(0, 0);
            if (sourceRenderTarget is null)
            {
                const string reason = "Zero-sized RenderTarget is unavailable in this backend.";
                Assert.Ignore(reason);
            }

            using var targets = new EffectTargets
            {
                new EffectTarget(sourceRenderTarget!, new Rect(0, 0, 0, 0)),
            };
            using var feCtx = new FilterEffectContext(new Rect(0, 0, 0, 0));
            var effect = new PixelSortEffect();
            feCtx.ApplyTransactional(effect.ToResource(new CompositionContext(MediaTime.Zero)));

            using var builder = new SKImageFilterBuilder();
            using var executor = new FilterEffectExecutor(
                targets,
                builder,
                RenderIntent.Delivery,
                RenderRequestPurpose.Auxiliary,
                drawableBrushMaterializer: null);
            Assert.DoesNotThrow(() => executor.Apply(feCtx));
        });
    }

    private static Drawable.Resource CreateColorShiftText()
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var text = new TextBlock
        {
            Text = { Value = "Effects & shaders" },
            Size = { Value = 24 },
            Fill = { Value = Brushes.White },
            SplitByCharacters = { Value = true },
            FilterEffect =
            {
                Value = new ColorShift
                {
                    RedOffset = { Value = new PixelPoint(2, 0) },
                    GreenOffset = { Value = new PixelPoint(0, 1) },
                    BlueOffset = { Value = new PixelPoint(-2, 0) }
                }
            }
        };
        text.FontFamily.Value = typeface.FontFamily;
        text.FontStyle.Value = typeface.Style;
        text.FontWeight.Value = typeface.Weight;
        return text.ToResource(CompositionContext.Default);
    }
}
