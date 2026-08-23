using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Moq;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

public sealed class NativeFilterScratchTextureTests
{
    [Test]
    public void AcquireNativeScratchTexture_ClearsOwnedTextureBeforeReturningIt()
    {
        var texture = new ClearableTexture(8, 6);

        var graphicsContext = new Mock<IGraphicsContext>();
        graphicsContext
            .Setup(x => x.CreateTexture2D(8, 6, TextureFormat.RGBA16Float))
            .Returns(texture);
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary);

        using NativeFilterTextureLease lease = context.AcquireNativeScratchTexture(
            graphicsContext.Object,
            8,
            6);

        Assert.That(lease.Texture, Is.SameAs(texture));
        Assert.That(texture.ClearCount, Is.EqualTo(1));
    }

    [Test]
    public void AcquireNativeScratchTexture_RejectsTextureWithoutOrderedClear()
    {
        var texture = new Mock<ITexture2D>();
        texture.SetupGet(x => x.Width).Returns(8);
        texture.SetupGet(x => x.Height).Returns(6);
        texture.SetupGet(x => x.Format).Returns(TextureFormat.RGBA16Float);
        var graphicsContext = new Mock<IGraphicsContext>();
        graphicsContext
            .Setup(x => x.CreateTexture2D(8, 6, TextureFormat.RGBA16Float))
            .Returns(texture.Object);
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary);

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() =>
            context.AcquireNativeScratchTexture(graphicsContext.Object, 8, 6));

        Assert.That(exception!.Message, Does.Contain("ordered transparent clear"));
        texture.Verify(x => x.Dispose(), Times.Once);
    }

    private sealed class ClearableTexture(int width, int height)
        : ITexture2D, ITransparentClearableTexture
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public TextureFormat Format => TextureFormat.RGBA16Float;

        public IntPtr NativeHandle => IntPtr.Zero;

        public IntPtr NativeViewHandle => IntPtr.Zero;

        public bool RequiresSkiaFlushForBackendInterop => false;

        public bool HasTransparentContents => ClearCount > 0;

        public int ClearCount { get; private set; }

        public void Upload(ReadOnlySpan<byte> data) => throw new NotSupportedException();

        public byte[] DownloadPixels() => throw new NotSupportedException();

        public SKSurface CreateSkiaSurface() => throw new NotSupportedException();

        public void PrepareForRender() => throw new NotSupportedException();

        public void PrepareForSampling() => throw new NotSupportedException();

        public void PrepareForSkiaRendering() => throw new NotSupportedException();

        public void PrepareForSkiaSampling(bool requireCompletion) => throw new NotSupportedException();

        public void ClearToTransparent() => ClearCount++;

        public void MarkContentsTransparent() => MarkedTransparentCount++;

        public int MarkedTransparentCount { get; private set; }

        public void Dispose()
        {
        }
    }
}
