using Beutl.Converters;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class CornerRadiusConverterTests
{
    private readonly CornerRadiusConverter _converter = new();

    [Test]
    public void CanConvertFrom_String_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void CanConvertFrom_UnknownSource_ReturnsFalse()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(int)), Is.False);
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        CornerRadius result = (CornerRadius)_converter.ConvertFrom(null, null, "1,2,3,4")!;
        Assert.That(result, Is.EqualTo(new CornerRadius(1, 2, 3, 4)));
    }
}
