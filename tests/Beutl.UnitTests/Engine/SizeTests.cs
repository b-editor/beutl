using System.Globalization;
using System.Text;
using Beutl.Graphics;
using Beutl.Media;
using NUnit.Framework;

namespace Beutl.UnitTests.Engine;

public class SizeTests
{
    [Test]
    public void Parse_CommaSeparatedString_ReturnsCorrectSize()
    {
        const string str = "1920,1080";
        var size = Size.Parse(str);

        Assert.That(size, Is.EqualTo(new Size(1920, 1080)));
    }

    [Test]
    public void ParseSpan_CommaSeparatedString_ReturnsCorrectSize()
    {
        const string str = "1920,1080";
        var size = Size.Parse(str.AsSpan());

        Assert.That(size, Is.EqualTo(new Size(1920, 1080)));
    }

    [Test]
    public void ParseWithProvider_SemicolonSeparatedString_ReturnsCorrectSize()
    {
        const string str = "1920;1080";
        var size = Size.Parse(str, CultureInfo.GetCultureInfo("fr"));

        Assert.That(size, Is.EqualTo(new Size(1920, 1080)));
    }

    [Test]
    public void ParseUtf8_CommaSeparatedUtf8String_ReturnsCorrectSize()
    {
        ReadOnlySpan<byte> str = "1920,1080"u8;
        var size = Size.Parse(str);

        Assert.That(size, Is.EqualTo(new Size(1920, 1080)));
    }

    [Test]
    public void ParseUtf8WithProvider_SemicolonSeparatedUtf8String_ReturnsCorrectSize()
    {
        ReadOnlySpan<byte> str = "1920;1080"u8;
        var size = Size.Parse(str, CultureInfo.GetCultureInfo("fr"));

        Assert.That(size, Is.EqualTo(new Size(1920, 1080)));
    }

    [Test]
    public void FormatToSpan_SizeFormattedToSpan_ReturnsCorrectString()
    {
        const string str = "1920, 1080";
        var size = new Size(1920, 1080);
        Span<char> s = stackalloc char[64];

        size.TryFormat(s, out int written);
        Assert.That(s.Slice(0, written).ToString(), Is.EqualTo(str));
    }

    [Test]
    public void FormatToUtf8_SizeFormattedToUtf8_ReturnsCorrectString()
    {
        const string str = "1920, 1080";
        var size = new Size(1920, 1080);
        Span<byte> s = stackalloc byte[64];

        size.TryFormat(s, out int written);

        Assert.That(Encoding.UTF8.GetString(s.Slice(0, written)), Is.EqualTo(str));
    }

    [Test]
    public void Deflate_SizeDeflatedByThickness_ReturnsCorrectSize()
    {
        var size = new Size(1920, 1080);
        var thickness = new Thickness(10, 15);

        size = size.Deflate(thickness);

        Assert.That(size, Is.EqualTo(new Size(1900, 1050)));
    }

    [Test]
    public void Inflate_SizeInflatedByThickness_ReturnsCorrectSize()
    {
        var size = new Size(1920, 1080);
        var thickness = new Thickness(15, 10);

        size = size.Inflate(thickness);

        Assert.That(size, Is.EqualTo(new Size(1950, 1100)));
    }


    [Test]
    public void AspectRatio_ReturnsCorrectRatio()
    {
        var size = new Size(1920, 1080);
        Assert.That(size.AspectRatio, Is.EqualTo(16.0f / 9.0f));
    }

    [Test]
    public void IsDefault_ReturnsTrueForDefaultSize()
    {
        var size = new Size();
        Assert.That(size.IsDefault, Is.True);
    }

    [Test]
    public void EqualityOperator_ReturnsTrueForEqualSizes()
    {
        var size1 = new Size(1920, 1080);
        var size2 = new Size(1920, 1080);
        Assert.That(size1 == size2, Is.True);
    }

    [Test]
    public void InequalityOperator_ReturnsTrueForDifferentSizes()
    {
        var size1 = new Size(1920, 1080);
        var size2 = new Size(1280, 720);
        Assert.That(size1 != size2, Is.True);
    }

    [Test]
    public void MultiplicationOperator_ReturnsScaledSize()
    {
        var size = new Size(1920, 1080);
        var scaledSize = size * 2;
        Assert.That(scaledSize, Is.EqualTo(new Size(3840, 2160)));
    }

    [Test]
    public void DivisionOperator_ReturnsScaledSize()
    {
        var size = new Size(1920, 1080);
        var scaledSize = size / 2;
        Assert.That(scaledSize, Is.EqualTo(new Size(960, 540)));
    }

    [Test]
    public void AdditionOperator_ReturnsSummedSize()
    {
        var size1 = new Size(1920, 1080);
        var size2 = new Size(1280, 720);
        var summedSize = size1 + size2;
        Assert.That(summedSize, Is.EqualTo(new Size(3200, 1800)));
    }

    [Test]
    public void SubtractionOperator_ReturnsDifferenceSize()
    {
        var size1 = new Size(1920, 1080);
        var size2 = new Size(1280, 720);
        var diffSize = size1 - size2;
        Assert.That(diffSize, Is.EqualTo(new Size(640, 360)));
    }

    [Test]
    public void TryParse_ValidString_ReturnsTrueAndCorrectSize()
    {
        const string str = "1920,1080";
        var result = Size.TryParse(str, out var size);
        Assert.That(result, Is.True);
        Assert.That(size, Is.EqualTo(new Size(1920, 1080)));
    }

    [Test]
    public void Constrain_SizeConstrainedByMaxSize_ReturnsCorrectSize()
    {
        var size = new Size(1920, 1080);
        var maxSize = new Size(1280, 720);
        var constrainedSize = size.Constrain(maxSize);
        Assert.That(constrainedSize, Is.EqualTo(maxSize));
    }

    [Test]
    public void NearlyEquals_SizesWithinTolerance_ReturnsTrue()
    {
        var size1 = new Size(1920, 1080);
        var size2 = new Size(1920.0001f, 1080.0001f);
        Assert.That(size1.NearlyEquals(size2), Is.True);
    }

    [Test]
    public void WithWidth_ReturnsSizeWithNewWidth()
    {
        var size = new Size(1920, 1080);
        var newSize = size.WithWidth(1280);
        Assert.That(newSize, Is.EqualTo(new Size(1280, 1080)));
    }

    [Test]
    public void WithHeight_ReturnsSizeWithNewHeight()
    {
        var size = new Size(1920, 1080);
        var newSize = size.WithHeight(720);
        Assert.That(newSize, Is.EqualTo(new Size(1920, 720)));
    }

    [Test]
    public void Ceiling_ReturnsSizeWithCeilingValues()
    {
        var size = new Size(1920.5f, 1080.5f);
        var ceilingSize = size.Ceiling();
        Assert.That(ceilingSize, Is.EqualTo(new PixelSize(1921, 1081)));
    }
}
