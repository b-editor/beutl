using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class ProxyPathUtilitiesTests
{
    [Test]
    public void IsGeneratedProxyFinalPath_AcceptsHashDirWithPresetName()
    {
        string root = CreateRoot();
        string hashDir = new('a', 64);
        string path = Path.Combine(root, hashDir, "quarter.mp4");

        Assert.That(ProxyPathUtilities.IsGeneratedProxyFinalPath(root, path), Is.True);
    }

    [Test]
    public void IsGeneratedProxyFinalPath_RejectsNonProxyShapedFiles()
    {
        string root = CreateRoot();
        string hashDir = new('a', 64);
        string unrelatedTopLevel = Path.Combine(root, "archive.mp4");
        string wrongNameInHashDir = Path.Combine(root, hashDir, "render-final.mp4");
        string tempInHashDir = Path.Combine(root, hashDir, "quarter.abc123.tmp.mp4");

        Assert.Multiple(() =>
        {
            Assert.That(ProxyPathUtilities.IsGeneratedProxyFinalPath(root, unrelatedTopLevel), Is.False);
            Assert.That(ProxyPathUtilities.IsGeneratedProxyFinalPath(root, wrongNameInHashDir), Is.False);
            Assert.That(ProxyPathUtilities.IsGeneratedProxyFinalPath(root, tempInHashDir), Is.False);
        });
    }

    [Test]
    public void IsGeneratedProxyFinalPath_RejectsNestedHashDir()
    {
        string root = CreateRoot();
        string hashDir = new('a', 64);
        string path = Path.Combine(root, "nested", hashDir, "quarter.mp4");

        Assert.That(ProxyPathUtilities.IsGeneratedProxyFinalPath(root, path), Is.False);
    }

    [Test]
    public void IsGeneratedProxyFinalPath_AndTempPath_AreMutuallyExclusive()
    {
        string root = CreateRoot();
        string hashDir = new('a', 64);
        string finalPath = Path.Combine(root, hashDir, "quarter.mp4");
        string tempPath = Path.Combine(root, hashDir, $"quarter.{Guid.NewGuid():N}.tmp.mp4");

        Assert.Multiple(() =>
        {
            Assert.That(ProxyPathUtilities.IsGeneratedProxyFinalPath(root, finalPath), Is.True);
            Assert.That(ProxyPathUtilities.IsGeneratedProxyTempPath(root, finalPath), Is.False);
            Assert.That(ProxyPathUtilities.IsGeneratedProxyFinalPath(root, tempPath), Is.False);
            Assert.That(ProxyPathUtilities.IsGeneratedProxyTempPath(root, tempPath), Is.True);
        });
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
