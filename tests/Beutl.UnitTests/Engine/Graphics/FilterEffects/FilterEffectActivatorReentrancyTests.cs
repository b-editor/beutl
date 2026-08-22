using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

/// <summary>
/// Pins that a Skia item may use the activator's public surface without breaking the chain around it.
/// </summary>
/// <remarks>
/// <see cref="FilterEffectActivator.Activate"/> and <see cref="FilterEffectActivator.Flush"/> both drop the
/// per-target chain bookkeeping, and Activate keeps it only for a builder that has no filter yet. A Skia item
/// runs author code, so an author calling either one from inside it must not leave the loop that follows
/// reading a map that is no longer there.
/// </remarks>
[TestFixture]
public sealed class FilterEffectActivatorReentrancyTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 6);

    [Test]
    public void ASkiaItemThatReentersActivate_LeavesTheChainUsable()
    {
        using EffectTargets targets = CreateSolidTargets(s_bounds);
        using var reentrant = new FilterEffectContext(s_bounds);
        using var context = new FilterEffectContext(s_bounds);
        context._items.Add(new ReentrantSkiaItem(reentrant));
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        Assert.That(() => activator.Apply(context), Throws.Nothing,
            "The activator must re-establish its own bookkeeping after running author code.");
    }

    private sealed record ReentrantSkiaItem(FilterEffectContext Reentrant)
        : FEItem<FilterEffectContext>(Reentrant, static (_, rect) => rect), IFEItem_Skia
    {
        public bool ResolveBoundsAtExecutionTime => false;

        public bool SupportsDirectReplay => false;

        public bool TryTransformSamplingBounds(Rect output, out Rect input)
        {
            input = output;
            return true;
        }

        public void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder)
        {
            builder.AppendSKColorFilter(
                SKColors.White,
                activator,
                static (color, _) => SKColorFilter.CreateBlendMode(color, SKBlendMode.Modulate));
            _ = activator.Activate(Reentrant);
        }

        public void AcceptsDirect(SKImageFilterBuilder builder)
            => throw new InvalidOperationException("The reentrancy fixture has no direct-replay factory.");
    }

    private static EffectTargets CreateSolidTargets(Rect bounds)
    {
        using RenderTarget renderTarget = RenderTarget.Create((int)bounds.Width, (int)bounds.Height)
            ?? throw new InvalidOperationException("A CPU render target is required for this test.");
        using (var canvas = new ImmediateCanvas(
                   renderTarget,
                   density: 1,
                   maxWorkingScale: 1,
                   logicalSize: bounds.Size))
        {
            canvas.Clear(Colors.Red);
        }

        return new EffectTargets
        {
            new EffectTarget(renderTarget, bounds, EffectiveScale.At(1)),
        };
    }
}
