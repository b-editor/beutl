using System.Globalization;
using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class RefStringTokenizerExtraTests
{
    [Test]
    public void ReadInt32_MaxKeyword_ReturnsIntMax()
    {
        var t = new RefStringTokenizer("Max".AsSpan(), CultureInfo.InvariantCulture);
        Assert.That(t.ReadInt32(), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void ReadInt32_MinKeyword_ReturnsIntMin()
    {
        var t = new RefStringTokenizer("min".AsSpan(), CultureInfo.InvariantCulture);
        Assert.That(t.ReadInt32(), Is.EqualTo(int.MinValue));
    }

    [Test]
    public void ReadDouble_MaxAndMin_ReturnsExtremes()
    {
        var max = new RefStringTokenizer("MAX".AsSpan(), CultureInfo.InvariantCulture);
        Assert.That(max.ReadDouble(), Is.EqualTo(double.MaxValue));

        var min = new RefStringTokenizer("min".AsSpan(), CultureInfo.InvariantCulture);
        Assert.That(min.ReadDouble(), Is.EqualTo(double.MinValue));
    }

    [Test]
    public void ReadSingle_MaxAndMin_ReturnsExtremes()
    {
        var max = new RefStringTokenizer("Max".AsSpan(), CultureInfo.InvariantCulture);
        Assert.That(max.ReadSingle(), Is.EqualTo(float.MaxValue));

        var min = new RefStringTokenizer("MIN".AsSpan(), CultureInfo.InvariantCulture);
        Assert.That(min.ReadSingle(), Is.EqualTo(float.MinValue));
    }

    [Test]
    public void TryReadInt32_InvalidKeyword_ReturnsFalse()
    {
        var t = new RefStringTokenizer("notanint".AsSpan(), CultureInfo.InvariantCulture);
        Assert.That(t.TryReadInt32(out int value), Is.False);
        Assert.That(value, Is.EqualTo(0));
    }

    [Test]
    public void Multiple_Tokens_AreSeparatedByCommaWithSpaces()
    {
        var t = new RefStringTokenizer("1, 2 ,3".AsSpan(), CultureInfo.InvariantCulture);
        Assert.That(t.ReadInt32(), Is.EqualTo(1));
        Assert.That(t.ReadInt32(), Is.EqualTo(2));
        Assert.That(t.ReadInt32(), Is.EqualTo(3));
    }

    [Test]
    public void DoubleSeparator_ThrowsFormatException()
    {
        Assert.That(() =>
        {
            var t = new RefStringTokenizer("1,,2".AsSpan(), CultureInfo.InvariantCulture);
            t.ReadInt32();
            t.ReadInt32();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    public void TrailingSeparator_ThrowsFormatException()
    {
        Assert.That(() =>
        {
            var t = new RefStringTokenizer("1,".AsSpan(), CultureInfo.InvariantCulture);
            t.ReadInt32();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    public void CurrentToken_ReturnsLastReadSpan()
    {
        var t = new RefStringTokenizer("hello,world".AsSpan(), CultureInfo.InvariantCulture);
        ReadOnlySpan<char> first = t.ReadString();
        Assert.That(first.ToString(), Is.EqualTo("hello"));
        Assert.That(t.CurrentToken.ToString(), Is.EqualTo("hello"));
    }

    [Test]
    public void TryReadDouble_OnEmpty_ReturnsFalse()
    {
        var t = new RefStringTokenizer("".AsSpan(), CultureInfo.InvariantCulture);
        Assert.That(t.TryReadDouble(out double value), Is.False);
        Assert.That(value, Is.EqualTo(0d));
    }

    [Test]
    public void TryReadSingle_OnEmpty_ReturnsFalse()
    {
        var t = new RefStringTokenizer("".AsSpan(), CultureInfo.InvariantCulture);
        Assert.That(t.TryReadSingle(out float value), Is.False);
        Assert.That(value, Is.EqualTo(0f));
    }
}
