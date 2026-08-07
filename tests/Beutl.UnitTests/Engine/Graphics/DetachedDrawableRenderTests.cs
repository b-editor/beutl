using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

/// <summary>
/// Rendering a <c>Drawable.Resource</c> dispatches through
/// <c>drawable.RequireOriginal().Render(context, drawable)</c>. A drawable is not authored on its resource the
/// way a geometry now is, so a detached one still cannot render; what the change buys is that it says so by
/// name instead of throwing <see cref="NullReferenceException"/>.
/// </summary>
/// <remarks>
/// This fixture covers the shared mechanism, not the set of call sites. An earlier version of it listed the
/// sites and asserted the list was complete; the list was written from a <c>GetOriginal().Member</c> search and
/// missed <c>GraphicsContext2D.DrawDrawable</c>, which spells the same dereference across two statements.
/// <c>EngineObjectOriginalAccessCensusTests</c> is what actually holds that line, syntactically and for every
/// call site under <c>src/</c>.
/// </summary>
[TestFixture]
public sealed class DetachedDrawableRenderTests
{
    [Test]
    public void RenderingADetachedDrawableResource_ThrowsAnInvalidOperationNamingTheResourceType()
    {
        using var detached = new RectShape.Resource { Width = 40, Height = 30 };
        using var node = new DrawableRenderNode(detached);
        using var context = new GraphicsContext2D(node, new Size(64, 64));

        var exception = Assert.Throws<InvalidOperationException>(
            () => detached.RequireOriginal().Render(context, detached));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain(nameof(RectShape)));
            Assert.That(exception.Message, Does.Contain(nameof(Beutl.Engine.EngineObject.ToResource)));
        }
    }

    [Test]
    public void RenderingAnAttachedDrawableResource_StillRecordsItsFragment()
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
            attached.RequireOriginal().Render(context, attached);
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
