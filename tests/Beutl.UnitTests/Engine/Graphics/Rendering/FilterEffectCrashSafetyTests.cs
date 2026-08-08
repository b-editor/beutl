using Beutl.Composition;
using Beutl.Graphics;
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

    [Test]
    public void ColorShift_representative_offset_beyond_source_has_no_decal_fringe_at_quarter_scale()
    {
        AssertColorShiftHasNoDecalFringe(offsetX: 100, offsetY: 0);
    }

    // The other directions stay in the Explicit orchestrator gate named below; the default suite
    // retains the positive-horizontal case that originally exposed the decal fringe.
    [TestCase(-100, 0)]
    [TestCase(0, 100)]
    [TestCase(0, -100)]
    [Explicit(
        "Orchestrator gate: dotnet test tests/Beutl.UnitTests -f net10.0 "
        + "--filter \"FullyQualifiedName~FilterEffectCrashSafetyTests.ColorShift_dense_offsets_beyond_source_have_no_decal_fringe_at_quarter_scale\"")]
    public void ColorShift_dense_offsets_beyond_source_have_no_decal_fringe_at_quarter_scale(
        int offsetX,
        int offsetY)
    {
        AssertColorShiftHasNoDecalFringe(offsetX, offsetY);
    }

    private static void AssertColorShiftHasNoDecalFringe(int offsetX, int offsetY)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new ColorShift
            {
                RedOffset = { CurrentValue = new PixelPoint(offsetX, offsetY) },
            };
            var shape = new RectShape
            {
                Width = { CurrentValue = 100 },
                Height = { CurrentValue = 100 },
                AlignmentX = { CurrentValue = AlignmentX.Left },
                AlignmentY = { CurrentValue = AlignmentY.Top },
                Fill = { CurrentValue = Brushes.Red },
                FilterEffect = { CurrentValue = effect },
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
            Speed = { CurrentValue = float.PositiveInfinity },
            StrengthX = { CurrentValue = float.NaN },
            StrengthY = { CurrentValue = float.MaxValue }
        };
        effect.ApplyTo(feCtx, effect.ToResource(new CompositionContext(TimeSpan.Zero)));

        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary);
        Assert.DoesNotThrow(() => activator.Apply(feCtx));

        foreach (EffectTarget target in activator.CurrentTargets)
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
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var sourceRenderTarget = RenderTarget.Create(0, 0);
            if (sourceRenderTarget is null)
            {
                Assert.Pass("Zero-sized RenderTarget is unavailable in this backend.");
            }

            using var targets = new EffectTargets
            {
                new EffectTarget(sourceRenderTarget!, new Rect(0, 0, 0, 0)),
            };
            using var feCtx = new FilterEffectContext(new Rect(0, 0, 0, 0));
            var effect = new PixelSortEffect();
            effect.ApplyTo(feCtx, effect.ToResource(new CompositionContext(TimeSpan.Zero)));

            using var builder = new SKImageFilterBuilder();
            using var activator = new FilterEffectActivator(
                targets,
                builder,
                RenderIntent.Delivery,
                RenderRequestPurpose.Auxiliary);
            Assert.DoesNotThrow(() => activator.Apply(feCtx));
        });
    }

    private static Drawable.Resource CreateColorShiftText()
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var text = new TextBlock
        {
            Text = { CurrentValue = "Effects & shaders" },
            Size = { CurrentValue = 24 },
            Fill = { CurrentValue = Brushes.White },
            SplitByCharacters = { CurrentValue = true },
            FilterEffect =
            {
                CurrentValue = new ColorShift
                {
                    RedOffset = { CurrentValue = new PixelPoint(2, 0) },
                    GreenOffset = { CurrentValue = new PixelPoint(0, 1) },
                    BlueOffset = { CurrentValue = new PixelPoint(-2, 0) }
                }
            }
        };
        text.FontFamily.CurrentValue = typeface.FontFamily;
        text.FontStyle.CurrentValue = typeface.Style;
        text.FontWeight.CurrentValue = typeface.Weight;
        return text.ToResource(CompositionContext.Default);
    }
}
