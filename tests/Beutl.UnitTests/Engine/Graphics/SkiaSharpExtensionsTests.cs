using Beutl.Graphics;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics;

public class SkiaSharpExtensionsTests
{
    [Test]
    public void Point_RoundTripsThroughSKPoint()
    {
        var p = new Point(1.5f, 2.5f);
        SKPoint sk = p.ToSKPoint();
        Point back = sk.ToGraphicsPoint();

        Assert.Multiple(() =>
        {
            Assert.That(sk.X, Is.EqualTo(1.5f));
            Assert.That(sk.Y, Is.EqualTo(2.5f));
            Assert.That(back, Is.EqualTo(p));
        });
    }

    [Test]
    public void PixelPoint_ToSKPointI_ConvertsValues()
    {
        var p = new PixelPoint(10, 20);
        SKPointI sk = p.ToSKPointI();
        Assert.That(sk, Is.EqualTo(new SKPointI(10, 20)));
    }

    [Test]
    public void Rect_RoundTripsThroughSKRect()
    {
        var r = new Rect(1, 2, 3, 4);
        SKRect sk = r.ToSKRect();
        Rect back = sk.ToGraphicsRect();

        Assert.Multiple(() =>
        {
            Assert.That(sk.Left, Is.EqualTo(1f));
            Assert.That(sk.Top, Is.EqualTo(2f));
            Assert.That(sk.Right, Is.EqualTo(4f));
            Assert.That(sk.Bottom, Is.EqualTo(6f));
            Assert.That(back, Is.EqualTo(r));
        });
    }

    [Test]
    public void PixelRect_ToSKRectI_ConvertsValues()
    {
        var r = new PixelRect(1, 2, 3, 4);
        SKRectI sk = r.ToSKRectI();
        Assert.Multiple(() =>
        {
            Assert.That(sk.Left, Is.EqualTo(1));
            Assert.That(sk.Top, Is.EqualTo(2));
            Assert.That(sk.Right, Is.EqualTo(4));
            Assert.That(sk.Bottom, Is.EqualTo(6));
        });
    }

    [Test]
    public void Size_ToSKSize_ConvertsValues()
    {
        var s = new Size(100f, 200f);
        SKSize sk = s.ToSKSize();

        Assert.That(sk, Is.EqualTo(new SKSize(100f, 200f)));
    }

    [Test]
    public void PixelSize_ToSKSizeI_ConvertsValues()
    {
        var s = new PixelSize(640, 480);
        SKSizeI sk = s.ToSKSizeI();

        Assert.That(sk, Is.EqualTo(new SKSizeI(640, 480)));
    }

    [Test]
    public void Matrix_RoundTripsThroughSKMatrix()
    {
        var m = new Matrix(1, 2, 3, 4, 5, 6);
        SKMatrix sk = m.ToSKMatrix();
        Matrix back = sk.ToMatrix();

        Assert.That(back, Is.EqualTo(m));
    }

    [Test]
    public void Color_ToSKColor_PreservesChannels()
    {
        var c = Color.FromArgb(0x10, 0x20, 0x30, 0x40);
        SKColor sk = c.ToSKColor();

        Assert.Multiple(() =>
        {
            Assert.That(sk.Alpha, Is.EqualTo(0x10));
            Assert.That(sk.Red, Is.EqualTo(0x20));
            Assert.That(sk.Green, Is.EqualTo(0x30));
            Assert.That(sk.Blue, Is.EqualTo(0x40));
        });
    }

    [Test]
    [TestCase(GradientSpreadMethod.Pad, SKShaderTileMode.Clamp)]
    [TestCase(GradientSpreadMethod.Reflect, SKShaderTileMode.Mirror)]
    [TestCase(GradientSpreadMethod.Repeat, SKShaderTileMode.Repeat)]
    [TestCase(GradientSpreadMethod.Decal, SKShaderTileMode.Decal)]
    public void GradientSpreadMethod_ToSKShaderTileMode(GradientSpreadMethod m, SKShaderTileMode expected)
    {
        Assert.That(m.ToSKShaderTileMode(), Is.EqualTo(expected));
    }

    [Test]
    [TestCase(ClipOperation.Difference, SKClipOperation.Difference)]
    [TestCase(ClipOperation.Intersect, SKClipOperation.Intersect)]
    public void ClipOperation_ToSKClipOperation(ClipOperation op, SKClipOperation expected)
    {
        Assert.That(op.ToSKClipOperation(), Is.EqualTo(expected));
    }

    [Test]
    public void BitmapInterpolationMode_ToSKSamplingOptions_LowQualityIsLinearNoMip()
    {
        SKSamplingOptions opts = BitmapInterpolationMode.LowQuality.ToSKSamplingOptions();
        Assert.That(opts.UseCubic, Is.False);
    }

    [Test]
    public void BitmapInterpolationMode_ToSKSamplingOptions_HighQualityIsCubic()
    {
        SKSamplingOptions opts = BitmapInterpolationMode.HighQuality.ToSKSamplingOptions();
        Assert.That(opts.UseCubic, Is.True);
    }

    [Test]
    public void BitmapInterpolationMode_ToSKSamplingOptions_DefaultIsCubic()
    {
        SKSamplingOptions opts = BitmapInterpolationMode.Default.ToSKSamplingOptions();
        Assert.That(opts.UseCubic, Is.True);
    }

    [Test]
    public void BitmapInterpolationMode_ToSKSamplingOptions_InvalidThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ((BitmapInterpolationMode)999).ToSKSamplingOptions());
    }

    [Test]
    [TestCase(SKFontStyleSlant.Upright, FontStyle.Normal)]
    [TestCase(SKFontStyleSlant.Italic, FontStyle.Italic)]
    [TestCase(SKFontStyleSlant.Oblique, FontStyle.Oblique)]
    public void SKFontStyleSlant_ToFontStyle(SKFontStyleSlant slant, FontStyle expected)
    {
        Assert.That(slant.ToFontStyle(), Is.EqualTo(expected));
    }

    [Test]
    public void SKFontStyleSlant_ToFontStyle_InvalidThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ((SKFontStyleSlant)999).ToFontStyle());
    }

    // Note: SKFontMetrics の各フィールドは読み取り専用かつコンストラクタで全て指定する手段がないため、
    // ToFontMetrics のフィールド対応マッピングを単体テストで網羅検証することは困難。
    // 既存の TextBlock 系統合テストでフォント計測ロジック全体は検証されている。
}
