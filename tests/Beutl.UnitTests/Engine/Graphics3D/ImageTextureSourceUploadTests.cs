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
    public void StraightColorUpload_KeepsTheColorsUnderAlphaZero()
    {
        using var source = new Bitmap(new SKBitmap(
            new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul)));
        new byte[] { 255, 128, 0, 0, 255, 128, 0, 255 }.CopyTo(source.GetPixelSpan<byte>());

        using Bitmap upload = ImageTextureSource.Resource.CreateStraightColorUploadBitmap(source);

        Assert.That(upload.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        Assert.That(upload.AlphaType, Is.EqualTo(BitmapAlphaType.Unpremul));
        Half[] pixels = upload.GetPixelSpan<Half>().ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((float)pixels[0], Is.EqualTo(1f).Within(0.002f));
            Assert.That((float)pixels[1], Is.EqualTo(0.216f).Within(0.002f));
            Assert.That((float)pixels[3], Is.Zero);
            Assert.That((float)pixels[5], Is.EqualTo(0.216f).Within(0.002f));
            Assert.That((float)pixels[7], Is.EqualTo(1f));
        }
    }

    [Test]
    public void ReadingAFileAgain_RecoversTheColorsThatDecodingLoses()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("beutl-straight-colors-");
        try
        {
            string path = Path.Combine(directory.FullName, "map.png");
            using (var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul)))
            {
                new byte[] { 255, 128, 0, 0 }.CopyTo(bitmap.GetPixelSpan());
                using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                using FileStream stream = File.Create(path);
                data.SaveTo(stream);
            }

            using LoadedImage image = LoadedImage.Open(new Uri(path));
            using Bitmap? straight = image.DecodeUnpremultiplied();

            Assert.That(image.Bitmap!.SKBitmap.GetPixel(0, 0).Red, Is.Zero, "Decoding multiplies by alpha.");
            Assert.That(straight!.SKBitmap.GetPixel(0, 0), Is.EqualTo(new SKColor(255, 128, 0, 0)));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Test]
    public void StraightColors_AreGivenUpOnWhenTheFileNowHoldsAnotherSize()
    {
        string name = nameof(StraightColors_AreGivenUpOnWhenTheFileNowHoldsAnotherSize);
        var (definition, context, created) = CreateSource(name);
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);
        Assert.That(resource.Source!.Bitmap, Is.Not.Null);
        using (var smaller = new SKBitmap(1, 1))
        {
            using SKData encoded = smaller.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(ImagePath(name), encoded.ToArray());
        }

        Assert.That(resource.GetStraightColorTexture(context.Object), Is.Null);
        Assert.That(created, Is.Empty);
    }

    // Halfway between the centers of the texels at 0.5, and clamped outside them.
    [TestCase(0f, 102f)]
    [TestCase(0.25f, 102f)]
    [TestCase(0.4f, 147.9f)]
    [TestCase(0.5f, 178.5f)]
    [TestCase(0.75f, 255f)]
    [TestCase(1f, 255f)]
    public void Alpha_IsFilteredAsTheMaterialsSampleIt(float u, float expected)
    {
        string name = $"{nameof(Alpha_IsFilteredAsTheMaterialsSampleIt)}-{u}";
        using (var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul)))
        {
            bitmap.SetPixel(0, 0, new SKColor(255, 255, 255, 102));
            bitmap.SetPixel(1, 0, new SKColor(255, 255, 255, 255));
            using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(ImagePath(name), encoded.ToArray());
        }

        var image = new ImageSource();
        image.ReadFrom(new Uri(ImagePath(name)));
        var definition = new ImageTextureSource { Source = { Value = image } };
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);

        Assert.That(resource.TryGetAlpha(new System.Numerics.Vector2(u, 0.5f), out float alpha), Is.True);
        Assert.That(alpha * 255, Is.EqualTo(expected).Within(0.5f));
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

        definition.Source.Value = Image(nameof(GetTexture_ReplacesBothKindsWhenTheSourceChanges) + "-next");
        bool versionBumped = false;
        resource.Reconcile(definition, CompositionContext.Default, ref versionBumped);
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

        definition.Source.Value = null;
        bool versionBumped = false;
        resource.Reconcile(definition, CompositionContext.Default, ref versionBumped);

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

    [Test]
    public void GetTexture_RasterizesSvgAtSurfaceDensity()
    {
        const string halfRedSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="10">
              <rect width="10" height="10" fill="#ff0000"/>
            </svg>
            """;
        var (definition, context, created) = CreateSvgSource(nameof(GetTexture_RasterizesSvgAtSurfaceDensity), halfRedSvg);
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);

        ITexture2D? texture = resource.GetTexture(context.Object, 4f, TextureContentKind.Color);

        Assert.That(texture, Is.SameAs(created.Single()));
        FakeTexture uploaded = created.Single();
        ReadOnlySpan<Half> pixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(uploaded.UploadedData);
        int rowOffset = 20 * uploaded.Width * 4;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(uploaded.Width, Is.EqualTo(80));
            Assert.That(uploaded.Height, Is.EqualTo(40));
            Assert.That(uploaded.Format, Is.EqualTo(TextureFormat.RGBA16Float));
            Assert.That((float)pixels[rowOffset + 38 * 4], Is.EqualTo(1f).Within(0.002f));
            Assert.That((float)pixels[rowOffset + 39 * 4], Is.EqualTo(1f).Within(0.002f));
            Assert.That((float)pixels[rowOffset + 40 * 4], Is.EqualTo(0f).Within(0.002f));
            Assert.That((float)pixels[rowOffset + 41 * 4], Is.EqualTo(0f).Within(0.002f));
        }
    }

    [Test]
    public void GetTexture_RasterizesSvgDataMapAtSurfaceDensity()
    {
        const string halfRedSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="10">
              <rect width="10" height="10" fill="#ff0000"/>
            </svg>
            """;
        var (definition, context, created) = CreateSvgSource(nameof(GetTexture_RasterizesSvgDataMapAtSurfaceDensity), halfRedSvg);
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);

        ITexture2D? texture = resource.GetTexture(context.Object, 4f, TextureContentKind.Data);

        Assert.That(texture, Is.SameAs(created.Single()));
        FakeTexture uploaded = created.Single();
        byte[] data = uploaded.UploadedData!;
        int leftOffset = (20 * uploaded.Width + 39) * 4;
        int rightOffset = (20 * uploaded.Width + 40) * 4;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(uploaded.Width, Is.EqualTo(80));
            Assert.That(uploaded.Height, Is.EqualTo(40));
            Assert.That(uploaded.Format, Is.EqualTo(TextureFormat.BGRA8Unorm));
            // BGRA8: B=0, G=0, R=255, A=255 on the left half; transparent on the right half.
            Assert.That(data[leftOffset + 2], Is.EqualTo(255));
            Assert.That(data[leftOffset + 3], Is.EqualTo(255));
            Assert.That(data[rightOffset + 3], Is.Zero);
        }
    }

    [Test]
    public void GetTexture_ReuploadsSvgWhenSurfaceDensityChangesAndReusesWhenUnchanged()
    {
        const string halfRedSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="10">
              <rect width="10" height="10" fill="#ff0000"/>
            </svg>
            """;
        var (definition, context, created) = CreateSvgSource(
            nameof(GetTexture_ReuploadsSvgWhenSurfaceDensityChangesAndReusesWhenUnchanged), halfRedSvg);
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);

        ITexture2D? first = resource.GetTexture(context.Object, 1f);
        ITexture2D? reused = resource.GetTexture(context.Object, 1f);
        ITexture2D? denser = resource.GetTexture(context.Object, 3f);
        ITexture2D? denserReused = resource.GetTexture(context.Object, 3f);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reused, Is.SameAs(first));
            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(created[0].Width, Is.EqualTo(20));
            Assert.That(created[0].Height, Is.EqualTo(10));
            Assert.That(created[0].Disposed, Is.True, "The lower-density upload must be released.");
            Assert.That(denser, Is.SameAs(created[1]));
            Assert.That(denserReused, Is.SameAs(denser));
            Assert.That(created[1].Width, Is.EqualTo(60));
            Assert.That(created[1].Height, Is.EqualTo(30));
        }
    }

    [Test]
    public void GetTexture_UploadsOversizedSvgClampedToDeviceAndPixelBudgets()
    {
        const string oversizedSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="16384" height="16384">
              <rect width="8192" height="16384" fill="#ff0000"/>
            </svg>
            """;
        var (definition, context, created) = CreateSvgSource(
            nameof(GetTexture_UploadsOversizedSvgClampedToDeviceAndPixelBudgets), oversizedSvg, maxAttachmentDimension: 256);
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);
        Assert.That(resource.Source!.Bitmap, Is.Null, "A 16384x16384 SVG has no default declared-size bitmap.");

        ITexture2D? clamped = resource.GetTexture(context.Object, 1f);
        ITexture2D? lowDensity = resource.GetTexture(context.Object, 0.01f);

        FakeTexture first = created[0];
        ReadOnlySpan<Half> pixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(first.UploadedData);
        int rowOffset = 128 * first.Width * 4;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(clamped, Is.SameAs(first));
            Assert.That(first.Width, Is.EqualTo(256));
            Assert.That(first.Height, Is.EqualTo(256));
            Assert.That((float)pixels[rowOffset + 64 * 4], Is.EqualTo(1f).Within(0.002f));
            Assert.That((float)pixels[rowOffset + 192 * 4], Is.EqualTo(0f).Within(0.002f));
            Assert.That(lowDensity, Is.SameAs(created[1]));
            Assert.That(created[1].Width, Is.EqualTo(164));
            Assert.That(created[1].Height, Is.EqualTo(164));
            Assert.That(resource.Source.ResolveSvgRasterSize(1f, 16384), Is.EqualTo(new PixelSize(8192, 8192)));
        }
    }

    [Test]
    public void GetStraightColorTexture_ReturnsNullForOversizedSvgWithoutDroppingCachedColorTexture()
    {
        const string oversizedSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="16384" height="16384">
              <rect width="8192" height="16384" fill="#ff0000"/>
            </svg>
            """;
        var (definition, context, created) = CreateSvgSource(
            nameof(GetStraightColorTexture_ReturnsNullForOversizedSvgWithoutDroppingCachedColorTexture),
            oversizedSvg,
            maxAttachmentDimension: 128);
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);

        ITexture2D? color = resource.GetTexture(context.Object, 1f);
        ITexture2D? straight = resource.GetStraightColorTexture(context.Object);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(straight, Is.Null);
            Assert.That(created.Single().Disposed, Is.False);
            Assert.That(resource.GetTexture(context.Object, 1f), Is.SameAs(color));
        }
    }

    [Test]
    public void TryGetAlpha_SamplesOversizedSvgWithoutBitmap()
    {
        const string oversizedSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="16384" height="16384">
              <rect width="8192" height="16384" fill="#ff0000"/>
            </svg>
            """;
        var (definition, _, _) = CreateSvgSource(nameof(TryGetAlpha_SamplesOversizedSvgWithoutBitmap), oversizedSvg);
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);

        Assert.That(resource.Source!.Bitmap, Is.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resource.TryGetAlpha(new System.Numerics.Vector2(0.25f, 0.5f), out float leftAlpha), Is.True);
            Assert.That(leftAlpha, Is.EqualTo(1f).Within(0.01f));
            Assert.That(resource.TryGetAlpha(new System.Numerics.Vector2(0.75f, 0.5f), out float rightAlpha), Is.True);
            Assert.That(rightAlpha, Is.EqualTo(0f).Within(0.01f));
        }
    }

    [Test]
    public void TryGetAlpha_MatchesDensityScaledSvgTextureAtEdge()
    {
        const string twoByOneSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="2" height="1">
              <rect width="1" height="1" fill="#ff0000"/>
            </svg>
            """;
        var (definition, context, _) = CreateSvgSource(nameof(TryGetAlpha_MatchesDensityScaledSvgTextureAtEdge), twoByOneSvg);
        using var resource = (ImageTextureSource.Resource)definition.ToResource(CompositionContext.Default);

        // At density 1 (2x1 texture), u = 0.4 linearly filters between texel 0 (1.0) and texel 1 (0.0) -> 0.7.
        Assert.That(resource.TryGetAlpha(new System.Numerics.Vector2(0.4f, 0.5f), out float alpha1x, surfaceDensity: 1f), Is.True);
        // At density 8 (16x8 texture), u = 0.4 samples texels 5 and 6 (both fully opaque) -> 1.0.
        Assert.That(resource.TryGetAlpha(new System.Numerics.Vector2(0.4f, 0.5f), out float alpha8x, surfaceDensity: 8f), Is.True);
        Assert.That(resource.TryGetAlpha(new System.Numerics.Vector2(0.5f, 0.5f), out float edge8x, surfaceDensity: 8f), Is.True);
        Assert.That(resource.TryGetAlpha(new System.Numerics.Vector2(0.6f, 0.5f), out float right8x, surfaceDensity: 8f), Is.True);

        // Once uploaded at density 8, callers that omit surfaceDensity still match the uploaded texture size.
        resource.GetTexture(context.Object, 8f);
        Assert.That(resource.TryGetAlpha(new System.Numerics.Vector2(0.4f, 0.5f), out float cached8x), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(alpha1x, Is.EqualTo(0.7f).Within(0.01f));
            Assert.That(alpha8x, Is.EqualTo(1f).Within(0.01f));
            Assert.That(edge8x, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(right8x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(cached8x, Is.EqualTo(1f).Within(0.01f));
        }
    }

    private static (ImageTextureSource, Mock<IGraphicsContext>, List<FakeTexture>) CreateSvgSource(
        string name, string svgContent, int maxAttachmentDimension = 0)
    {
        var created = new List<FakeTexture>();
        var context = new Mock<IGraphicsContext>();
        if (maxAttachmentDimension > 0)
            context.SetupGet(c => c.MaxAttachmentDimension).Returns(maxAttachmentDimension);
        context.Setup(c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns((int width, int height, TextureFormat format) =>
            {
                var texture = new FakeTexture(width, height, format, () => false);
                created.Add(texture);
                return texture;
            });

        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{nameof(ImageTextureSourceUploadTests)}-{name}.svg");
        File.WriteAllText(path, svgContent);
        var image = new ImageSource();
        image.ReadFrom(new Uri(path));
        var definition = new ImageTextureSource { Source = { Value = image } };
        return (definition, context, created);
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
        definition.Source.Value = Image(name);
        return (definition, context, created);
    }

    private static string ImagePath(string name)
        => Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{nameof(ImageTextureSourceUploadTests)}-{name}.png");

    private static ImageSource Image(string name)
    {
        string path = ImagePath(name);
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

        public byte[]? UploadedData { get; private set; }

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
            UploadedData = data.ToArray();
        }

        public byte[] DownloadPixels() => throw new NotSupportedException();

        public SKSurface CreateSkiaSurface() => throw new NotSupportedException();

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
