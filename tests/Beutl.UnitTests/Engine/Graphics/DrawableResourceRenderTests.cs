using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

/// <summary>
/// Rendering a <c>Drawable.Resource</c> dispatches through
/// <c>drawable.GetOriginal().Render(context, drawable)</c>, so a drawable — unlike a geometry — is still
/// authored on its engine object rather than on its resource.
/// </summary>
[TestFixture]
public sealed class DrawableResourceRenderTests
{
    [Test]
    public void RenderingAnAttachedDrawableResource_RecordsItsFragment()
    {
        var shape = new RectShape
        {
            Width = { CurrentValue = 40 },
            Height = { CurrentValue = 30 },
            Fill = { CurrentValue = Brushes.White },
        };
        using Drawable.Resource attached = shape.ToResource(CompositionContext.Default);
        using var node = new DrawableRenderNode(attached);
        using (var context = new GraphicsContext2D(node, new Size(64, 64)))
        {
            attached.GetOriginal().Render(context, attached);
        }

        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });
        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.That(rasterization.Bitmap, Is.Not.Null);
    }
}
