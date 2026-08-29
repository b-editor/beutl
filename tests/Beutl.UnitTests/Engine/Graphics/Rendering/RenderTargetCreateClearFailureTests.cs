using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Moq;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Pins that <see cref="RenderTarget.Create"/> releases the target it already built when the initial
/// transparent clear fails.
/// </summary>
/// <remarks>
/// The clear submits to the device, so it is the step that fails while the device is lost — exactly when
/// callers treat the resulting null as a per-frame degrade and ask again on the next frame. Degrading to
/// null without releasing strands the Skia surface and the backend texture behind it until a finalizer
/// runs, so the leak compounds once per attempt at the worst possible moment.
///
/// The texture here deliberately does not record backend clears: <c>ITransparentClearableTexture</c> is
/// internal, so a texture from any out-of-tree <see cref="IGraphicsContext"/> cannot implement it, and
/// <see cref="RenderTarget.CreateSharedSurface"/> leaves such a texture uncleared. That is the shape which
/// reaches the render-target-level clear with a live backend texture attached.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class RenderTargetCreateClearFailureTests
{
    [Test]
    public void Create_ReleasesTheSurfaceAndTexture_WhenTheInitialClearThrows()
    {
        var texture = new PlainBackendTexture(4, 4, failOnSkiaRendering: true);
        RenderTarget? created = null;

        RunWithStandInContext(texture, () =>
        {
            created = RenderTarget.Create(4, 4);

            // The texture defers its release to the reclaim queue, which a context-wide sync discharges
            // rather than Dispose itself.
            GpuResourceReclaimQueue.DrainAfterContextSync();
        });

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.Null, "a failed clear must not hand back a target");
            Assert.That(
                texture.PrepareForSkiaRenderingCount,
                Is.EqualTo(1),
                "the fixture must actually reach the clear it fails, or the test proves nothing");
            Assert.That(
                texture.DisposeCount,
                Is.EqualTo(1),
                "the backend texture has no finalizer, so abandoning it here strands its image, view and device memory");
            Assert.That(
                texture.CreatedSurface!.Handle,
                Is.EqualTo(IntPtr.Zero),
                "the Skia surface the target owned has to go with it");
        });
    }

    /// <remarks>
    /// The negative control for the release above: a clear that succeeds must still hand the caller a
    /// usable target rather than one whose resources were released underneath it.
    /// </remarks>
    [Test]
    public void Create_KeepsTheSurfaceAndTexture_WhenTheInitialClearSucceeds()
    {
        var texture = new PlainBackendTexture(4, 4, failOnSkiaRendering: false);
        RenderTarget? created = null;
        int disposeCountWhileHeld = -1;

        RunWithStandInContext(texture, () =>
        {
            created = RenderTarget.Create(4, 4);
            disposeCountWhileHeld = texture.DisposeCount;

            created?.Dispose();
            GpuResourceReclaimQueue.DrainAfterContextSync();
        });

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.Not.Null, "a successful clear has to produce a target");
            Assert.That(texture.PrepareForSkiaRenderingCount, Is.EqualTo(1));
            Assert.That(
                disposeCountWhileHeld,
                Is.Zero,
                "a target handed to the caller must still own its texture");
            Assert.That(texture.DisposeCount, Is.EqualTo(1), "the caller's own Dispose releases it");
        });
    }

    /// <summary>
    /// Runs <paramref name="body"/> on the render thread with <paramref name="texture"/>'s context standing
    /// in for the shared one, then puts the live graphics state back.
    /// </summary>
    private static void RunWithStandInContext(PlainBackendTexture texture, Action body)
    {
        var context = new Mock<IGraphicsContext>();
        context
            .Setup(x => x.CreateTexture2D(texture.Width, texture.Height, TextureFormat.RGBA16Float))
            .Returns(texture);

        RenderThread.Dispatcher.Invoke(() =>
        {
            // Flush what the live context still owes before standing in for it: a resource left queued
            // would be destroyed against the stand-in instead.
            GpuResourceReclaimQueue.FlushAndDrain();
            InstalledGraphics live = GraphicsContextFactory.ExchangeInstalledGraphics(
                new InstalledGraphics(context.Object, null, null, FailedToInitialize: false));
            try
            {
                body();
            }
            finally
            {
                // A no-op once the body drained it; kept so a regression cannot leak the fixture's
                // texture into the rest of the run.
                GpuResourceReclaimQueue.DrainAfterContextSync();
                InstalledGraphics discarded = GraphicsContextFactory.ExchangeInstalledGraphics(live);
                discarded.SharedContext?.Dispose();
                discarded.VulkanInstance?.Dispose();
            }
        });
    }

    /// <summary>
    /// A backend texture that records no transparent clear of its own, so the render target clears it
    /// through Skia — optionally failing that hand-off the way a lost device does.
    /// </summary>
    private sealed class PlainBackendTexture(int width, int height, bool failOnSkiaRendering) : ITexture2D
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public TextureFormat Format => TextureFormat.RGBA16Float;

        public IntPtr NativeHandle => IntPtr.Zero;

        public IntPtr NativeViewHandle => IntPtr.Zero;

        public bool RequiresSkiaFlushForBackendInterop => false;

        public SKSurface? CreatedSurface { get; private set; }

        public int PrepareForSkiaRenderingCount { get; private set; }

        public int DisposeCount { get; private set; }

        public SKSurface CreateSkiaSurface()
        {
            CreatedSurface = SKSurface.Create(new SKImageInfo(
                Width, Height, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()));
            return CreatedSurface;
        }

        public void PrepareForSkiaRendering()
        {
            PrepareForSkiaRenderingCount++;
            if (failOnSkiaRendering)
                throw new InvalidOperationException("The device was lost before the clear could be submitted.");
        }

        public void Upload(ReadOnlySpan<byte> data) => throw new NotSupportedException();

        public byte[] DownloadPixels() => throw new NotSupportedException();

        public void PrepareForRender() => throw new NotSupportedException();

        public void PrepareForSampling() => throw new NotSupportedException();

        public void PrepareForSkiaSampling(bool requireCompletion) => throw new NotSupportedException();

        public void Dispose() => DisposeCount++;
    }
}
