using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Beutl.UnitTests.Core;

public class StringHashTests
{
    [Test]
    public void GetMD5Hash_EmptyString_MatchesDirectComputation()
    {
        string hash = "".GetMD5Hash();
        string expected = ComputeExpected("");

        Assert.That(hash, Is.EqualTo(expected));
    }

    [Test]
    public void GetMD5Hash_NonEmptyString_MatchesDirectComputation()
    {
        const string input = "Beutl";
        string hash = input.GetMD5Hash();
        string expected = ComputeExpected(input);

        Assert.That(hash, Is.EqualTo(expected));
    }

    [Test]
    public void GetMD5Hash_DifferentStrings_ProduceDifferentHashes()
    {
        string a = "alpha".GetMD5Hash();
        string b = "beta".GetMD5Hash();

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void GetMD5Hash_SameInputs_ProduceSameHash()
    {
        string a = "stable".GetMD5Hash();
        string b = "stable".GetMD5Hash();

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void GetMD5Hash_OutputLength_Is32HexCharacters()
    {
        string hash = "anything".GetMD5Hash();

        Assert.Multiple(() =>
        {
            Assert.That(hash, Has.Length.EqualTo(32));
            Assert.That(hash, Does.Match("^[0-9A-F]{32}$"));
        });
    }

    private static string ComputeExpected(string s)
    {
        byte[] bytes = MD5.HashData(MemoryMarshal.Cast<char, byte>(s.AsSpan()));
        return Convert.ToHexString(bytes);
    }
}
