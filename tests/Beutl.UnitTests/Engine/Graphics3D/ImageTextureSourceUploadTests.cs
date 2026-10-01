using Beutl.Graphics3D.Textures;
using Beutl.Media;

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

    // An opaque, untagged (so sRGB) 8-bit bitmap whose x-th pixel stores x in every color channel.
    private static Bitmap Gradient()
    {
        var bitmap = new Bitmap(256, 1, BitmapColorType.Bgra8888, BitmapAlphaType.Premul);
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
}
