using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

public class BitmapColorTypeExtensionsTests
{
    private static readonly (BitmapColorType Beutl, SKColorType Sk)[] s_pairs =
    [
        (BitmapColorType.Alpha8, SKColorType.Alpha8),
        (BitmapColorType.Rgb565, SKColorType.Rgb565),
        (BitmapColorType.Argb4444, SKColorType.Argb4444),
        (BitmapColorType.Rgba8888, SKColorType.Rgba8888),
        (BitmapColorType.Rgb888x, SKColorType.Rgb888x),
        (BitmapColorType.Bgra8888, SKColorType.Bgra8888),
        (BitmapColorType.Rgba1010102, SKColorType.Rgba1010102),
        (BitmapColorType.Bgra1010102, SKColorType.Bgra1010102),
        (BitmapColorType.Rgb101010x, SKColorType.Rgb101010x),
        (BitmapColorType.Bgr101010x, SKColorType.Bgr101010x),
        (BitmapColorType.Bgr101010xXR, SKColorType.Bgr101010xXR),
        (BitmapColorType.Gray8, SKColorType.Gray8),
        (BitmapColorType.RgbaF16, SKColorType.RgbaF16),
        (BitmapColorType.RgbaF16Clamped, SKColorType.RgbaF16Clamped),
        (BitmapColorType.RgbaF32, SKColorType.RgbaF32),
        (BitmapColorType.Rg88, SKColorType.Rg88),
        (BitmapColorType.AlphaF16, SKColorType.AlphaF16),
        (BitmapColorType.RgF16, SKColorType.RgF16),
        (BitmapColorType.Alpha16, SKColorType.Alpha16),
        (BitmapColorType.Rg1616, SKColorType.Rg1616),
        (BitmapColorType.Rgba16161616, SKColorType.Rgba16161616),
        (BitmapColorType.Srgba8888, SKColorType.Srgba8888),
        (BitmapColorType.R8Unorm, SKColorType.R8Unorm),
    ];

    [TestCaseSource(nameof(s_pairs))]
    public void RoundTrip_BetweenBeutlAndSkia((BitmapColorType beutl, SKColorType sk) pair)
    {
        Assert.That(pair.beutl.ToSKColorType(), Is.EqualTo(pair.sk));
        Assert.That(BitmapColorTypeExtensions.FromSKColorType(pair.sk), Is.EqualTo(pair.beutl));
    }

    [Test]
    public void Unknown_To_Sk_ReturnsUnknown()
    {
        Assert.That(BitmapColorType.Unknown.ToSKColorType(), Is.EqualTo(SKColorType.Unknown));
    }

    [Test]
    public void Unknown_From_Sk_ReturnsUnknown()
    {
        Assert.That(BitmapColorTypeExtensions.FromSKColorType(SKColorType.Unknown),
            Is.EqualTo(BitmapColorType.Unknown));
    }

    [Test]
    public void OutOfRange_BeutlValue_ReturnsSkUnknown()
    {
        Assert.That(((BitmapColorType)int.MaxValue).ToSKColorType(),
            Is.EqualTo(SKColorType.Unknown));
    }

    [Test]
    public void OutOfRange_SkValue_ReturnsBeutlUnknown()
    {
        Assert.That(BitmapColorTypeExtensions.FromSKColorType((SKColorType)int.MaxValue),
            Is.EqualTo(BitmapColorType.Unknown));
    }
}
