using System.Security.Cryptography;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public sealed class Sha256ChainingStateTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(55)]
    [TestCase(56)]
    [TestCase(63)]
    [TestCase(64)]
    [TestCase(65)]
    [TestCase(119)]
    [TestCase(120)]
    [TestCase(1000)]
    [TestCase(1024 * 1024 + 7)]
    public void Finishes_with_the_digest_of_every_byte(int length)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(length);
        int whole = length / 2 / 64 * 64;
        var hash = new Sha256ChainingState();
        hash.Append(bytes.AsSpan(0, whole));
        Assert.That(hash.Finish(bytes.AsSpan(whole)), Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
    }

    [Test]
    public void Resumes_from_the_state_it_reports()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(4096 + 17);
        var first = new Sha256ChainingState();
        first.Append(bytes.AsSpan(0, 2048));
        Sha256ChainingState resumed = Sha256ChainingState.Resume(first.ToString(), 2048);
        Assert.Multiple(() =>
        {
            Assert.That(resumed.Length, Is.EqualTo(2048));
            Assert.That(resumed.Finish(bytes.AsSpan(2048)), Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
        });
    }

    [Test]
    public void Writes_the_state_as_hosted_Git_does()
    {
        // Computed by hosted Git's own resumable SHA-256 over these 128 bytes.
        byte[] bytes = Enumerable.Range(0, 128).Select(i => (byte)(i * 31)).ToArray();
        var hash = new Sha256ChainingState();
        hash.Append(bytes);
        Assert.That(hash.ToString(), Is.EqualTo("9fa5801a823a5e0c00a91b8fe775f172c4ac044a0cd8419d9f1a927c46590b89"));
    }

    [TestCase("9FA5801A823A5E0C00A91B8FE775F172C4AC044A0CD8419D9F1A927C46590B89", 128)]
    [TestCase("9fa5801a823a5e0c00a91b8fe775f172c4ac044a0cd8419d9f1a927c46590b8", 128)]
    [TestCase("9fa5801a823a5e0c00a91b8fe775f172c4ac044a0cd8419d9f1a927c46590b8g", 128)]
    [TestCase("9fa5801a823a5e0c00a91b8fe775f172c4ac044a0cd8419d9f1a927c46590b89", 100)]
    public void Rejects_a_state_hosted_Git_could_not_have_sent(string state, long length)
    {
        Assert.Throws<FormatException>(() => Sha256ChainingState.Resume(state, length));
    }

    [Test]
    public void Takes_only_whole_blocks_before_the_end()
    {
        Assert.Throws<ArgumentException>(() => new Sha256ChainingState().Append(new byte[65]));
    }
}
