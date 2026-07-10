using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Crash-safety of effects fed degenerate parameters, exercised end-to-end through the declarative pipeline. The
// legacy imperative-pipeline harness (FilterEffectActivator / EffectTarget / SKImageFilterBuilder) is gone; the same
// intent is now covered by rendering through GoldenImageHarness, which builds the effect graph, compiles it and runs
// the executor. The executor's C7 empty-bounds skip / delivery-vs-preview drop-or-throw normalization is unit-tested
// directly in EffectPipeline/PrimitivePassTests.
[NonParallelizable]
public sealed class FilterEffectCrashSafetyTests
{
    private static readonly PixelSize Frame = new(320, 180);

    [Test]
    public void ColorShift_split_character_text_with_empty_targets_does_not_throw()
    {
        Assert.DoesNotThrow(() =>
        {
            using Bitmap _ = GoldenImageHarness.RenderAtScale(CreateColorShiftText(), Frame, 1f);
        });
    }

    [Test]
    public void ShakeEffect_extreme_values_render_a_finite_frame()
    {
        // Speed +Inf and NaN/MaxValue strengths would produce a non-finite geometry translation without ShakeEffect's
        // ClampOffset guard, which then yields non-finite pass bounds and an unallocatable buffer. The clamp keeps the
        // translation finite, so the render completes and produces a full-size frame instead of crashing.
        var shape = new RectShape();
        shape.Width.CurrentValue = 150;
        shape.Height.CurrentValue = 60;
        shape.Fill.CurrentValue = Brushes.White;
        shape.FilterEffect.CurrentValue = new ShakeEffect
        {
            Speed = { CurrentValue = float.PositiveInfinity },
            StrengthX = { CurrentValue = float.NaN },
            StrengthY = { CurrentValue = float.MaxValue },
        };

        Bitmap? rendered = null;
        Assert.DoesNotThrow(() => rendered = GoldenImageHarness.RenderAtScale(
            shape.ToResource(CompositionContext.Default), Frame, 1f));
        using Bitmap bitmap = rendered!;
        Assert.That(bitmap.Width, Is.EqualTo(Frame.Width));
        Assert.That(bitmap.Height, Is.EqualTo(Frame.Height));
    }

    [Test]
    public void PixelSort_renders_without_throwing_and_degrades_when_gpu_absent()
    {
        // PixelSort is a Vulkan compute effect; its ComputeFallback.Identity makes the pass a no-op when no compute
        // context is available, so the render must complete without throwing on either path.
        var shape = new RectShape();
        shape.Width.CurrentValue = 120;
        shape.Height.CurrentValue = 80;
        shape.Fill.CurrentValue = Brushes.White;
        shape.FilterEffect.CurrentValue = new PixelSortEffect();

        Assert.DoesNotThrow(() =>
        {
            using Bitmap _ = GoldenImageHarness.RenderAtScale(shape.ToResource(CompositionContext.Default), Frame, 1f);
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
