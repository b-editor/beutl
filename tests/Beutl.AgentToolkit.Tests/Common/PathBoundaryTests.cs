using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;

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
        TestWorkspace.CreateSymbolicLinkOrIgnore(linkPath, missingTarget, directory: false);

        string resolved = PathBoundary.ResolveDeepestExistingTarget(linkPath);

        Assert.That(
            resolved.StartsWith(_tempRoot, PathBoundary.Comparison),
            Is.False,
            $"A broken symlink must resolve to its target, not its in-root path. Got: {resolved}");
        Assert.That(resolved, Is.EqualTo(Path.GetFullPath(missingTarget)));
    }

    [Test]
    public void ResolveDeepestExistingTarget_BrokenSymlinkWithRelativeTarget_ResolvesAgainstLinkDirectory()
    {
        string linkDir = Path.Combine(_tempRoot, "sub");
        Directory.CreateDirectory(linkDir);
        string linkPath = Path.Combine(linkDir, "escape");
        // A broken link's relative target resolves against the link's own directory, not the process CWD.
        string relativeTarget = Path.Combine("..", "pb-rel-missing-" + Guid.NewGuid().ToString("N"));
        TestWorkspace.CreateSymbolicLinkOrIgnore(linkPath, relativeTarget, directory: false);

        string resolved = PathBoundary.ResolveDeepestExistingTarget(linkPath);

        Assert.That(resolved, Is.EqualTo(Path.GetFullPath(Path.Combine(linkDir, relativeTarget))));
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
            TestWorkspace.CreateSymbolicLinkOrIgnore(link, outsideRoot, directory: true);

            string resolved = PathBoundary.ResolveExistingPath(Path.Combine(link, "leaf.txt"));

            Assert.That(
                resolved.StartsWith(_tempRoot, PathBoundary.Comparison),
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
            TestWorkspace.CreateSymbolicLinkOrIgnore(link, Path.GetRelativePath(linkDir, outsideLeaf), directory: false);

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
}
