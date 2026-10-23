using Beutl.Composition;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Failure;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public class DrawableTextureSourceDensityTests
{
    private const int AuthorSize = 256;

    private static DrawableTextureSource.Resource MakeVectorTextureSource()
    {
        // A re-rasterizable vector Drawable: a filled rectangle the size of the texture.
        var rect = new RectShape();
        rect.Width.Value = AuthorSize;
        rect.Height.Value = AuthorSize;
        rect.Fill.Value = Brushes.White;

        var source = new DrawableTextureSource();
        source.Drawable.Value = rect;
        source.TextureWidth.Value = AuthorSize;
        source.TextureHeight.Value = AuthorSize;
        return (DrawableTextureSource.Resource)source.ToResource(CompositionContext.Default);
    }

    [Test]
    public void GetTexture_EmptyDrawableReturnsNullInsideDeferredCallback()
    {
        var sourceDefinition = new DrawableTextureSource();
        using var source =
            (DrawableTextureSource.Resource)sourceDefinition.ToResource(CompositionContext.Default);
        using RenderExecutionCallbackGuard.Scope callback = RenderExecutionCallbackGuard.Enter();

        ITexture2D? texture = null;
        Assert.That(
            () => texture = source.GetTexture(Mock.Of<IGraphicsContext>()),
            Throws.Nothing);
        Assert.That(texture, Is.Null);
    }

    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(float.NaN)]
    public void GetTexture_UnboundDrawableReturnsNullExceptInsideDeferredCallback(float density)
    {
        using DrawableTextureSource.Resource source = MakeVectorTextureSource();
        IGraphicsContext context = Mock.Of<IGraphicsContext>();

        Assert.That(source.GetTexture(context, density), Is.Null);
        using RenderExecutionCallbackGuard.Scope callback = RenderExecutionCallbackGuard.Enter();
        Assert.Throws<InvalidOperationException>(() => source.GetTexture(context, density));
    }

    [Test]
    public void GetTexture_NestedOversizedDrawableUsesTheClampedRecordingDensity()
    {
        var sourceDefinition = new DrawableTextureSource();
        sourceDefinition.Drawable.Value = new RectShape();
        sourceDefinition.TextureWidth.Value = 8192;
        sourceDefinition.TextureHeight.Value = 1;
        using var source =
            (DrawableTextureSource.Resource)sourceDefinition.ToResource(CompositionContext.Default);
        using var registry = new RenderTargetPool(new CpuTargetFactory());
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);
        RenderTargetLease lease = session.Acquire(
            new PixelSize(BufferDimensionBudget.EngineCeiling.MaxDimension, 2));
        using var binding = new NestedRenderTargetBinding();
        binding.Stage(
            lease,
            source.TextureDomain,
            density: 2);
        binding.PrepareForSampling();
        ITexture2D? texture = null;

        Assert.That(
            () => NestedRenderTargetBindingScope.Use(
                source,
                binding,
                () => texture = source.GetTexture(Mock.Of<IGraphicsContext>(), surfaceDensity: 4)),
            Throws.Nothing);
        Assert.That(texture, Is.Null,
            "A CPU-backed nested target has no GPU texture, but its resolved density must still match.");
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => FailureTestSupport.CreateCpuTarget(
                allocation.DeviceSize.Width,
                allocation.DeviceSize.Height);
    }
}
