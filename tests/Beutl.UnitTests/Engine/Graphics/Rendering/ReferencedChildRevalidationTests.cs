using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[NonParallelizable]
[TestFixture]
public class ReferencedChildRevalidationTests
{
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
            renderScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            surface: new CpuRenderTarget(16, 16));

    private static CompositionFrame CreateFrame(params Drawable[] drawables)
        => new(
            [.. drawables.Select(static drawable =>
                (EngineObject.Resource)drawable.ToResource(CompositionContext.Default))],
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(16, 16));

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
    private readonly RenderNode _child;

    public ReferencingDrawable(RenderNode child)
    {
        _child = child;
    }

    public bool LastRecordingObservedChange { get; private set; }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => context.DrawNode(
            _child,
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
