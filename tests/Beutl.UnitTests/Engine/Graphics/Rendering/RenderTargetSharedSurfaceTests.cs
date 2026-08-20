using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Moq;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Pins that a failed shared-surface initialization releases the backend texture it already created.
/// </summary>
/// <remarks>
/// The backend texture has no finalizer and nothing registers it anywhere, so a texture that escapes
/// before it reaches a <see cref="RenderTarget"/> strands its image, view and device memory for the
/// life of the process — and callers treat the resulting null as a per-frame degrade, so the leak
/// compounds instead of happening once.
/// </remarks>
public sealed class RenderTargetSharedSurfaceTests
{
    [Test]
    public void CreateSharedSurface_ReleasesTextureAndSurface_WhenTheTransparentClearThrows()
    {
        var texture = new FailingClearTexture(4, 4, failOnSurfaceCreation: false);
        var context = new Mock<IGraphicsContext>();
        context.Setup(x => x.CreateTexture2D(4, 4, TextureFormat.RGBA16Float)).Returns(texture);

        Assert.Throws<InvalidOperationException>(
            () => RenderTarget.CreateSharedSurface(context.Object, 4, 4, out _));

        Assert.That(texture.DisposeCount, Is.EqualTo(1));
        Assert.That(texture.CreatedSurface!.Handle, Is.EqualTo(IntPtr.Zero));
    }

    [Test]
    public void CreateSharedSurface_ReleasesTheTexture_WhenSurfaceCreationThrows()
    {
        var texture = new FailingClearTexture(4, 4, failOnSurfaceCreation: true);
        var context = new Mock<IGraphicsContext>();
        context.Setup(x => x.CreateTexture2D(4, 4, TextureFormat.RGBA16Float)).Returns(texture);

        Assert.Throws<InvalidOperationException>(
            () => RenderTarget.CreateSharedSurface(context.Object, 4, 4, out _));

        Assert.That(texture.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void CreateSharedSurface_ClearsAndKeepsTheTexture_WhenInitializationSucceeds()
    {
        var texture = new ClearableRasterTexture(4, 4);
        var context = new Mock<IGraphicsContext>();
        context.Setup(x => x.CreateTexture2D(4, 4, TextureFormat.RGBA16Float)).Returns(texture);

        using SKSurface surface = RenderTarget.CreateSharedSurface(context.Object, 4, 4, out ITexture2D created);

        Assert.That(created, Is.SameAs(texture));
        Assert.That(texture.ClearCount, Is.EqualTo(1));
        Assert.That(texture.DisposeCount, Is.Zero);
        Assert.That(surface.Handle, Is.Not.EqualTo(IntPtr.Zero));
    }

    private abstract class RasterBackedTexture(int width, int height)
        : ITexture2D, ITransparentClearableTexture
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public TextureFormat Format => TextureFormat.RGBA16Float;

        public IntPtr NativeHandle => IntPtr.Zero;

        public IntPtr NativeViewHandle => IntPtr.Zero;

        public bool RequiresSkiaFlushForBackendInterop => false;

        public abstract bool HasTransparentContents { get; }

        public SKSurface? CreatedSurface { get; private set; }

        public int DisposeCount { get; private set; }

        protected SKSurface CreateRasterSurface()
        {
            CreatedSurface = SKSurface.Create(new SKImageInfo(Width, Height));
            return CreatedSurface;
        }

        public void Upload(ReadOnlySpan<byte> data) => throw new NotSupportedException();

        public byte[] DownloadPixels() => throw new NotSupportedException();

        public abstract SKSurface CreateSkiaSurface();

        public void PrepareForRender() => throw new NotSupportedException();

        public void PrepareForSampling() => throw new NotSupportedException();

        public void PrepareForSkiaRendering() => throw new NotSupportedException();

        public void PrepareForSkiaSampling(bool requireCompletion) => throw new NotSupportedException();

        public abstract void ClearToTransparent();

        public void Dispose() => DisposeCount++;
    }

    private sealed class FailingClearTexture(int width, int height, bool failOnSurfaceCreation)
        : RasterBackedTexture(width, height)
    {
        public override bool HasTransparentContents => false;

        public override SKSurface CreateSkiaSurface()
            => failOnSurfaceCreation
                ? throw new InvalidOperationException("surface creation failed")
                : CreateRasterSurface();

        public override void ClearToTransparent()
            => throw new InvalidOperationException("clear failed");
    }

    private sealed class ClearableRasterTexture(int width, int height) : RasterBackedTexture(width, height)
    {
        public override bool HasTransparentContents => ClearCount > 0;

        public int ClearCount { get; private set; }

        public override SKSurface CreateSkiaSurface() => CreateRasterSurface();

        public override void ClearToTransparent() => ClearCount++;
    }
}
