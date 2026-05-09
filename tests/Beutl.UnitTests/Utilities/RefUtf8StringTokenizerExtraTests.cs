using System.Globalization;
using System.Text;
using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class RefUtf8StringTokenizerExtraTests
{
    [Test]
    public void ReadInt32_MaxKeyword_ReturnsIntMax()
    {
        var t = new RefUtf8StringTokenizer("Max"u8, CultureInfo.InvariantCulture);
        Assert.That(t.ReadInt32(), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void ReadInt32_MinKeyword_ReturnsIntMin()
    {
        var t = new RefUtf8StringTokenizer("min"u8, CultureInfo.InvariantCulture);
        Assert.That(t.ReadInt32(), Is.EqualTo(int.MinValue));
    }

    [Test]
    public void ReadDouble_MaxKeyword_ReturnsDoubleMax()
    {
        var t = new RefUtf8StringTokenizer("MAX"u8, CultureInfo.InvariantCulture);
        Assert.That(t.ReadDouble(), Is.EqualTo(double.MaxValue));
    }

    [Test]
    public void ReadSingle_MaxKeyword_ReturnsFloatMax()
    {
        var t = new RefUtf8StringTokenizer("Max"u8, CultureInfo.InvariantCulture);
        Assert.That(t.ReadSingle(), Is.EqualTo(float.MaxValue));
    }

    [Test]
    public void Multiple_Tokens_AreSeparatedByCommaWithSpaces()
    {
        var t = new RefUtf8StringTokenizer("1, 2 ,3"u8, CultureInfo.InvariantCulture);
        Assert.That(t.ReadInt32(), Is.EqualTo(1));
        Assert.That(t.ReadInt32(), Is.EqualTo(2));
        Assert.That(t.ReadInt32(), Is.EqualTo(3));
    }

    [Test]
    public void DoubleSeparator_ThrowsFormatException()
    {
        Assert.That(() =>
        {
            var t = new RefUtf8StringTokenizer("1,,2"u8, CultureInfo.InvariantCulture);
            t.ReadInt32();
            t.ReadInt32();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    public void TrailingSeparator_ThrowsFormatException()
    {
        Assert.That(() =>
        {
            var t = new RefUtf8StringTokenizer("1,"u8, CultureInfo.InvariantCulture);
            t.ReadInt32();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    public void CurrentToken_ReturnsLastReadSpan()
    {
        var t = new RefUtf8StringTokenizer("hello,world"u8, CultureInfo.InvariantCulture);
        ReadOnlySpan<byte> first = t.ReadString();
        Assert.That(Encoding.UTF8.GetString(first), Is.EqualTo("hello"));
        Assert.That(Encoding.UTF8.GetString(t.CurrentToken), Is.EqualTo("hello"));
    }

    [Test]
    public void TryReadDouble_OnEmpty_ReturnsFalse()
    {
        var t = new RefUtf8StringTokenizer(""u8, CultureInfo.InvariantCulture);
        Assert.That(t.TryReadDouble(out double value), Is.False);
        Assert.That(value, Is.EqualTo(0d));
    }

    [Test]
    public void TryReadSingle_OnEmpty_ReturnsFalse()
    {
        var t = new RefUtf8StringTokenizer(""u8, CultureInfo.InvariantCulture);
        Assert.That(t.TryReadSingle(out float value), Is.False);
        Assert.That(value, Is.EqualTo(0f));
    }
}
