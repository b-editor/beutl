using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Moq;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public class ImageTextureSourceFailureTests
{
    [OneTimeSetUp]
    public void RegisterTestDecoder()
    {
        TestMediaHelper.RegisterTestDecoder();
    }

    [TestCase(RenderIntent.Preview, false)]
    [TestCase(RenderIntent.Delivery, true)]
    public void TextureAllocationFailure_FollowsRenderIntent(RenderIntent intent, bool shouldThrow)
    {
        using ImageTextureSource.Resource resource = CreateResource();
        var graphicsContext = new Mock<IGraphicsContext>();
        graphicsContext
            .Setup(x => x.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Throws(new InvalidOperationException("allocation failed"));

        if (shouldThrow)
        {
            Assert.Throws<InvalidOperationException>(() =>
                resource.GetTexture(graphicsContext.Object, intent, RenderPullPurpose.Frame));
        }
        else
        {
            Assert.That(resource.GetTexture(graphicsContext.Object, intent, RenderPullPurpose.Frame), Is.Null,
                "preview may drop a texture whose allocation failed");
        }
    }

    [TestCase(RenderIntent.Preview, false)]
    [TestCase(RenderIntent.Delivery, true)]
    public void TextureUploadFailure_FollowsRenderIntentAndDisposesPartialTexture(
        RenderIntent intent,
        bool shouldThrow)
    {
        using ImageTextureSource.Resource resource = CreateResource();
        var partialTexture = new TestTexture(8, 8, throwOnUpload: true);
        var graphicsContext = new Mock<IGraphicsContext>();
        graphicsContext
            .Setup(x => x.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns(partialTexture);

        if (shouldThrow)
        {
            Assert.Throws<InvalidOperationException>(() =>
                resource.GetTexture(graphicsContext.Object, intent, RenderPullPurpose.Frame));
        }
        else
        {
            Assert.That(resource.GetTexture(graphicsContext.Object, intent, RenderPullPurpose.Frame), Is.Null,
                "preview may drop a texture whose upload failed");
        }

        Assert.That(partialTexture.IsDisposed, Is.True,
            "an upload failure must release the texture allocated before the failure");
    }

    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void TextureUploadFailure_WhenCleanupThrows_PreservesIntentContract(RenderIntent intent)
    {
        using ImageTextureSource.Resource resource = CreateResource();
        var partialTexture = new TestTexture(8, 8, throwOnUpload: true, throwOnDispose: true);
        var graphicsContext = new Mock<IGraphicsContext>();
        graphicsContext
            .Setup(x => x.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns(partialTexture);

        if (intent == RenderIntent.Delivery)
        {
            InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() =>
                resource.GetTexture(graphicsContext.Object, intent, RenderPullPurpose.Frame));
            Assert.That(exception!.Message, Is.EqualTo("upload failed"),
                "cleanup failure must not replace the delivery upload failure");
        }
        else
        {
            Assert.That(resource.GetTexture(graphicsContext.Object, intent, RenderPullPurpose.Frame), Is.Null,
                "preview must still degrade to missing content after best-effort cleanup fails");
        }

        Assert.That(partialTexture.IsDisposed, Is.True,
            "cleanup must still be attempted before its failure is suppressed");
    }

    private static ImageTextureSource.Resource CreateResource()
    {
        Uri uri = TestMediaHelper.CreateTestImageUri(8, 8, Colors.White);
        var image = new ImageSource();
        image.ReadFrom(uri);
        var source = new ImageTextureSource();
        source.Source.CurrentValue = image;
        return (ImageTextureSource.Resource)source.ToResource(CompositionContext.Default);
    }

    private sealed class TestTexture(
        int width,
        int height,
        bool throwOnUpload,
        bool throwOnDispose = false) : ITexture2D
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public TextureFormat Format => TextureFormat.BGRA8Unorm;

        public nint NativeHandle => nint.Zero;

        public nint NativeViewHandle => nint.Zero;

        public bool IsDisposed { get; private set; }

        public void Upload(ReadOnlySpan<byte> data)
        {
            if (throwOnUpload)
                throw new InvalidOperationException("upload failed");
        }

        public byte[] DownloadPixels() => throw new NotSupportedException();

        public SKSurface CreateSkiaSurface() => throw new NotSupportedException();

        public void PrepareForRender() => throw new NotSupportedException();

        public void PrepareForSampling() => throw new NotSupportedException();

        public void Dispose()
        {
            IsDisposed = true;
            if (throwOnDispose)
                throw new InvalidOperationException("cleanup failed");
        }
    }
}
