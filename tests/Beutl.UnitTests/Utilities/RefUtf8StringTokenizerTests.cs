using System.Globalization;
using System.Text;
using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class RefUtf8StringTokenizerTests
{
    [Test]
    [TestCase("123", 123)]
    [TestCase("0", 0)]
    [TestCase("-123", -123)]
    public void ReadInt32_ShouldReturnCorrectValueForValidInteger(string input, int expected)
    {
        Span<byte> inputBytes = Encoding.UTF8.GetBytes(input);
        var tokenizer = new RefUtf8StringTokenizer(inputBytes, CultureInfo.InvariantCulture);
        int value = tokenizer.ReadInt32();

        Assert.That(value, Is.EqualTo(expected));
    }

    [Test]
    [TestCase("abc")]
    [TestCase("123.45")]
    [TestCase("")]
    [TestCase("   ")]
    public void ReadInt32_ShouldThrowFormatExceptionForInvalidInteger(string input)
    {
        Assert.That(() =>
        {
            Span<byte> inputBytes = Encoding.UTF8.GetBytes(input);
            var tokenizer = new RefUtf8StringTokenizer(inputBytes, CultureInfo.InvariantCulture);
            tokenizer.ReadInt32();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    [TestCase("123.45", 123.45)]
    [TestCase("0", 0)]
    [TestCase("-123", -123)]
    public void ReadDouble_ShouldReturnCorrectValueForValidDouble(string input, double expected)
    {
        Span<byte> inputBytes = Encoding.UTF8.GetBytes(input);
        var tokenizer = new RefUtf8StringTokenizer(inputBytes, CultureInfo.InvariantCulture);
        double value = tokenizer.ReadDouble();

        Assert.That(value, Is.EqualTo(expected).Within(0.001f));
    }

    [Test]
    [TestCase("abc")]
    [TestCase("")]
    [TestCase("   ")]
    public void ReadDouble_ShouldThrowFormatExceptionForInvalidDouble(string input)
    {
        Assert.That(() =>
        {
            Span<byte> inputBytes = Encoding.UTF8.GetBytes(input);
            var tokenizer = new RefUtf8StringTokenizer(inputBytes, CultureInfo.InvariantCulture);
            tokenizer.ReadDouble();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    [TestCase("123.45", 123.45f)]
    [TestCase("0", 0f)]
    [TestCase("-123", -123f)]
    public void ReadSingle_ShouldReturnCorrectValueForValidFloat(string input, float expected)
    {
        Span<byte> inputBytes = Encoding.UTF8.GetBytes(input);
        var tokenizer = new RefUtf8StringTokenizer(inputBytes, CultureInfo.InvariantCulture);
        float value = tokenizer.ReadSingle();

        Assert.That(value, Is.EqualTo(expected).Within(0.001f));
    }

    [Test]
    [TestCase("abc")]
    [TestCase("")]
    [TestCase("   ")]
    public void ReadSingle_ShouldThrowFormatExceptionForInvalidFloat(string input)
    {
        Assert.That(() =>
        {
            Span<byte> inputBytes = Encoding.UTF8.GetBytes(input);
            var tokenizer = new RefUtf8StringTokenizer(inputBytes, CultureInfo.InvariantCulture);
            tokenizer.ReadSingle();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    [TestCase("hello")]
    public void ReadString_ShouldReturnCorrectValueForValidString(string input)
    {
        Span<byte> inputBytes = Encoding.UTF8.GetBytes(input);
        var tokenizer = new RefUtf8StringTokenizer(inputBytes, CultureInfo.InvariantCulture);
        var value = tokenizer.ReadString();

        Assert.That(Encoding.UTF8.GetString(value), Is.EqualTo(input));
    }

    [Test]
    [TestCase("")]
    [TestCase("   ")]
    public void ReadString_ShouldThrowFormatExceptionForInvalidString(string input)
    {
        Assert.That(() =>
        {
            Span<byte> inputBytes = Encoding.UTF8.GetBytes(input);
            var tokenizer = new RefUtf8StringTokenizer(inputBytes, CultureInfo.InvariantCulture);
            tokenizer.ReadString();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Dispose_ShouldThrowFormatExceptionIfNotAllTokensRead()
    {
        Assert.That(() =>
        {
            // ref structなのでラムダ式内で初期化
            var tokenizer = new RefUtf8StringTokenizer("123,456"u8, CultureInfo.InvariantCulture);
            tokenizer.Dispose();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Dispose_ShouldNotThrowIfAllTokensRead()
    {
        Assert.That(() =>
        {
            // ref structなのでラムダ式内で初期化
            var tokenizer = new RefUtf8StringTokenizer("123"u8, CultureInfo.InvariantCulture);
            tokenizer.TryReadInt32(out _);

            tokenizer.Dispose();
        }, Throws.Nothing);
    }

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
