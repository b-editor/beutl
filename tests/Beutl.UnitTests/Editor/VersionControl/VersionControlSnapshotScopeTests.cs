using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class VersionControlSnapshotScopeTests : RealGitTestRepository
{
    [Test]
    public async Task One_element_property_edit_commits_only_that_element_file()
    {
        const string changedElement = "elements/11111111111111111111111111111111.belm";
        const string unchangedElement = "elements/22222222222222222222222222222222.belm";
        await WriteProjectFileAsync("project.bep", """{"name":"Snapshot scope fixture"}""" + "\n");
        await WriteProjectFileAsync(
            "main.scene",
            """{"elements":["11111111111111111111111111111111.belm","22222222222222222222222222222222.belm"]}"""
            + "\n");
        await WriteProjectFileAsync(
            changedElement,
            """{"id":"11111111-1111-1111-1111-111111111111","opacity":1.0}""" + "\n");
        await WriteProjectFileAsync(
            unchangedElement,
            """{"id":"22222222-2222-2222-2222-222222222222","opacity":1.0}""" + "\n");
        await WriteProjectFileAsync(".gitignore", "**/.beutl/\n*.tmp\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("commit", "-m", "saved project baseline");

        await WriteProjectFileAsync(
            changedElement,
            """{"id":"11111111-1111-1111-1111-111111111111","opacity":0.5}""" + "\n");
        await WriteProjectFileAsync(".beutl/view-state.json", """{"zoom":2}""" + "\n");
        await WriteProjectFileAsync("render-cache.tmp", "ignored\n");
        using var service = CreateService();

        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;
        IReadOnlyList<FileChange> changedFiles = await service.GetCommitFilesAsync(
            commit.Sha,
            CancellationToken.None);

        Assert.That(
            changedFiles,
            Is.EqualTo(
            [
                new FileChange(changedElement, FileChangeStatus.Modified),
            ]));
        Assert.That(changedFiles, Has.None.Matches<FileChange>(
            change => change.Path.EndsWith(".bep", StringComparison.Ordinal)
                      || change.Path.Contains(".beutl", StringComparison.Ordinal)
                      || change.Path == unchangedElement));
    }

    [Test]
    public async Task Snapshot_excludes_tracked_Beutl_state_and_tmp_files()
    {
        await WriteProjectFileAsync("project.bep", "baseline\n");
        await WriteProjectFileAsync(".beutl/output-profile.json", "old\n");
        await WriteProjectFileAsync("render-cache.tmp", "old\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("commit", "-m", "baseline");

        await WriteProjectFileAsync(".gitignore", "**/.beutl/\n*.tmp\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "add hygiene rules");

        await WriteProjectFileAsync("project.bep", "changed\n");
        await WriteProjectFileAsync(".beutl/output-profile.json", "machine-local\n");
        await WriteProjectFileAsync("render-cache.tmp", "machine-local\n");
        using var service = CreateService();

        var revision = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;
        IReadOnlyList<FileChange> changedFiles = await service.GetCommitFilesAsync(
            revision.Sha,
            CancellationToken.None);

        Assert.That(
            changedFiles,
            Is.EqualTo([new FileChange("project.bep", FileChangeStatus.Modified)]));
        GitCommandResult status = await RunGitAsync("status", "--porcelain=v1");
        Assert.That(status.Stdout, Is.EqualTo(" M .beutl/output-profile.json\n M render-cache.tmp\n"));
    }

    [Test]
    public async Task Initial_snapshot_excludes_tracked_Beutl_state_and_tmp_files()
    {
        await WriteProjectFileAsync("project.bep", "baseline\n");
        await WriteProjectFileAsync(".beutl/output-profile.json", "old\n");
        await WriteProjectFileAsync("render-cache.tmp", "old\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("commit", "-m", "baseline");

        await WriteProjectFileAsync("project.bep", "changed\n");
        await WriteProjectFileAsync(".beutl/output-profile.json", "machine-local\n");
        await WriteProjectFileAsync("render-cache.tmp", "machine-local\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner(TimeSpan.FromSeconds(30)));

        await service.InitializeAsync(
            new InitOptions(Repository, UseLfsWhenAvailable: false)
            {
                Identity = new GitIdentity("Beutl Test", "beutl-test@example.invalid"),
            },
            CancellationToken.None);

        GitCommandResult changedFiles = await RunGitAsync(
            "diff-tree",
            "--no-commit-id",
            "--name-only",
            "-r",
            "HEAD");
        GitCommandResult status = await RunGitAsync("status", "--porcelain=v1");
        Assert.Multiple(() =>
        {
            Assert.That(changedFiles.Stdout, Does.Contain("project.bep\n"));
            Assert.That(changedFiles.Stdout, Does.Not.Contain(".beutl/output-profile.json"));
            Assert.That(changedFiles.Stdout, Does.Not.Contain("render-cache.tmp"));
            Assert.That(status.Stdout, Is.EqualTo(" M .beutl/output-profile.json\n M render-cache.tmp\n"));
        });
    }

    private GitCliVersionControlService CreateService()
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(TimeSpan.FromSeconds(30)));
    }

    private async Task WriteProjectFileAsync(string relativePath, string contents)
    {
        string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);
    }
}
