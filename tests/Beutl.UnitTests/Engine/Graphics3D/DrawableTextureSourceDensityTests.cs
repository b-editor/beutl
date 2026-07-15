using Beutl.Composition;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics3D;

// DrawableTextureSource must rasterize at ceil(authorSize * surfaceDensity) for sharp high-density 3D surfaces.
// GPU-gated; skips when Vulkan is unavailable.
[TestFixture]
public class DrawableTextureSourceDensityTests
{
    private const int AuthorSize = 256;

    private static DrawableTextureSource.Resource MakeVectorTextureSource()
    {
        // A re-rasterizable vector Drawable: a filled rectangle the size of the texture.
        var rect = new RectShape();
        rect.Width.CurrentValue = AuthorSize;
        rect.Height.CurrentValue = AuthorSize;
        rect.Fill.CurrentValue = Brushes.White;

        var source = new DrawableTextureSource();
        source.Drawable.CurrentValue = rect;
        source.TextureWidth.CurrentValue = AuthorSize;
        source.TextureHeight.CurrentValue = AuthorSize;
        return (DrawableTextureSource.Resource)source.ToResource(CompositionContext.Default);
    }

    [Test]
    public void GetTexture_RejectsUnknownRenderPoliciesBeforeUsingTheGraphicsContext()
    {
        using DrawableTextureSource.Resource source = MakeVectorTextureSource();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => source.GetTexture(
                null!, (RenderIntent)42, RenderPullPurpose.Frame));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.GetTexture(
                null!, RenderIntent.Preview, (RenderPullPurpose)42));
        });
    }

    [Test]
    public void GetTexture_AuxiliaryDensityDoesNotReplaceFrameCache()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using DrawableTextureSource.Resource source = MakeVectorTextureSource();

            ITexture2D? frameBefore = source.GetTexture(
                context, RenderIntent.Preview, RenderPullPurpose.Frame, 1f);
            ITexture2D? auxiliary = source.GetTexture(
                context, RenderIntent.Preview, RenderPullPurpose.Auxiliary, 2f);
            ITexture2D? frameAfter = source.GetTexture(
                context, RenderIntent.Preview, RenderPullPurpose.Frame, 1f);

            Assert.Multiple(() =>
            {
                Assert.That(frameBefore, Is.Not.Null);
                Assert.That(auxiliary, Is.Not.Null);
                Assert.That(auxiliary!.Width, Is.EqualTo(AuthorSize * 2));
                Assert.That(frameAfter, Is.SameAs(frameBefore),
                    "an auxiliary texture pull must not replace the retained frame texture");
            });
        });
    }

    [Test]
    public void GetTexture_VectorDrawable_RasterizesAtSurfaceDensity()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using DrawableTextureSource.Resource source = MakeVectorTextureSource();

            // renderScale 1: device == logical.
            ITexture2D? at1 = source.GetTexture(
                context, RenderIntent.Delivery, RenderPullPurpose.Frame, 1f);
            Assert.That(at1, Is.Not.Null, "GetTexture(ctx, 1f) returned null on a GPU-available environment");
            int width1 = at1!.Width;
            int height1 = at1.Height;
            Assert.That(width1, Is.EqualTo(AuthorSize), "renderScale 1 must stay at the authored size");
            Assert.That(height1, Is.EqualTo(AuthorSize));

            // renderScale 2: device == 512, cache rebuilds.
            ITexture2D? at2 = source.GetTexture(
                context, RenderIntent.Delivery, RenderPullPurpose.Frame, 2f);
            Assert.That(at2, Is.Not.Null, "GetTexture(ctx, 2f) returned null on a GPU-available environment");
            int width2 = at2!.Width;
            int height2 = at2.Height;
            Assert.That(width2, Is.EqualTo(AuthorSize * 2),
                "renderScale 2 must rasterize the vector Drawable at ceil(authorSize × 2) device px (512), not 256");
            Assert.That(height2, Is.EqualTo(AuthorSize * 2));

            // The whole point of the fix: the device texture grows with the surface density.
            Assert.That(width2, Is.EqualTo(width1 * 2));
            Assert.That(height2, Is.EqualTo(height1 * 2));
        });
    }
}
