using System.Text.Json;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class TypeJsonConvertersTests
{
    private static T RoundTrip<T>(T value)
    {
        string json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<T>(json)!;
    }

    public static IEnumerable<TestCaseData> RoundTripValues()
    {
        yield return Case(new Point(1.5f, -2.25f), "Point");
        yield return Case(new Rect(1, 2, 3, 4), "Rect");
        yield return Case(new Size(3, 4), "Size");
        yield return Case(new Vector(2.5f, 3.5f), "Vector");
        yield return Case(new Thickness(1, 2, 3, 4), "Thickness");
        yield return Case(RelativePoint.Parse("0.5,0.5"), "RelativePoint");
        yield return Case(RelativeRect.Parse("0%,0%,100%,100%"), "RelativeRect");
        yield return Case(new PixelPoint(3, 4), "PixelPoint");
        yield return Case(new PixelRect(1, 2, 3, 4), "PixelRect");
        yield return Case(new PixelSize(3, 4), "PixelSize");
        yield return Case(Color.FromArgb(255, 100, 50, 25), "Color");
        yield return Case(new GradingColor(0.1f, 0.5f, 0.9f), "GradingColor");
        yield return Case(new CornerRadius(1, 2, 3, 4), "CornerRadius");
        yield return Case(Matrix.Identity, "Matrix identity");
        yield return Case(Matrix.CreateScale(2, 3), "Matrix scale");

        static TestCaseData Case(object value, string name) => new TestCaseData(value).SetArgDisplayNames(name);
    }

    [TestCaseSource(nameof(RoundTripValues))]
    public void JsonConverter_RoundTrips(object value)
    {
        string json = JsonSerializer.Serialize(value, value.GetType());
        Assert.That(JsonSerializer.Deserialize(json, value.GetType()), Is.EqualTo(value));
    }

    [Test]
    public void PointJsonConverter_WritesString()
    {
        string json = JsonSerializer.Serialize(new Point(1.5f, 2f));
        Assert.That(json, Does.StartWith("\""));
        Assert.That(json, Does.EndWith("\""));
    }

    [Test]
    public void CurveControlPointJsonConverter_RoundTrips()
    {
        var p = new CurveControlPoint(0.5f, 0.5f);
        var result = RoundTrip(p);
        Assert.That(result.Point, Is.EqualTo(p.Point));
        Assert.That(result.LeftHandle, Is.EqualTo(p.LeftHandle));
        Assert.That(result.RightHandle, Is.EqualTo(p.RightHandle));
    }

    [Test]
    public void CurveMapJsonConverter_RoundTripsDefaultMap()
    {
        var map = CurveMap.Default;
        var result = RoundTrip(map);
        Assert.That(result, Is.EqualTo(map));
    }

    [Test]
    public void FontFamilyJsonConverter_RoundTrips()
    {
        var family = new FontFamily("Arial");
        var result = RoundTrip(family);
        Assert.That(result.Name, Is.EqualTo(family.Name));
    }

    [Test]
    public void FontFamilyJsonConverter_WritesAsString()
    {
        var family = new FontFamily("Arial");
        string json = JsonSerializer.Serialize(family);
        Assert.That(json, Is.EqualTo("\"Arial\""));
    }

    [Test]
    public void TypefaceJsonConverter_RoundTrips()
    {
        var typeface = new Typeface(new FontFamily("Arial"), FontStyle.Italic, FontWeight.Bold);
        var result = RoundTrip(typeface);
        Assert.That(result, Is.EqualTo(typeface));
    }

    [Test]
    public void TypefaceJsonConverter_WriteStructure()
    {
        var typeface = new Typeface(new FontFamily("Arial"), FontStyle.Italic, FontWeight.Bold);
        string json = JsonSerializer.Serialize(typeface);
        Assert.That(json, Does.Contain("fontfamily"));
        Assert.That(json, Does.Contain("Arial"));
        Assert.That(json, Does.Contain("weight"));
        Assert.That(json, Does.Contain("style"));
    }

    [Test]
    public void TypefaceJsonConverter_DeserializesWithDefaults_WhenWeightAndStyleOmitted()
    {
        const string json = "{\"fontfamily\":\"Arial\"}";
        var result = JsonSerializer.Deserialize<Typeface>(json);
        Assert.That(result.FontFamily.Name, Is.EqualTo("Arial"));
        Assert.That(result.Weight, Is.EqualTo(FontWeight.Regular));
        Assert.That(result.Style, Is.EqualTo(FontStyle.Normal));
    }
}
