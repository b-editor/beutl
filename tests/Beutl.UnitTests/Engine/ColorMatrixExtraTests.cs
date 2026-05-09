using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class ColorMatrixExtraTests
{
    [Test]
    public void Identity_IsIdentity()
    {
        Assert.That(ColorMatrix.Identity.IsIdentity, Is.True);
    }

    [Test]
    public void DefaultStruct_IsNotIdentity()
    {
        Assert.That(default(ColorMatrix).IsIdentity, Is.False);
    }

    [Test]
    public void Equals_AndOperators()
    {
        var m1 = ColorMatrix.Identity;
        var m2 = ColorMatrix.Identity;
        var m3 = ColorMatrix.CreateBrightness(2f);

        Assert.That(m1, Is.EqualTo(m2));
        Assert.That(m1 == m2, Is.True);
        Assert.That(m1 != m3, Is.True);
        Assert.That(m1.Equals((object)m2), Is.True);
        Assert.That(m1.Equals((object)"not a matrix"), Is.False);
        Assert.That(m1.GetHashCode(), Is.EqualTo(m2.GetHashCode()));
    }

    [Test]
    public void IdentityTimesColor_ReturnsSameColor()
    {
        var color = Color.FromArgb(200, 100, 150, 50);
        var result = ColorMatrix.Identity * color;

        Assert.That(result, Is.EqualTo(color));
    }

    [Test]
    public void IdentityTimesIdentity_IsIdentity()
    {
        var result = ColorMatrix.Identity * ColorMatrix.Identity;
        Assert.That(result.IsIdentity, Is.True);
    }

    [Test]
    public void CreateBrightness_AppliedToColor_ScalesRGB()
    {
        var brightness = ColorMatrix.CreateBrightness(0.5f);
        var input = Color.FromArgb(255, 200, 100, 50);
        var output = brightness * input;

        Assert.That(output.R, Is.EqualTo(100).Within(1));
        Assert.That(output.G, Is.EqualTo(50).Within(1));
        Assert.That(output.B, Is.EqualTo(25).Within(1));
        Assert.That(output.A, Is.EqualTo(255));
    }

    [Test]
    public void CreateSaturate_One_IsIdentityOnRgb()
    {
        var sat = ColorMatrix.CreateSaturate(1f);
        var input = Color.FromArgb(255, 100, 150, 200);
        var output = sat * input;

        Assert.That(output.R, Is.EqualTo(input.R).Within(1));
        Assert.That(output.G, Is.EqualTo(input.G).Within(1));
        Assert.That(output.B, Is.EqualTo(input.B).Within(1));
    }

    [Test]
    public void CreateSaturate_Zero_ProducesGreyscale()
    {
        var sat = ColorMatrix.CreateSaturate(0f);
        var input = Color.FromArgb(255, 200, 200, 200);
        var output = sat * input;

        Assert.That(output.R, Is.EqualTo(output.G));
        Assert.That(output.G, Is.EqualTo(output.B));
    }

    [Test]
    public void CreateHueRotate_ZeroIsApproxIdentity()
    {
        var rot = ColorMatrix.CreateHueRotate(0f);
        var input = Color.FromArgb(255, 100, 150, 200);
        var output = rot * input;

        Assert.That(output.R, Is.EqualTo(input.R).Within(2));
        Assert.That(output.G, Is.EqualTo(input.G).Within(2));
        Assert.That(output.B, Is.EqualTo(input.B).Within(2));
    }

    [Test]
    public void CreateLuminanceToAlpha_PreservesRgb()
    {
        var matrix = ColorMatrix.CreateLuminanceToAlpha();
        var input = Color.FromArgb(0, 100, 150, 200);
        var output = matrix * input;

        Assert.That(output.R, Is.EqualTo(input.R));
        Assert.That(output.G, Is.EqualTo(input.G));
        Assert.That(output.B, Is.EqualTo(input.B));
    }

    [Test]
    public void CreateContrast_Zero_IsIdentityScale()
    {
        var matrix = ColorMatrix.CreateContrast(0f);
        var input = Color.FromArgb(255, 128, 128, 128);
        var output = matrix * input;

        Assert.That(output.R, Is.EqualTo(input.R).Within(1));
    }

    [Test]
    public void CreateFromSpan_TooShort_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            Span<float> span = stackalloc float[10];
            ColorMatrix.CreateFromSpan(span);
        });
    }

    [Test]
    public void ToArray_ReturnsAllTwentyValues()
    {
        var array = ColorMatrix.Identity.ToArray();

        Assert.That(array.Length, Is.EqualTo(20));
        Assert.That(array[0], Is.EqualTo(1));
        Assert.That(array[6], Is.EqualTo(1));
        Assert.That(array[12], Is.EqualTo(1));
        Assert.That(array[18], Is.EqualTo(1));
    }

    [Test]
    public void ToString_ContainsBrace()
    {
        var s = ColorMatrix.Identity.ToString();
        Assert.That(s, Does.Contain("{"));
        Assert.That(s, Does.Contain("M00"));
    }

    [Test]
    public void Constructor_StoresAllValues()
    {
        var m = new ColorMatrix(
            1, 2, 3, 4, 5,
            6, 7, 8, 9, 10,
            11, 12, 13, 14, 15,
            16, 17, 18, 19, 20);

        Assert.That(m.M11, Is.EqualTo(1));
        Assert.That(m.M15, Is.EqualTo(5));
        Assert.That(m.M21, Is.EqualTo(6));
        Assert.That(m.M33, Is.EqualTo(13));
        Assert.That(m.M45, Is.EqualTo(20));
    }
}
