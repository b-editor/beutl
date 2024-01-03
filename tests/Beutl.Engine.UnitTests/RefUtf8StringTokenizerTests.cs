using System.Text;

using Beutl.Utilities;

using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Beutl.Engine.UnitTests;

public class RefUtf8StringTokenizerTests
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
            ClassicAssert.AreEqual(x, tokenizer.ReadInt32());
            ClassicAssert.AreEqual(y, tokenizer.ReadInt32());
        }
    }

    [Test]
    [TestCase("    1,        2")]
    [TestCase("1        2   ")]
    public void ParseDirty(string s)
    {
        using (var tokenizer = new RefUtf8StringTokenizer(Encoding.UTF8.GetBytes(s)))
        {
            ClassicAssert.AreEqual(1, tokenizer.ReadInt32());
            ClassicAssert.AreEqual(2, tokenizer.ReadInt32());
        }
    }
}
