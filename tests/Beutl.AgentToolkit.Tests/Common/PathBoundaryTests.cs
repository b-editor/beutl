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

    // A leaf-only check would accept this in-root path while a write follows the link outside the root.
    [Test]
    public void ResolveExistingPath_IntermediateSymlinkedDirectory_FollowsLinkTargetOutsideRoot()
    {
        string outsideRoot = Path.Combine(Path.GetTempPath(), "pb-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        try
        {
            string outsideLeaf = Path.Combine(outsideRoot, "leaf.txt");
            File.WriteAllText(outsideLeaf, "x");

            string link = Path.Combine(_tempRoot, "link");
            CreateDirectorySymlinkOrIgnore(link, outsideRoot);

            string resolved = PathBoundary.ResolveExistingPath(Path.Combine(link, "leaf.txt"));

            Assert.That(
                resolved.StartsWith(_tempRoot, PathComparison.ForCurrentPlatform),
                Is.False,
                $"An intermediate symlinked directory must resolve to its target, not its in-root path. Got: {resolved}");
            Assert.That(resolved, Is.EqualTo(outsideLeaf));
        }
        finally
        {
            Directory.Delete(outsideRoot, true);
        }
    }

    [Test]
    public void ResolveExistingPath_RootedRelativeSymlinkTarget_ResolvesAgainstLinkDirectory()
    {
        string outsideRoot = Path.Combine(Path.GetTempPath(), "pb-rel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        try
        {
            string outsideLeaf = Path.Combine(outsideRoot, "leaf.txt");
            File.WriteAllText(outsideLeaf, "x");

            // A relative symlink target resolves against the link's own directory, not the process CWD.
            string linkDir = Path.Combine(_tempRoot, "links");
            Directory.CreateDirectory(linkDir);
            string link = Path.Combine(linkDir, "link");
            CreateSymbolicLinkOrIgnore(link, outsideLeaf);

            string resolved = PathBoundary.ResolveExistingPath(link);

            Assert.That(resolved, Is.EqualTo(outsideLeaf));
        }
        finally
        {
            Directory.Delete(outsideRoot, true);
        }
    }

    [Test]
    public void ResolveExistingPath_PlainExistingPath_ReturnsCanonicalFullPath()
    {
        string leaf = Path.Combine(_tempRoot, "leaf.txt");
        File.WriteAllText(leaf, "x");

        string resolved = PathBoundary.ResolveExistingPath(leaf);

        Assert.That(resolved, Is.EqualTo(Path.GetFullPath(leaf)));
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

    private static void CreateDirectorySymlinkOrIgnore(string linkPath, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not creatable in this environment: {ex.Message}");
        }
    }
}
