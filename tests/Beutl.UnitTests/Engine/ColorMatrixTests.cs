using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

public class ColorMatrixTests
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
    public void CreateFromSpan_TooShort_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            Span<float> span = stackalloc float[10];
            ColorMatrix.CreateFromSpan(span);
        });
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
