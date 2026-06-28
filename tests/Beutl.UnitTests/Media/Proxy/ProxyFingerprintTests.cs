using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class ProxyFingerprintTests
{
    [Test]
    public void Equality_UsesPathSizeAndMtime()
    {
        var mtime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        string sourcePath = CreatePath("source.mov");
        string otherPath = CreatePath("other.mov");
        var baseline = new ProxyFingerprint(sourcePath, 123, mtime);

        Assert.Multiple(() =>
        {
            Assert.That(new ProxyFingerprint(sourcePath, 123, mtime), Is.EqualTo(baseline));
            Assert.That(new ProxyFingerprint(otherPath, 123, mtime), Is.Not.EqualTo(baseline));
            Assert.That(new ProxyFingerprint(sourcePath, 456, mtime), Is.Not.EqualTo(baseline));
            Assert.That(new ProxyFingerprint(sourcePath, 123, mtime.AddTicks(1)), Is.Not.EqualTo(baseline));
        });
    }

    [Test]
    public void TryFromFile_MissingFile_ReturnsFalse()
    {
        string missing = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".mov");

        bool result = ProxyFingerprint.TryFromFile(missing, out ProxyFingerprint fingerprint);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(fingerprint, Is.EqualTo(default(ProxyFingerprint)));
        });
    }

    [Test]
    public void FromFile_NormalizesToAbsolutePath()
    {
        string dir = TestContext.CurrentContext.WorkDirectory;
        string path = Path.Combine(dir, Guid.NewGuid() + ".mov");
        File.WriteAllBytes(path, [1, 2, 3]);

        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(path);

        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathFullyQualified(fingerprint.AbsolutePath), Is.True);
            Assert.That(fingerprint.FileSizeBytes, Is.EqualTo(3));
            Assert.That(fingerprint.MtimeUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    private static string CreatePath(string fileName)
    {
        return Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName);
    }
}
