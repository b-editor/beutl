using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

public class VectorTests
{
    [Test]
    public void Constants_HaveExpectedValues()
    {
        Assert.That(Vector.Zero, Is.EqualTo(new Vector(0, 0)));
        Assert.That(Vector.One, Is.EqualTo(new Vector(1, 1)));
        Assert.That(Vector.UnitX, Is.EqualTo(new Vector(1, 0)));
        Assert.That(Vector.UnitY, Is.EqualTo(new Vector(0, 1)));
        Assert.That(Vector.Zero.IsDefault, Is.True);
        Assert.That(Vector.One.IsDefault, Is.False);
    }

    [Test]
    public void Length_ReturnsEuclideanDistance()
    {
        var v = new Vector(3, 4);
        Assert.That(v.Length, Is.EqualTo(5f).Within(1e-5f));
        Assert.That(v.SquaredLength, Is.EqualTo(25f).Within(1e-5f));
    }

    [Test]
    public void Add_Sum_OfComponents()
    {
        var a = new Vector(1, 2);
        var b = new Vector(3, 4);
        Assert.That(a + b, Is.EqualTo(new Vector(4, 6)));
        Assert.That(Vector.Add(a, b), Is.EqualTo(new Vector(4, 6)));
    }

    [Test]
    public void Subtract_Difference_OfComponents()
    {
        var a = new Vector(5, 6);
        var b = new Vector(1, 2);
        Assert.That(a - b, Is.EqualTo(new Vector(4, 4)));
        Assert.That(Vector.Subtract(a, b), Is.EqualTo(new Vector(4, 4)));
    }

    [Test]
    public void Multiply_ScalarFromBothSides_AndDot()
    {
        var v = new Vector(2, 3);
        Assert.That(v * 2f, Is.EqualTo(new Vector(4, 6)));
        Assert.That(2f * v, Is.EqualTo(new Vector(4, 6)));
        Assert.That(Vector.Multiply(v, 2f), Is.EqualTo(new Vector(4, 6)));
        Assert.That(Vector.Multiply(v, new Vector(2, 3)), Is.EqualTo(new Vector(4, 9)));
        Assert.That(v * new Vector(2, 3), Is.EqualTo(2f * 2f + 3f * 3f));
    }

    [Test]
    public void Divide_ByScalarAndVector()
    {
        var v = new Vector(4, 9);
        Assert.That(v / 2f, Is.EqualTo(new Vector(2, 4.5f)));
        Assert.That(Vector.Divide(v, 2f), Is.EqualTo(new Vector(2, 4.5f)));
        Assert.That(Vector.Divide(v, new Vector(2, 3)), Is.EqualTo(new Vector(2, 3)));
    }

    [Test]
    public void Negate_FlipsBothComponents()
    {
        var v = new Vector(1, -2);
        Assert.That(-v, Is.EqualTo(new Vector(-1, 2)));
        Assert.That(Vector.Negate(v), Is.EqualTo(new Vector(-1, 2)));
        Assert.That(v.Negate(), Is.EqualTo(new Vector(-1, 2)));
    }

    [Test]
    public void Dot_AndCross_ProduceExpectedScalars()
    {
        var a = new Vector(1, 2);
        var b = new Vector(3, 4);
        Assert.That(Vector.Dot(a, b), Is.EqualTo(11f));
        Assert.That(Vector.Cross(a, b), Is.EqualTo(-2f));
    }

    [Test]
    public void Normalize_ProducesUnitVector()
    {
        var v = new Vector(3, 4);
        Vector n = v.Normalize();
        Assert.That(n.Length, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(Vector.Normalize(v), Is.EqualTo(n));
    }

    [Test]
    public void WithX_WithY_ReplaceComponentImmutably()
    {
        var v = new Vector(1, 2);
        Assert.That(v.WithX(7), Is.EqualTo(new Vector(7, 2)));
        Assert.That(v.WithY(9), Is.EqualTo(new Vector(1, 9)));
    }

    [Test]
    public void Equality_ComparesByComponents()
    {
        var a = new Vector(1, 2);
        var b = new Vector(1, 2);
        var c = new Vector(1, 3);
        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"x"), Is.False);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void NearlyEquals_TolerantWithinFloatEpsilon()
    {
        var a = new Vector(1f, 2f);
        var b = new Vector(1f + float.Epsilon, 2f - float.Epsilon);
        Assert.That(a.NearlyEquals(b), Is.True);
    }

    [Test]
    public void Deconstruct_ReturnsComponents()
    {
        var v = new Vector(7, 8);
        var (x, y) = v;
        Assert.That(x, Is.EqualTo(7));
        Assert.That(y, Is.EqualTo(8));
    }

    [Test]
    public void ImplicitToPoint_PreservesComponents()
    {
        var v = new Vector(2, 3);
        Point p = (Point)v;
        Assert.That(p.X, Is.EqualTo(2));
        Assert.That(p.Y, Is.EqualTo(3));
    }

    [Test]
    public void ToString_RoundTripsThroughParse()
    {
        var v = new Vector(1.5f, -2.5f);
        string s = v.ToString();
        Vector parsed = Vector.Parse(s);
        Assert.That(parsed, Is.EqualTo(v));
    }

    [Test]
    public void TryParse_ValidAndInvalid()
    {
        Assert.That(Vector.TryParse("3,4", out Vector ok), Is.True);
        Assert.That(ok, Is.EqualTo(new Vector(3, 4)));

        Assert.That(Vector.TryParse("garbage", out Vector bad), Is.False);
        Assert.That(bad, Is.EqualTo(default(Vector)));

        Assert.That(Vector.TryParse("3,4"u8, null, out Vector utf8Ok), Is.True);
        Assert.That(utf8Ok, Is.EqualTo(new Vector(3, 4)));

        Assert.That(Vector.TryParse("garbage"u8, null, out _), Is.False);
    }

    [Test]
    public void TryFormat_Char_AndUtf8_WriteSameContent()
    {
        var v = new Vector(1.5f, 2.5f);
        Span<char> chars = stackalloc char[32];
        Span<byte> bytes = stackalloc byte[32];

        Assert.That(v.TryFormat(chars, out int charsWritten), Is.True);
        Assert.That(v.TryFormat(bytes, out int bytesWritten), Is.True);

        string charText = chars[..charsWritten].ToString();
        string utf8Text = System.Text.Encoding.UTF8.GetString(bytes[..bytesWritten]);
        Assert.That(charText, Is.EqualTo(utf8Text));
    }
}
