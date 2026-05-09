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

    [Test]
    public void PointJsonConverter_RoundTrips()
    {
        var p = new Point(1.5f, -2.25f);
        Assert.That(RoundTrip(p), Is.EqualTo(p));
    }

    [Test]
    public void PointJsonConverter_WritesString()
    {
        string json = JsonSerializer.Serialize(new Point(1.5f, 2f));
        Assert.That(json, Does.StartWith("\""));
        Assert.That(json, Does.EndWith("\""));
    }

    [Test]
    public void RectJsonConverter_RoundTrips()
    {
        var r = new Rect(1, 2, 3, 4);
        Assert.That(RoundTrip(r), Is.EqualTo(r));
    }

    [Test]
    public void SizeJsonConverter_RoundTrips()
    {
        var s = new Size(3, 4);
        Assert.That(RoundTrip(s), Is.EqualTo(s));
    }

    [Test]
    public void VectorJsonConverter_RoundTrips()
    {
        var v = new Vector(2.5f, 3.5f);
        var result = RoundTrip(v);
        Assert.That(result.X, Is.EqualTo(v.X));
        Assert.That(result.Y, Is.EqualTo(v.Y));
    }

    [Test]
    public void ThicknessJsonConverter_RoundTrips()
    {
        var t = new Thickness(1, 2, 3, 4);
        Assert.That(RoundTrip(t), Is.EqualTo(t));
    }

    [Test]
    public void RelativePointJsonConverter_RoundTrips()
    {
        var p = RelativePoint.Parse("0.5,0.5");
        Assert.That(RoundTrip(p), Is.EqualTo(p));
    }

    [Test]
    public void RelativeRectJsonConverter_RoundTrips()
    {
        var r = RelativeRect.Parse("0%,0%,100%,100%");
        Assert.That(RoundTrip(r), Is.EqualTo(r));
    }

    [Test]
    public void PixelPointJsonConverter_RoundTrips()
    {
        var p = new PixelPoint(3, 4);
        Assert.That(RoundTrip(p), Is.EqualTo(p));
    }

    [Test]
    public void PixelRectJsonConverter_RoundTrips()
    {
        var r = new PixelRect(1, 2, 3, 4);
        Assert.That(RoundTrip(r), Is.EqualTo(r));
    }

    [Test]
    public void PixelSizeJsonConverter_RoundTrips()
    {
        var s = new PixelSize(3, 4);
        Assert.That(RoundTrip(s), Is.EqualTo(s));
    }

    [Test]
    public void ColorJsonConverter_RoundTrips()
    {
        var c = Color.FromArgb(255, 100, 50, 25);
        Assert.That(RoundTrip(c), Is.EqualTo(c));
    }

    [Test]
    public void GradingColorJsonConverter_RoundTrips()
    {
        var c = new GradingColor(0.1f, 0.5f, 0.9f);
        Assert.That(RoundTrip(c), Is.EqualTo(c));
    }

    [Test]
    public void CornerRadiusJsonConverter_RoundTrips()
    {
        var cr = new CornerRadius(1, 2, 3, 4);
        Assert.That(RoundTrip(cr), Is.EqualTo(cr));
    }

    [Test]
    public void MatrixJsonConverter_RoundTrips_Identity()
    {
        var m = Matrix.Identity;
        Assert.That(RoundTrip(m), Is.EqualTo(m));
    }

    [Test]
    public void MatrixJsonConverter_RoundTrips_Scale()
    {
        var m = Matrix.CreateScale(2, 3);
        Assert.That(RoundTrip(m), Is.EqualTo(m));
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
