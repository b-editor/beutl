using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

/// <summary>Test infrastructure shared by the contract tests, which cannot use the UnitTests helpers.</summary>
internal static class ContractTestSupport
{
    public static float AlphaAt(Bitmap bitmap, int x, int y)
    {
        Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        return (float)bitmap.GetRow<Half>(y)[x * 4 + 3];
    }

    public static DrawableBrush.Resource CreateDrawableBrushResource()
    {
        var content = new EllipseShape();
        content.Width.CurrentValue = 20;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        return CreateDrawableBrush(content).ToResource(CompositionContext.Default);
    }

    public static DrawableBrush CreateDrawableBrush(Drawable content)
        => new() { Drawable = { CurrentValue = content } };

    public static FilterEffectRenderNode CreateScene(FilterEffect.Resource resource, Rect domain)
    {
        var node = new FilterEffectRenderNode(resource);
        node.AddChild(new EllipseRenderNode(domain, Brushes.Resource.White, null));
        return node;
    }
}

internal sealed class CpuRenderTarget : RenderTarget
{
    private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

    public CpuRenderTarget(PixelSize size, SKColorType colorType = SKColorType.RgbaF16)
        : base(CreateSurface(size, colorType), size.Width, size.Height)
    {
    }

    public int DisposeCalls { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            DisposeCalls++;
        }

        base.Dispose(disposing);
    }

    private static SKSurface CreateSurface(PixelSize size, SKColorType colorType)
    {
        return SKSurface.Create(new SKImageInfo(
                   size.Width,
                   size.Height,
                   colorType,
                   SKAlphaType.Premul,
                   s_colorSpace))
               ?? throw new InvalidOperationException("Could not create a CPU contract-test surface.");
    }
}

internal sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
{
    public override void Process(RenderNodeContext context) => process(context);
}

internal sealed class DelegateContainerNode(Action<RenderNodeContext> process) : ContainerRenderNode
{
    public override void Process(RenderNodeContext context) => process(context);
}

internal sealed class SolidSourceNode(Rect bounds, Color color) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            (bounds, color),
            static (session, state) =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(state.color));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale);
        context.Publish(context.OpaqueSource(description));
    }
}
