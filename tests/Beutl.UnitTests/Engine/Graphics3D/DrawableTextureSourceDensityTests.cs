using Beutl.Composition;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics3D;

// feature 003 (DrawableTextureSource density fix): a DrawableTextureSource wrapping a re-rasterizable vector
// Drawable must rasterize at ceil(authorSize × surfaceDensity) so a crisp vector label/logo stays sharp on a
// supersampled / high-density 3D surface instead of being frozen at the authored pixel count and GPU-magnified.
// GetTexture(ctx, renderScale) keys the cached render target on the DEVICE size, so:
//   - renderScale 1  → device == logical (byte-identity baseline: 256 stays 256)
//   - renderScale 2  → device == ceil(authorSize × 2) (512)
// GPU-gated via the shared VulkanTestEnvironment: it allocates a real RenderTarget on the render thread, so it
// SKIPS (Assert.Ignore) when Vulkan / MoltenVK is unavailable rather than failing for lack of a GPU.
[TestFixture]
public class DrawableTextureSourceDensityTests
{
    private const int AuthorSize = 256;

    private static DrawableTextureSource.Resource MakeVectorTextureSource()
    {
        // A vector Drawable (re-rasterizable): a filled rectangle the size of the texture.
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
    public void GetTexture_VectorDrawable_RasterizesAtSurfaceDensity()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using DrawableTextureSource.Resource source = MakeVectorTextureSource();

            // renderScale 1: device == logical (byte-identity baseline).
            ITexture2D? at1 = source.GetTexture(context, 1f);
            Assert.That(at1, Is.Not.Null, "GetTexture(ctx, 1f) returned null on a GPU-available environment");
            int width1 = at1!.Width;
            int height1 = at1.Height;
            Assert.That(width1, Is.EqualTo(AuthorSize), "renderScale 1 must stay at the authored size (byte-identity)");
            Assert.That(height1, Is.EqualTo(AuthorSize));

            // renderScale 2: device == ceil(256 × 2) = 512. The cached target is keyed on the device size, so
            // this rebuilds at the larger size rather than reusing the 256 target.
            ITexture2D? at2 = source.GetTexture(context, 2f);
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
