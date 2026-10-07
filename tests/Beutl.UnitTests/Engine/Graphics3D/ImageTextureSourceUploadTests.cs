using Beutl.Composition;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.Media.Source;
using Moq;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics3D;

// ImageTextureSource uploads colors decoded to linear and data maps with their stored values.
[TestFixture]
public class ImageTextureSourceUploadTests
{
    [Test]
    public void ColorUpload_KeepsEveryDarkSrgbLevelDistinct()
    {
        using var source = Gradient();

        using Bitmap upload = ImageTextureSource.Resource.CreateUploadBitmap(source, TextureContentKind.Color);

        Assert.That(upload.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        Span<Half> pixels = upload.GetPixelSpan<Half>();
        using (Assert.EnterMultipleScope())
        {
            // sRGB 128 decodes to about 0.216 in linear light.
            Assert.That((float)pixels[128 * 4], Is.EqualTo(0.216f).Within(0.002f));
            // 8-bit linear storage collapses sRGB 0-12 to 0 and 1, which bands the shadows.
            for (int x = 1; x < 256; x++)
                Assert.That((float)pixels[x * 4], Is.GreaterThan((float)pixels[(x - 1) * 4]), $"sRGB {x}");
        }
    }

    [Test]
    public void DataUpload_KeepsTheStoredValues()
    {
        using var source = Gradient();

        using Bitmap upload = ImageTextureSource.Resource.CreateUploadBitmap(source, TextureContentKind.Data);

        Assert.That(upload.ColorType, Is.EqualTo(BitmapColorType.Bgra8888));
        Assert.That(upload.GetPixelSpan<byte>().ToArray(), Is.EqualTo(source.GetPixelSpan<byte>().ToArray()));
    }

    [Test]
    public void DataUpload_KeepsHalfFloatPrecision()
    {
        using var gradient = Gradient();
        using var source = gradient.Convert(BitmapColorType.RgbaF16, colorSpace: BitmapColorSpace.LinearSrgb);

        using Bitmap upload = ImageTextureSource.Resource.CreateUploadBitmap(source, TextureContentKind.Data);

        Assert.That(upload.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        Assert.That(upload.GetPixelSpan<Half>().ToArray(), Is.EqualTo(source.GetPixelSpan<Half>().ToArray()));
    }

    [Test]
    public void DataUpload_KeepsSrgbStorageIn8Bits()
    {
        using var source = Gradient(BitmapColorType.Srgba8888);

        using Bitmap upload = ImageTextureSource.Resource.CreateUploadBitmap(source, TextureContentKind.Data);

        Assert.That(upload.ColorType, Is.EqualTo(BitmapColorType.Bgra8888));
        // Every channel holds x, so the RGBA to BGRA swap leaves the bytes as stored.
        Assert.That(upload.GetPixelSpan<byte>().ToArray(), Is.EqualTo(source.GetPixelSpan<byte>().ToArray()));
    }

    [Test]
    public void GetTexture_CachesOneUploadPerContentKind()
    {
        var (definition, context, created) = CreateSource(nameof(GetTexture_CachesOneUploadPerContentKind));
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);

        ITexture2D? color = resource.GetTexture(context.Object);
        ITexture2D? data = resource.GetTexture(context.Object, 1f, TextureContentKind.Data);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(color!.Format, Is.EqualTo(TextureFormat.RGBA16Float));
            Assert.That(data!.Format, Is.EqualTo(TextureFormat.BGRA8Unorm));
            Assert.That(resource.GetTexture(context.Object, 1f, TextureContentKind.Color), Is.SameAs(color));
            Assert.That(resource.GetTexture(context.Object, 1f, TextureContentKind.Data), Is.SameAs(data));
            Assert.That(created, Has.Count.EqualTo(2), "Alternating kinds must not re-upload either texture.");
        }
    }

    [Test]
    public void GetTexture_ReplacesBothKindsWhenTheSourceChanges()
    {
        var (definition, context, created) = CreateSource(nameof(GetTexture_ReplacesBothKindsWhenTheSourceChanges));
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);
        resource.GetTexture(context.Object);
        resource.GetTexture(context.Object, 1f, TextureContentKind.Data);

