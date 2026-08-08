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
