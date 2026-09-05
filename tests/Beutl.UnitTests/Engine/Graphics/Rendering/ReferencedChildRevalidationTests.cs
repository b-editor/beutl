using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[NonParallelizable]
[TestFixture]
public class ReferencedChildRevalidationTests
{
    [Test]
    public void ReferencedChild_ReportsAChangeOnlyWhenTheReferencePointsSomewhereElse()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using var first = new RectangleRenderNode(new Rect(0, 0, 4, 4), Brushes.Resource.White, null);
            using var second = new RectangleRenderNode(new Rect(0, 0, 4, 4), Brushes.Resource.White, null);
            var drawable = new ReferencingDrawable(first);
            using Renderer renderer = CreateRenderer();

            renderer.Render(CreateFrame(drawable));
            bool steadyState = drawable.LastRecordingObservedChange;

            drawable.Child = second;
            renderer.Render(CreateFrame(drawable));
            bool afterSwap = drawable.LastRecordingObservedChange;

            Assert.Multiple(() =>
            {
                Assert.That(steadyState, Is.False,
                    "Re-recording an unchanged reference must not invalidate what depends on it.");
                Assert.That(afterSwap, Is.True,
                    "Pointing the reference at another node must revalidate it.");
                Assert.That(FindReference(renderer, drawable).Child, Is.SameAs(second));
            });
        });
    }

    [Test]
    public void ARecordingFailure_LeavesTheRendererAbleToRecordTheNextFrame()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var drawable = new SwitchableFaultingDrawable();
            using Renderer renderer = CreateRenderer();
            renderer.Render(CreateFrame(drawable));

            drawable.ShouldFault = true;
            Assert.Throws<InvalidOperationException>(() => renderer.Render(CreateFrame(drawable)));

            drawable.ShouldFault = false;
            Assert.DoesNotThrow(
                () => renderer.Render(CreateFrame(drawable)),
                "A failed recording must not poison the retained node tree.");
        });
    }

    private static ReferencesChildRenderNode FindReference(Renderer renderer, Drawable drawable)
    {
        DrawableRenderNode node = renderer.FindRenderNode(drawable)
            ?? throw new InvalidOperationException("The drawable was never recorded.");
        return node.Children.OfType<ReferencesChildRenderNode>().Single();
    }

    private static Renderer CreateRenderer()
        => new(
            width: 16,
            height: 16,
            intent: RenderIntent.Preview,
            renderScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            surface: new CpuRenderTarget(16, 16));

    private static CompositionFrame CreateFrame(params Drawable[] drawables)
        => new(
            [.. drawables.Select(static drawable =>
                (EngineObject.Resource)drawable.ToResource(CompositionContext.Default))],
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(16, 16),
            null);

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}

// Top-level partial because EngineObjectResourceGenerator does not support nested types.
internal sealed partial class ReferencingDrawable : Drawable
{
    public ReferencingDrawable(RenderNode child)
    {
        Child = child;
    }

    public RenderNode Child { get; set; }

    public bool LastRecordingObservedChange { get; private set; }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => context.DrawNode(
            Child,
            static node => new ReferencesChildRenderNode(node),
            (reference, node) => LastRecordingObservedChange = reference.Update(node));

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(4, 4);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed partial class SwitchableFaultingDrawable : Drawable
{
    public bool ShouldFault { get; set; }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        context.DrawRectangle(new Rect(0, 0, 4, 4), Brushes.Resource.White, null);
        if (ShouldFault)
        {
            throw new InvalidOperationException("recording failed");
        }
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(4, 4);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}
