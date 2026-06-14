using Beutl.Converters;
using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

public class VectorConverterTests
{
    private readonly VectorConverter _converter = new();

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
        Vector v = (Vector)_converter.ConvertFrom(null, null, "1.5,2.5")!;
        Assert.That(v.X, Is.EqualTo(1.5f));
        Assert.That(v.Y, Is.EqualTo(2.5f));
    }
}
