using Beutl.Composition;
using Beutl.Graphics;
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

        return MakeTextureSource(rect);
    }

    private static DrawableTextureSource.Resource MakeTextureSource(Drawable drawable)
    {
        var source = new DrawableTextureSource();
        source.Drawable.CurrentValue = drawable;
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
    public void GetTexture_AuxiliaryPullAtFrameDensity_ReusesFrameTexture()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using DrawableTextureSource.Resource source = MakeVectorTextureSource();

            ITexture2D? frame = source.GetTexture(
                context, RenderIntent.Preview, RenderPullPurpose.Frame, 1f);
            ITexture2D? auxiliary = source.GetTexture(
                context, RenderIntent.Preview, RenderPullPurpose.Auxiliary, 1f);

            Assert.That(auxiliary, Is.SameAs(frame),
                "A compatible auxiliary pull may read the retained frame texture without mutating it.");
        });
    }

    [Test]
    public void GetTexture_FrameAfterAuxiliaryPull_UsesIsolatedTexture()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using DrawableTextureSource.Resource source = MakeVectorTextureSource();

            ITexture2D? auxiliary = source.GetTexture(
                context, RenderIntent.Preview, RenderPullPurpose.Auxiliary, 1f);
            ITexture2D? frame = source.GetTexture(
                context, RenderIntent.Preview, RenderPullPurpose.Frame, 1f);

            Assert.That(frame, Is.Not.SameAs(auxiliary),
                "A frame must not adopt a texture rendered with auxiliary policy.");
        });
    }

    [Test]
    public void GetTexture_PolicyChanges_ReprocessPolicySensitiveDrawable()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = new PolicySensitiveTextureDrawable();
            using DrawableTextureSource.Resource source = MakeTextureSource(drawable);

            _ = source.GetTexture(context, RenderIntent.Preview, RenderPullPurpose.Auxiliary, 1f);
            _ = source.GetTexture(context, RenderIntent.Preview, RenderPullPurpose.Frame, 1f);
            _ = source.GetTexture(context, RenderIntent.Delivery, RenderPullPurpose.Frame, 1f);

            Assert.That(drawable.Observations, Is.EqualTo(new[]
            {
                (RenderIntent.Preview, RenderPullPurpose.Auxiliary),
                (RenderIntent.Preview, RenderPullPurpose.Frame),
                (RenderIntent.Delivery, RenderPullPurpose.Frame),
            }));
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

internal sealed partial class PolicySensitiveTextureDrawable : Drawable
{
    private const int TextureSize = 256;

    public List<(RenderIntent, RenderPullPurpose)> Observations { get; } = [];

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => context.DrawNode(new PolicySensitiveTextureNode(Observations));

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
        => new(TextureSize, TextureSize);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class PolicySensitiveTextureNode(List<(RenderIntent, RenderPullPurpose)> observations) : RenderNode
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        observations.Add((context.RenderIntent, context.PullPurpose));
        return [RenderNodeOperation.CreateLambda(new Rect(0, 0, 1, 1), static _ => { })];
    }
}
