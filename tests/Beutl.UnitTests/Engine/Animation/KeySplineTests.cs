using Beutl.Animation;

namespace Beutl.UnitTests.Engine.Animation;

[TestFixture]
public class KeySplineTests
{
    [Test]
    public void TryParse_ShouldReturnTrueForValidString()
    {
        bool result = KeySpline.TryParse("0.1,0.2,0.3,0.4", out var keySpline);

        Assert.That(result, Is.True);
        Assert.That(keySpline, Is.Not.Null);
        Assert.That(keySpline!.ControlPointX1, Is.EqualTo(0.1f));
        Assert.That(keySpline.ControlPointY1, Is.EqualTo(0.2f));
        Assert.That(keySpline.ControlPointX2, Is.EqualTo(0.3f));
        Assert.That(keySpline.ControlPointY2, Is.EqualTo(0.4f));
    }

    [Test]
    public void TryParse_ShouldReturnFalseForInvalidString()
    {
        bool result = KeySpline.TryParse("invalid", out var keySpline);

        Assert.That(result, Is.False);
        Assert.That(keySpline, Is.Null);
    }

    [Test]
    public void Parse_ShouldThrowFormatExceptionForInvalidString()
    {
        Assert.That(() => KeySpline.Parse("invalid"), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void ControlPointX1_ShouldThrowArgumentExceptionForInvalidValue()
    {
        var keySpline = new KeySpline();

        Assert.That(() => keySpline.ControlPointX1 = -0.1f, Throws.TypeOf<ArgumentException>());
        Assert.That(() => keySpline.ControlPointX1 = 1.1f, Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ControlPointX2_ShouldThrowArgumentExceptionForInvalidValue()
    {
        var keySpline = new KeySpline();

        Assert.That(() => keySpline.ControlPointX2 = -0.1f, Throws.TypeOf<ArgumentException>());
        Assert.That(() => keySpline.ControlPointX2 = 1.1f, Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void GetSplineProgress_ShouldReturnLinearProgressWhenNotSpecified()
    {
        var keySpline = new KeySpline();

        float progress = keySpline.GetSplineProgress(0.5f);

        Assert.That(progress, Is.EqualTo(0.5f));
    }

    [Test]
    public void GetSplineProgress_ShouldReturnBezierValueWhenSpecified()
    {
        var keySpline = new KeySpline(0.25f, 0.1f, 0.25f, 1.0f);

        float progress = keySpline.GetSplineProgress(0.5f);

        Assert.That(progress, Is.EqualTo(0.8024f).Within(0.001f));
    }

    [Test]
    public void IsValid_ShouldReturnTrueForValidControlPoints()
    {
        var keySpline = new KeySpline(0.1f, 0.2f, 0.3f, 0.4f);

        bool isValid = keySpline.IsValid();

        Assert.That(isValid, Is.True);
    }

    [Test]
    public void IsValid_ShouldReturnFalseForInvalidControlPoints()
    {
        var keySpline = new KeySpline(-0.1f, 0.2f, 1.1f, 0.4f);

        bool isValid = keySpline.IsValid();

        Assert.That(isValid, Is.False);
    }
}