        definition.Source.CurrentValue = Image(nameof(GetTexture_ReplacesBothKindsWhenTheSourceChanges) + "-next");
        bool updateOnly = false;
        resource.Update(definition, CompositionContext.Default, ref updateOnly);
        ITexture2D? color = resource.GetTexture(context.Object);
        ITexture2D? data = resource.GetTexture(context.Object, 1f, TextureContentKind.Data);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Has.Count.EqualTo(4));
            Assert.That(color, Is.SameAs(created[2]));
            Assert.That(data, Is.SameAs(created[3]));
            Assert.That(created[0].Disposed && created[1].Disposed, Is.True, "Stale uploads must be released.");
        }
    }

    [TestCase(TextureContentKind.Color)]
    [TestCase(TextureContentKind.Data)]
    public void GetTexture_ReplacesTheCachedUploadWhenTheContextChanges(TextureContentKind contentKind)
    {
        var (definition, firstContext, firstCreated) = CreateSource(
            $"{nameof(GetTexture_ReplacesTheCachedUploadWhenTheContextChanges)}-{contentKind}");
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);
        ITexture2D? first = resource.GetTexture(firstContext.Object, 1f, contentKind);
        var secondCreated = new List<FakeTexture>();
        var secondContext = new Mock<IGraphicsContext>();
        secondContext.Setup(c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns((int width, int height, TextureFormat format) =>
            {
                var texture = new FakeTexture(width, height, format, () => false);
                secondCreated.Add(texture);
                return texture;
            });

        ITexture2D? second = resource.GetTexture(secondContext.Object, 1f, contentKind);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.SameAs(firstCreated.Single()), "the first context owns the initial upload");
            Assert.That(secondCreated, Has.Count.EqualTo(1), "a different device must receive its own upload");
            Assert.That(second, Is.SameAs(secondCreated.SingleOrDefault()));
            Assert.That(firstCreated.Single().Disposed, Is.True, "the replaced upload must be released");
            Assert.That(resource.GetTexture(secondContext.Object, 1f, contentKind), Is.SameAs(second));
            Assert.That(secondCreated, Has.Count.EqualTo(1), "subsequent draws on that device reuse its upload");
        }
    }

    [Test]
    public void GetTexture_ReleasesBothKindsWhenTheSourceIsCleared()
    {
        var (definition, context, created) = CreateSource(nameof(GetTexture_ReleasesBothKindsWhenTheSourceIsCleared));
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);
        resource.GetTexture(context.Object);
        resource.GetTexture(context.Object, 1f, TextureContentKind.Data);

        definition.Source.CurrentValue = null;
        bool updateOnly = false;
        resource.Update(definition, CompositionContext.Default, ref updateOnly);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resource.GetTexture(context.Object, 1f, TextureContentKind.Data), Is.Null);
            Assert.That(created.All(texture => texture.Disposed), Is.True, "Both uploads must be released.");
        }
    }

    [Test]
    public void GetTexture_ReleasesTheTextureWhenTheUploadFails()
    {
        bool failUploads = true;
        var (definition, context, created) = CreateSource(
            nameof(GetTexture_ReleasesTheTextureWhenTheUploadFails), () => failUploads);
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);

        Assert.That(() => resource.GetTexture(context.Object), Throws.InvalidOperationException);
        failUploads = false;

        Assert.That(created.Single().Disposed, Is.True);
        Assert.That(resource.GetTexture(context.Object), Is.SameAs(created[1]), "The next draw must retry the upload.");
    }

    private static (ImageTextureSource, Mock<IGraphicsContext>, List<FakeTexture>) CreateSource(
        string name, Func<bool>? failUploads = null)
    {
        var created = new List<FakeTexture>();
        var context = new Mock<IGraphicsContext>();
        context.Setup(c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns((int width, int height, TextureFormat format) =>
            {
                var texture = new FakeTexture(width, height, format, failUploads ?? (() => false));
                created.Add(texture);
                return texture;
            });

        var definition = new ImageTextureSource();
        definition.Source.CurrentValue = Image(name);
        return (definition, context, created);
    }

    private static ImageSource Image(string name)
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{nameof(ImageTextureSourceUploadTests)}-{name}.png");
        using (var bitmap = new SKBitmap(2, 2))
        {
            bitmap.Erase(new SKColor(128, 128, 255));
            using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, encoded.ToArray());
        }

        var image = new ImageSource();
        image.ReadFrom(new Uri(path));
        return image;
    }

    // An opaque, untagged (so sRGB) 8-bit bitmap whose x-th pixel stores x in every color channel.
    private static Bitmap Gradient(BitmapColorType colorType = BitmapColorType.Bgra8888)
    {
        var bitmap = new Bitmap(256, 1, colorType, BitmapAlphaType.Premul);
        Span<byte> bytes = bitmap.GetPixelSpan<byte>();
        for (int x = 0; x < 256; x++)
        {
            bytes[x * 4] = (byte)x;
            bytes[x * 4 + 1] = (byte)x;
            bytes[x * 4 + 2] = (byte)x;
            bytes[x * 4 + 3] = 255;
        }

        return bitmap;
    }

    // Moq cannot intercept Upload, whose span parameter cannot be boxed.
    private sealed class FakeTexture(int width, int height, TextureFormat format, Func<bool> failUploads) : ITexture2D
    {
        public bool Disposed { get; private set; }

        public int Width => width;

        public int Height => height;

        public TextureFormat Format => format;

        public IntPtr NativeHandle => IntPtr.Zero;

        public IntPtr NativeViewHandle => IntPtr.Zero;

        public bool RequiresSkiaFlushForBackendInterop => false;

        public void Upload(ReadOnlySpan<byte> data)
        {
            if (failUploads())
                throw new InvalidOperationException("Upload failed.");
        }

        public byte[] DownloadPixels() => throw new NotSupportedException();

        public SKSurface CreateSkiaSurface(SKColorSpace colorSpace) => throw new NotSupportedException();

        public void PrepareForRender()
        {
        }

        public void PrepareForSampling()
        {
        }

        public void PrepareForSkiaRendering()
        {
        }

        public void PrepareForSkiaSampling(bool requireCompletion)
        {
        }

        public void Dispose() => Disposed = true;
    }
}
