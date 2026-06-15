using Beutl.Converters;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PixelRectConverterTests
{
    private readonly PixelRectConverter _converter = new();

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
        PixelRect r = (PixelRect)_converter.ConvertFrom(null, null, "1,2,3,4")!;
        Assert.That(r, Is.EqualTo(new PixelRect(1, 2, 3, 4)));
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
