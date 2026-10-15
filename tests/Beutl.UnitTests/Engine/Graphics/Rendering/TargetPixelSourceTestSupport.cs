using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Records a render target's pixels as an opaque source, for tests that need an input whose pixels they choose.
internal static class TargetPixelSourceTestSupport
{
    private static readonly RenderResourceSlot<SKImage> s_imageSlot = new();

    /// <summary>
    /// Records <paramref name="source"/>'s pixels over <paramref name="bounds"/> at <paramref name="density"/>
    /// device pixels per logical unit.
    /// </summary>
    public static RenderFragmentHandle RecordTargetPixels(
        RenderNodeContext context,
        RenderTarget source,
        Rect bounds,
        float density = 1,
        RenderHitTestContract? hitTest = null)
    {
        RenderResource<SKImage> image = context.Own(source.Value.Snapshot());
        return context.OpaqueSource(OpaqueRenderDescription.Create(
            bounds,
            static (session, bounds) =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(bounds);
                session.UseResource(
                    s_imageSlot,
                    image => output.Canvas.Use(canvas => canvas.DrawImageScaled(image, bounds)));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(bounds),
            hitTest ?? RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.Custom(density, static (density, _) => density),
            RenderDeviceGridSensitivity.Insensitive,
            resources: [s_imageSlot.Bind(image)]));
    }
}
