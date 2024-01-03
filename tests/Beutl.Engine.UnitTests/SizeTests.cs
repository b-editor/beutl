using System.Globalization;
using System.Text;

using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Beutl.Graphics.UnitTests;

public class SizeTests
{
    [Test]
    public void Parse()
    {
        const string str = "1920,1080";
        var size = Size.Parse(str);

        ClassicAssert.AreEqual(new Size(1920, 1080), size);
    }
    
    [Test]
    public void ParseSpan()
    {
        const string str = "1920,1080";
        var size = Size.Parse(str.AsSpan());

        ClassicAssert.AreEqual(new Size(1920, 1080), size);
    }

    [Test]
    public void ParseWithProvider()
    {
        const string str = "1920;1080";
        var size = Size.Parse(str, CultureInfo.GetCultureInfo("fr"));

        ClassicAssert.AreEqual(new Size(1920, 1080), size);
    }

    [Test]
    public void ParseUtf8()
    {
        ReadOnlySpan<byte> str = "1920,1080"u8;
        var size = Size.Parse(str);

        ClassicAssert.AreEqual(new Size(1920, 1080), size);
    }

    [Test]
    public void ParseUtf8WithProvider()
    {
        ReadOnlySpan<byte> str = "1920;1080"u8;
        var size = Size.Parse(str, CultureInfo.GetCultureInfo("fr"));

        ClassicAssert.AreEqual(new Size(1920, 1080), size);
    }

    [Test]
    public void FormatToSpan()
    {
        const string str = "1920, 1080";
        var size = new Size(1920, 1080);
        Span<char> s = stackalloc char[64];

        size.TryFormat(s, out int written);
        ClassicAssert.AreEqual(str, s.Slice(0, written).ToString());
    }

    [Test]
    public void FormatToUtf8()
    {
        const string str = "1920, 1080";
        var size = new Size(1920, 1080);
        Span<byte> s = stackalloc byte[64];

        size.TryFormat(s, out int written);

        ClassicAssert.AreEqual(str, Encoding.UTF8.GetString(s.Slice(0, written)));
    }

    [Test]
    public void Deflate()
    {
        var size = new Size(1920, 1080);
        var thickness = new Thickness(10, 15);

        size = size.Deflate(thickness);

        ClassicAssert.AreEqual(new Size(1900, 1050), size);
    }

    [Test]
    public void Inflate()
    {
        var size = new Size(1920, 1080);
        var thickness = new Thickness(15, 10);

        size = size.Inflate(thickness);

        ClassicAssert.AreEqual(new Size(1950, 1100), size);
    }
}
