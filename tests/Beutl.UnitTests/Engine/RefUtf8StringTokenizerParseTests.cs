using System.Text;
using Beutl.Utilities;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class RefUtf8StringTokenizerParseTests
{
    [Test]
    [TestCase(100, 100)]
    [TestCase(86541478, 1568)]
    [TestCase(546848, 63147864)]
    public void ParseCommaSeparated(int x, int y)
    {
        Span<byte> s = stackalloc byte[1024];
        x.TryFormat(s, out int written);
        s[written] = (byte)',';
        y.TryFormat(s.Slice(written + 1), out _);

        using (var tokenizer = new RefUtf8StringTokenizer(s))
        {
            Assert.That(tokenizer.ReadInt32(), Is.EqualTo(x));
            Assert.That(tokenizer.ReadInt32(), Is.EqualTo(y));
        }
    }

    [Test]
    [TestCase("    1,        2")]
    [TestCase("1        2   ")]
    public void ParseDirty(string s)
    {
        using (var tokenizer = new RefUtf8StringTokenizer(Encoding.UTF8.GetBytes(s)))
        {
            Assert.That(tokenizer.ReadInt32(), Is.EqualTo(1));
            Assert.That(tokenizer.ReadInt32(), Is.EqualTo(2));
        }
    }
}
