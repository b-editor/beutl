using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class FilterEffectActivatorFlushPlanTests
{
    private static readonly Rect s_leadingBounds = new(0, 0, 8, 6);
    private static readonly Rect s_trailingBounds = new(0, 0, 4, 3);

    /// <summary>
    /// A flush measures every target before it allocates any of them, because one target's dimension clamp
    /// lowers the density the rest are allocated at. The measurements are held positionally, and the pass
    /// that consumes them removes targets from the list they were measured against - so a target behind a
    /// dropped one must still find its own measurement rather than the dropped one's.
    /// </summary>
    [Test]
    public void AFlushThatDropsAnEmptyTarget_StillFindsTheMeasurementsBehindIt()
    {
        using var targets = new EffectTargets
        {
            CreateSolidTarget(s_leadingBounds),
            // A target with no bounds and no backing surface: renderable-but-empty, which the flush drops
            // in every render intent rather than reporting as an allocation failure.
            new EffectTarget(),
            CreateSolidTarget(s_trailingBounds),
        };
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: null,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);
        builder.AppendSKColorFilter(
            SKColors.White,
            activator,
            static (color, _) => SKColorFilter.CreateBlendMode(color, SKBlendMode.Modulate));

        activator.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(targets, Has.Count.EqualTo(2), "only the empty target may be dropped");
            Assert.That(targets[0].Bounds, Is.EqualTo(s_leadingBounds));
            Assert.That(targets[1].Bounds, Is.EqualTo(s_trailingBounds));
            Assert.That(targets[0].RenderTarget, Is.Not.Null);
            Assert.That(targets[1].RenderTarget, Is.Not.Null);
        });
    }

    private static EffectTarget CreateSolidTarget(Rect bounds)
    {
        using RenderTarget renderTarget = RenderTarget.Create((int)bounds.Width, (int)bounds.Height)
            ?? throw new InvalidOperationException("A CPU render target is required for this test.");
        using (var canvas = new ImmediateCanvas(
                   renderTarget,
                   RenderIntent.Preview,
                   density: 1,
                   maxWorkingScale: 1,
                   logicalSize: bounds.Size))
        {
            canvas.Clear(Colors.Red);
        }

        return new EffectTarget(renderTarget, bounds, EffectiveScale.At(1));
    }
}
