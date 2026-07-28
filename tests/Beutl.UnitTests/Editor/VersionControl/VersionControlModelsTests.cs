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
    public void GitOperationException_preserves_stderr_and_detects_lock_failure()
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
}
