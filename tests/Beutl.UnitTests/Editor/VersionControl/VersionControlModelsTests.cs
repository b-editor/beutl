using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class VersionControlModelsTests
{
    [Test]
    public void WorkspaceStatus_is_clean_only_without_changes()
    {
        var clean = new WorkspaceStatus("main", 0, 0, [], HasConflicts: false);
        var dirty = clean with
        {
            Changes = [new FileChange("project.bep", FileChangeStatus.Modified)],
        };

        Assert.Multiple(() =>
        {
            Assert.That(clean.IsClean, Is.True);
            Assert.That(dirty.IsClean, Is.False);
        });
    }

    [Test]
    public void RepositoryInfo_normalizes_nested_pathspec_and_enforces_containment()
    {
        string root = Path.Combine(Path.GetTempPath(), "repo");
        string project = Path.Combine(root, "projects", "movie");

        var repository = new RepositoryInfo(root, project);

        Assert.Multiple(() =>
        {
            Assert.That(repository.IsNestedInForeignRepo, Is.True);
            Assert.That(repository.Pathspec, Is.EqualTo("projects/movie"));
            Assert.That(
                () => new RepositoryInfo(root, Path.GetTempPath()),
                Throws.ArgumentException);
        });
    }

    [Test]
    public void RepositoryInfo_equality_uses_platform_path_semantics()
    {
        string root = Path.Combine(Path.GetTempPath(), "beutl-repository-equality");
        string upperRoot = root.ToUpperInvariant();
        var repository = new RepositoryInfo(root, Path.Combine(root, "project"));
        var upperRepository = new RepositoryInfo(
            upperRoot,
            Path.Combine(upperRoot, "PROJECT"));

        bool expectedEqual = OperatingSystem.IsWindows();
        Assert.That(repository.Equals(upperRepository), Is.EqualTo(expectedEqual));
        if (expectedEqual)
        {
            Assert.That(repository.GetHashCode(), Is.EqualTo(upperRepository.GetHashCode()));
        }
    }

    [Test]
    public void GitOperationException_preserves_safe_stderr_and_detects_lock_failure()
    {
        const string stderr = "fatal: Unable to create '.git/index.lock': File exists.";
        var exception = new GitOperationException(128, stderr);

        Assert.Multiple(() =>
        {
            Assert.That(exception.ExitCode, Is.EqualTo(128));
            Assert.That(exception.Stderr, Is.EqualTo(stderr));
            Assert.That(exception.IsRepositoryLockFailure, Is.True);
        });
    }

    [Test]
    public void GitOperationException_redacts_credentials_from_stderr_and_message()
    {
        const string secret = "super-secret-token";
        var exception = new GitOperationException(
            128,
            $"fatal: Authentication failed for 'https://user:{secret}@example.invalid/repo.git/'");

        Assert.Multiple(() =>
        {
            Assert.That(exception.Stderr, Does.Not.Contain(secret));
            Assert.That(exception.Message, Does.Not.Contain(secret));
            Assert.That(
                exception.Stderr,
                Does.Contain("https://***@example.invalid/repo.git/"));
        });
    }
}
