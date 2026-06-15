using Beutl.Converters;
using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

public class SizeConverterTests
{
    private readonly SizeConverter _converter = new();

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
        Size s = (Size)_converter.ConvertFrom(null, null, "10,20")!;
        Assert.That(s, Is.EqualTo(new Size(10, 20)));
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
