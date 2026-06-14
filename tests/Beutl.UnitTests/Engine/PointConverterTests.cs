using Beutl.Converters;
using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

public class PointConverterTests
{
    private readonly PointConverter _converter = new();

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
    public void ConvertFrom_String_RoundTripsViaParse()
    {
        Point parsed = (Point)_converter.ConvertFrom(null, null, "1.5,2.5")!;
        Assert.That(parsed, Is.EqualTo(new Point(1.5f, 2.5f)));
    }
}
