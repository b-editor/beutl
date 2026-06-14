using Beutl.Converters;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PixelSizeConverterTests
{
    private readonly PixelSizeConverter _converter = new();

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
        PixelSize s = (PixelSize)_converter.ConvertFrom(null, null, "10,20")!;
        Assert.That(s, Is.EqualTo(new PixelSize(10, 20)));
    }
}
