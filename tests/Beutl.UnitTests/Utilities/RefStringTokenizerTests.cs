using System.Globalization;
using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class RefStringTokenizerTests
{
    [Test]
    [TestCase("123", 123)]
    [TestCase("0", 0)]
    [TestCase("-123", -123)]
    public void ReadInt32_ShouldReturnCorrectValueForValidInteger(string input, int expected)
    {
        var tokenizer = new RefStringTokenizer(input.AsSpan(), CultureInfo.InvariantCulture);
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
            var tokenizer = new RefStringTokenizer(input.AsSpan(), CultureInfo.InvariantCulture);
            tokenizer.ReadInt32();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    [TestCase("123.45", 123.45)]
    [TestCase("0", 0)]
    [TestCase("-123", -123)]
    public void ReadDouble_ShouldReturnCorrectValueForValidDouble(string input, double expected)
    {
        var tokenizer = new RefStringTokenizer(input.AsSpan(), CultureInfo.InvariantCulture);
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
            var tokenizer = new RefStringTokenizer(input.AsSpan(), CultureInfo.InvariantCulture);
            tokenizer.ReadDouble();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    [TestCase("123.45", 123.45f)]
    [TestCase("0", 0f)]
    [TestCase("-123", -123f)]
    public void ReadSingle_ShouldReturnCorrectValueForValidFloat(string input, float expected)
    {
        var tokenizer = new RefStringTokenizer(input.AsSpan(), CultureInfo.InvariantCulture);
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
            var tokenizer = new RefStringTokenizer(input.AsSpan(), CultureInfo.InvariantCulture);
            tokenizer.ReadSingle();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    [TestCase("hello")]
    public void ReadString_ShouldReturnCorrectValueForValidString(string input)
    {
        var tokenizer = new RefStringTokenizer(input.AsSpan(), CultureInfo.InvariantCulture);
        var value = tokenizer.ReadString();

        Assert.That(value.ToString(), Is.EqualTo(input));
    }

    [Test]
    [TestCase("")]
    [TestCase("   ")]
    public void ReadString_ShouldThrowFormatExceptionForInvalidString(string input)
    {
        Assert.That(() =>
        {
            var tokenizer = new RefStringTokenizer(input.AsSpan(), CultureInfo.InvariantCulture);
            tokenizer.ReadString();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Dispose_ShouldThrowFormatExceptionIfNotAllTokensRead()
    {
        Assert.That(() =>
        {
            // ref structなのでラムダ式内で初期化
            var tokenizer = new RefStringTokenizer("123,456".AsSpan(), CultureInfo.InvariantCulture);
            tokenizer.Dispose();
        }, Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Dispose_ShouldNotThrowIfAllTokensRead()
    {
        Assert.That(() =>
        {
            // ref structなのでラムダ式内で初期化
            var tokenizer = new RefStringTokenizer("123".AsSpan(), CultureInfo.InvariantCulture);
            tokenizer.TryReadInt32(out _);

            tokenizer.Dispose();
        }, Throws.Nothing);
    }
}
