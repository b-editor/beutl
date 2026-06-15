using Beutl.Converters;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PixelPointConverterTests
{
    private readonly PixelPointConverter _converter = new();

    [Test]
    public void CanConvertFrom_String_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void CanConvertFrom_UnknownSource_ReturnsFalse()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(DateTime)), Is.False);
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        PixelPoint p = (PixelPoint)_converter.ConvertFrom(null, null, "12,34")!;
        Assert.That(p, Is.EqualTo(new PixelPoint(12, 34)));
    }

    [Test]
    public void ConvertFrom_InvalidString_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => _converter.ConvertFrom(null, null, "invalid"));
    }

    [Test]
    public void ConvertFrom_UnsupportedType_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => _converter.ConvertFrom(null, null, 42));
    }
}
