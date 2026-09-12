using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

/// <summary>
/// Git tracks a symbolic link as the link entry itself, so the tracked path of a project file is its own
/// entry name inside its canonical parent directory. These tests pin that rule for the path
/// <see cref="GitCliVersionControlService"/> hands to <c>ls-tree</c> and hook-tree mode lookups.
/// </summary>
[TestFixture]
public sealed class TrackedProjectFilePathTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "beutl-tracked-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Test]
    public void ProjectFileSymbolicLink_KeepsItsOwnEntryName()
    {
        RequireSymbolicLinks();
        string target = Path.Combine(_root, "project-target.bep");
        File.WriteAllText(target, "{}");
        string link = Path.Combine(_root, "project.bep");
        File.CreateSymbolicLink(link, "project-target.bep");

        string tracked = GitCliVersionControlService.GetRepositoryRelativeProjectFilePath(
            new RepositoryInfo(_root, _root),
            link);

        Assert.That(tracked, Is.EqualTo("project.bep"));
    }

    [Test]
    public void DirectoryAliasInsideTheRoot_ResolvesToTheTrackedDirectory()
    {
        RequireSymbolicLinks();
        string sub = Path.Combine(_root, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "project.bep"), "{}");
        Directory.CreateSymbolicLink(Path.Combine(_root, "alias"), "sub");

        // Git stores "alias" as a symbolic-link blob, not a tree, so the only tracked project entry
        // is sub/project.bep even when the project was opened through the alias.
        string tracked = GitCliVersionControlService.GetRepositoryRelativeProjectFilePath(
            new RepositoryInfo(_root, _root),
            Path.Combine(_root, "alias", "project.bep"));

        Assert.That(tracked, Is.EqualTo("sub/project.bep"));
    }

    [Test]
    public void AliasOfTheProjectRoot_YieldsTheRootRelativeName()
    {
        RequireSymbolicLinks();
        File.WriteAllText(Path.Combine(_root, "project.bep"), "{}");
        string outside = Path.Combine(Path.GetTempPath(), "beutl-tracked-path-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            string aliasRoot = Path.Combine(outside, "link");
            Directory.CreateSymbolicLink(aliasRoot, _root);

            string tracked = GitCliVersionControlService.GetRepositoryRelativeProjectFilePath(
                new RepositoryInfo(_root, _root),
                Path.Combine(aliasRoot, "project.bep"));

            Assert.That(tracked, Is.EqualTo("project.bep"));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public void CallerSpelling_IsNormalizedToTheOnDiskEntry()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        File.WriteAllText(projectFile, "{}");
        string variant = Path.Combine(_root, "PROJECT.BEP");
        if (!File.Exists(variant))
        {
            Assert.Ignore("This case needs a case-insensitive volume.");
        }

        string tracked = GitCliVersionControlService.GetRepositoryRelativeProjectFilePath(
            new RepositoryInfo(_root, _root),
            variant);

        Assert.That(tracked, Is.EqualTo("project.bep"));
    }

    [Test]
    public void AbsentEntry_KeepsTheCallerSpelling()
    {
        // A case-sensitive volume can hold PROJECT.BEP while project.bep is what the index tracks; a
        // caller asking for project.bep must not be redirected to the neighbour.
        File.WriteAllText(Path.Combine(_root, "PROJECT.BEP"), "{}");
        string requested = Path.Combine(_root, "project.bep");
        if (File.Exists(requested))
        {
            Assert.Ignore("This case needs a case-sensitive volume.");
        }

        string tracked = GitCliVersionControlService.GetRepositoryRelativeProjectFilePath(
            new RepositoryInfo(_root, _root),
            requested);

        Assert.That(tracked, Is.EqualTo("project.bep"));
    }

    [Test]
    public void LinkWhoseParentIsOutsideTheRoot_FallsBackToTheResolvedTarget()
    {
        RequireSymbolicLinks();
        File.WriteAllText(Path.Combine(_root, "project.bep"), "{}");
        string outside = Path.Combine(Path.GetTempPath(), "beutl-tracked-path-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            string link = Path.Combine(outside, "link.bep");
            File.CreateSymbolicLink(link, Path.Combine(_root, "project.bep"));

            // The entry itself lives outside the root, so Git can only be tracking its target.
            string tracked = GitCliVersionControlService.GetRepositoryRelativeProjectFilePath(
                new RepositoryInfo(_root, _root),
                link);

            Assert.That(tracked, Is.EqualTo("project.bep"));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public void UnlistableParent_KeepsTheCallerSpelling()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("This case needs Unix directory permissions.");
            return;
        }

        string locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(locked);
        string projectFile = Path.Combine(locked, "project.bep");
        File.WriteAllText(projectFile, "{}");
        File.SetUnixFileMode(locked, UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        try
        {
            try
            {
                _ = Directory.EnumerateFileSystemEntries(locked).Any();
                Assert.Ignore("Directory listing was not denied; the test needs an unprivileged user.");
            }
            catch (UnauthorizedAccessException)
            {
            }

            // A traverse-only parent cannot be listed for spelling, so the caller's name is kept.
            string tracked = GitCliVersionControlService.GetRepositoryRelativeProjectFilePath(
                new RepositoryInfo(_root, _root),
                projectFile);

            Assert.That(tracked, Is.EqualTo("locked/project.bep"));
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Test]
    public void NestedProjectRoot_KeepsThePathspecPrefix()
    {
        string projectRoot = Path.Combine(_root, "nested");
        Directory.CreateDirectory(projectRoot);
        string projectFile = Path.Combine(projectRoot, "project.bep");
        File.WriteAllText(projectFile, "{}");

        string tracked = GitCliVersionControlService.GetRepositoryRelativeProjectFilePath(
            new RepositoryInfo(_root, projectRoot),
            projectFile);

        Assert.That(tracked, Is.EqualTo("nested/project.bep"));
    }

    private static void RequireSymbolicLinks()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("This regression requires Unix symbolic-link semantics.");
        }
    }
}
