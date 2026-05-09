using Beutl.Converters;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class CornerRadiusConverterTests
{
    private readonly CornerRadiusConverter _converter = new();

    [Test]
    public void CanConvertTo_KnownTargets_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(float[])), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Tuple<float, float, float, float>)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Tuple<float, float>)), Is.True);
    }

    [Test]
    public void CanConvertTo_OtherTypes_FallsBackToBase()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(int)), Is.False);
    }

    [Test]
    public void ConvertTo_FloatArray_ReturnsFourComponents()
    {
        var radius = new CornerRadius(1, 2, 3, 4);
        float[] result = (float[])_converter.ConvertTo(null, null, radius, typeof(float[]))!;
        Assert.That(result, Is.EqualTo(new[] { 1f, 2f, 3f, 4f }));
    }

    [Test]
    public void ConvertTo_FourTuple_ReturnsFourComponents()
    {
        var radius = new CornerRadius(1, 2, 3, 4);
        Tuple<float, float, float, float> result =
            (Tuple<float, float, float, float>)_converter.ConvertTo(null, null, radius, typeof(Tuple<float, float, float, float>))!;
        Assert.That(result, Is.EqualTo(new Tuple<float, float, float, float>(1, 2, 3, 4)));
    }

    [Test]
    public void ConvertTo_TwoTuple_ReturnsTopLeftAndBottomLeft()
    {
        var radius = new CornerRadius(10, 20, 30, 40);
        Tuple<float, float> result =
            (Tuple<float, float>)_converter.ConvertTo(null, null, radius, typeof(Tuple<float, float>))!;
        Assert.That(result.Item1, Is.EqualTo(10f));
        Assert.That(result.Item2, Is.EqualTo(40f));
    }

    [Test]
    public void ConvertFrom_OneElementArray_UsesUniform()
    {
        CornerRadius result = (CornerRadius)_converter.ConvertFrom(null, null, new[] { 5f })!;
        Assert.That(result, Is.EqualTo(new CornerRadius(5)));
    }

    [Test]
    public void ConvertFrom_TwoElementArray_UsesPair()
    {
        CornerRadius result = (CornerRadius)_converter.ConvertFrom(null, null, new[] { 1f, 3f })!;
        Assert.That(result, Is.EqualTo(new CornerRadius(1, 3)));
    }

    [Test]
    public void ConvertFrom_FourElementArray_UsesAll()
    {
        CornerRadius result = (CornerRadius)_converter.ConvertFrom(null, null, new[] { 1f, 2f, 3f, 4f })!;
        Assert.That(result, Is.EqualTo(new CornerRadius(1, 2, 3, 4)));
    }

    [Test]
    public void ConvertFrom_Float_UsesUniform()
    {
        CornerRadius result = (CornerRadius)_converter.ConvertFrom(null, null, 7f)!;
        Assert.That(result, Is.EqualTo(new CornerRadius(7)));
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        CornerRadius result = (CornerRadius)_converter.ConvertFrom(null, null, "1,2,3,4")!;
        Assert.That(result, Is.EqualTo(new CornerRadius(1, 2, 3, 4)));
    }
}
