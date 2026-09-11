namespace Beutl.UnitTests.Core;

public sealed class PathBoundaryTests
{
    [Test]
    public void IsPathInsideRoot_DoesNotFoldNonexistentPathComponents()
    {
        string parent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.That(PathBoundary.IsPathInsideRoot(Path.Combine(parent, "Project"),
            Path.Combine(parent, "project", "sidecar.json")), Is.False);
    }

    [Test]
    public void IsPathInsideRoot_FollowsTheActualVolumeCaseRules()
    {
        string parent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string root = Path.Combine(parent, "Project");
        Directory.CreateDirectory(root);
        try
        {
            string alternate = Path.Combine(parent, "project");
            bool sameDirectory = Directory.Exists(alternate);
            if (!sameDirectory)
                Directory.CreateDirectory(alternate);
            Assert.That(PathBoundary.IsPathInsideRoot(root, Path.Combine(alternate, "sidecar.json")), Is.EqualTo(sameDirectory));
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Test]
    public void IsPathInsideRoot_RejectsSymlinkEscapeAndAcceptsRootAlias()
    {
        string parent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string root = Path.Combine(parent, "project");
        string outside = Path.Combine(parent, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "escape"), outside);
            string alias = Path.Combine(parent, "alias");
            Directory.CreateSymbolicLink(alias, root);
            Assert.Multiple(() =>
            {
                Assert.That(PathBoundary.IsPathInsideRoot(root, Path.Combine(root, "escape", "new.json")), Is.False);
                Assert.That(PathBoundary.IsPathInsideRoot(root, Path.Combine(alias, "new.json")), Is.True);
                Assert.That(PathBoundary.ResolveExistingPath(alias), Is.EqualTo(PathBoundary.ResolveExistingPath(root)));
            });
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Creating symlinks requires Windows developer mode or elevation.");
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }
}
