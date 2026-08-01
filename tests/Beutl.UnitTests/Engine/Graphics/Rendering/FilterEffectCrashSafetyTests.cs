using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

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
