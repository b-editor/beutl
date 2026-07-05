using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Tests.Common;

public sealed class PathBoundaryTests
{
    private string _tempRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "path-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }

    [Test]
    public void ResolveDeepestExistingTarget_BrokenSymlink_FollowsLinkTargetOutsideRoot()
    {
        string linkPath = Path.Combine(_tempRoot, "escape");
        string missingTarget = Path.Combine(Path.GetTempPath(), "pb-target-" + Guid.NewGuid().ToString("N"));
        CreateSymbolicLinkOrIgnore(linkPath, missingTarget);

        string resolved = PathBoundary.ResolveDeepestExistingTarget(linkPath);

        Assert.That(
            resolved.StartsWith(_tempRoot, PathComparison.ForCurrentPlatform),
            Is.False,
            $"A broken symlink must resolve to its target, not its in-root path. Got: {resolved}");
        Assert.That(resolved, Is.EqualTo(Path.GetFullPath(missingTarget)));
    }

    [Test]
    public void ResolveDeepestExistingTarget_PlainMissingPath_ReappendsRemainderUnderRoot()
    {
        string missing = Path.Combine(_tempRoot, "a", "b", "c.txt");

        string resolved = PathBoundary.ResolveDeepestExistingTarget(missing);

        Assert.That(resolved, Is.EqualTo(Path.GetFullPath(missing)));
    }

    private static void CreateSymbolicLinkOrIgnore(string linkPath, string target)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not creatable in this environment: {ex.Message}");
        }
    }
}
