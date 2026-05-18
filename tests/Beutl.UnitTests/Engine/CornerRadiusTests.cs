using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class CornerRadiusTests
{
    [Test]
    public void Constructor_Uniform_AppliesToAllCorners()
    {
        var r = new CornerRadius(5);
        Assert.Multiple(() =>
        {
            Assert.That(r.TopLeft, Is.EqualTo(5));
            Assert.That(r.TopRight, Is.EqualTo(5));
            Assert.That(r.BottomLeft, Is.EqualTo(5));
            Assert.That(r.BottomRight, Is.EqualTo(5));
            Assert.That(r.IsUniform, Is.True);
            Assert.That(r.IsEmpty, Is.False);
        });
    }

    [Test]
    public void Constructor_TopBottom_AppliesToTopAndBottomPairs()
    {
        var r = new CornerRadius(top: 1, bottom: 2);
        Assert.Multiple(() =>
        {
            Assert.That(r.TopLeft, Is.EqualTo(1));
            Assert.That(r.TopRight, Is.EqualTo(1));
            Assert.That(r.BottomLeft, Is.EqualTo(2));
            Assert.That(r.BottomRight, Is.EqualTo(2));
        });
    }

    [Test]
    public void Constructor_Full_StoresAllComponents()
    {
        var r = new CornerRadius(1, 2, 3, 4);
        Assert.Multiple(() =>
        {
            Assert.That(r.TopLeft, Is.EqualTo(1));
            Assert.That(r.TopRight, Is.EqualTo(2));
            Assert.That(r.BottomRight, Is.EqualTo(3));
            Assert.That(r.BottomLeft, Is.EqualTo(4));
            Assert.That(r.IsUniform, Is.False);
        });
    }

    [Test]
    public void Default_IsEmptyAndUniform()
    {
        var r = default(CornerRadius);
        Assert.Multiple(() =>
        {
            Assert.That(r.IsEmpty, Is.True);
            Assert.That(r.IsUniform, Is.True);
        });
    }

    [Test]
    public void Equality_Operators_AndHashCode()
    {
        var a = new CornerRadius(1, 2, 3, 4);
        var b = new CornerRadius(1, 2, 3, 4);
        var c = new CornerRadius(0, 2, 3, 4);

        Assert.Multiple(() =>
        {
            Assert.That(a == b, Is.True);
            Assert.That(a != c, Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((object)"foo"), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.GetHashCode(), Is.Not.EqualTo(c.GetHashCode()));
        });
    }

    [Test]
    public void ToString_FormatsAllCornersInOrder()
    {
        var r = new CornerRadius(1, 2, 3, 4);
        Assert.That(r.ToString(), Is.EqualTo("1,2,3,4"));
    }

    [Test]
    public void Parse_SingleValue_ProducesUniformRadius()
    {
        var r = CornerRadius.Parse("5");
        Assert.That(r, Is.EqualTo(new CornerRadius(5)));
    }

    [Test]
    public void Parse_TwoValues_ProducesTopBottomRadius()
    {
        var r = CornerRadius.Parse("1,2");
        Assert.That(r, Is.EqualTo(new CornerRadius(1, 2)));
    }

    [Test]
    public void Parse_FourValues_ProducesFullRadius()
    {
        var r = CornerRadius.Parse("1,2,3,4");
        Assert.That(r, Is.EqualTo(new CornerRadius(1, 2, 3, 4)));
    }

    [Test]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<FormatException>(() => CornerRadius.Parse(""));
    }

    [Test]
    public void TryParse_ValidAndInvalid()
    {
        Assert.That(CornerRadius.TryParse("1,2,3,4", out CornerRadius ok), Is.True);
        Assert.That(ok, Is.EqualTo(new CornerRadius(1, 2, 3, 4)));

        Assert.That(CornerRadius.TryParse("garbage", out CornerRadius bad), Is.False);
        Assert.That(bad, Is.EqualTo(default(CornerRadius)));

        Assert.That(CornerRadius.TryParse("5".AsSpan(), out CornerRadius span), Is.True);
        Assert.That(span, Is.EqualTo(new CornerRadius(5)));
    }

    [Test]
    public void With_ReplaceIndividualCorners()
    {
        var r = new CornerRadius(1, 2, 3, 4);

        Assert.Multiple(() =>
        {
            Assert.That(r.WithTopLeft(9), Is.EqualTo(new CornerRadius(9, 2, 3, 4)));
            Assert.That(r.WithTopRight(9), Is.EqualTo(new CornerRadius(1, 9, 3, 4)));
            Assert.That(r.WithBottomRight(9), Is.EqualTo(new CornerRadius(1, 2, 9, 4)));
            Assert.That(r.WithBottomLeft(9), Is.EqualTo(new CornerRadius(1, 2, 3, 9)));
            Assert.That(r.WithTop(9), Is.EqualTo(new CornerRadius(9, 9, 3, 4)));
            Assert.That(r.WithBottom(9), Is.EqualTo(new CornerRadius(1, 2, 9, 9)));
        });
    }

    [Test]
    public void TupleConvertible_RoundTripPreservesOrder()
    {
        Assert.That(GetTupleLength<CornerRadius>(), Is.EqualTo(4));

        var original = new CornerRadius(1, 2, 3, 4);
        var tuple = new float[4];
        ConvertTo<CornerRadius>(original, tuple);

        Assert.Multiple(() =>
        {
            Assert.That(tuple[0], Is.EqualTo(1));
            Assert.That(tuple[1], Is.EqualTo(2));
            Assert.That(tuple[2], Is.EqualTo(3));
            Assert.That(tuple[3], Is.EqualTo(4));
        });

        var restored = ConvertFrom<CornerRadius>(tuple);
        Assert.That(restored, Is.EqualTo(original));
    }

    private static int GetTupleLength<T>()
        where T : struct, ITupleConvertible<T, float> => T.TupleLength;

    private static void ConvertTo<T>(T value, Span<float> tuple)
        where T : struct, ITupleConvertible<T, float> => T.ConvertTo(value, tuple);

    private static T ConvertFrom<T>(Span<float> tuple)
        where T : struct, ITupleConvertible<T, float>
    {
        T.ConvertFrom(tuple, out T value);
        return value;
    }
}
