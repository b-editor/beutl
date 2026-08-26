using Beutl.Editor.VersionControl;
using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class VersionControlSnapshotScopeTests : RealGitTestRepository
{
    [Test]
    public async Task Snapshot_includes_direct_addressable_file_source_even_when_tmp_is_ignored()
    {
        string projectFile = Path.Combine(Root, "project.bep");
        string sceneFile = Path.Combine(Root, "main.scene");
        string elementFile = Path.Combine(Root, "elements", "11111111111111111111111111111111.belm");
        string sourceFile = Path.Combine(Root, "state.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(elementFile)!);

        var project = new Project { Uri = new Uri(projectFile) };
        var scene = new Scene(640, 480, "main") { Uri = new Uri(sceneFile) };
        var element = new Element { Uri = new Uri(elementFile) };
        var imageSource = new ImageSource();
        imageSource.ReadFrom(new Uri(sourceFile));
        var image = new SourceImage();
        image.Source.CurrentValue = imageSource;
        element.Objects.Add(image);
        scene.Children.Add(element);
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, new Uri(projectFile));
        CoreSerializer.StoreToUri(scene, new Uri(sceneFile));
        CoreSerializer.StoreToUri(element, new Uri(elementFile));
        Assert.That(await File.ReadAllTextAsync(elementFile), Does.Contain("state.tmp"));
        await File.WriteAllTextAsync(sourceFile, "plugin state\n");
        await WriteProjectFileAsync(".gitignore", "*.tmp\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("add", "-f", "--", "state.tmp");
        await RunGitAsync("commit", "-m", "saved project baseline");
        await File.WriteAllTextAsync(sourceFile, "plugin state updated\n");

        IReadOnlySet<string> referenced = SerializedProjectGraph.GetRelativePaths(projectFile, Root);
        Assert.That(referenced, Does.Contain("state.tmp"));

        using var service = CreateService(projectFile);
        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;
        IReadOnlyList<FileChange> committedFiles = await service.GetCommitFilesAsync(
            commit.Sha,
            CancellationToken.None);
        GitCommandResult committedSource = await RunGitAsync("show", $"{commit.Sha}:state.tmp");

        Assert.Multiple(() =>
        {
            Assert.That(committedFiles, Has.Some.Matches<FileChange>(
                change => change.Path == "state.tmp" && change.Status == FileChangeStatus.Modified));
            Assert.That(committedSource.Stdout, Is.EqualTo("plugin state updated\n"));
        });
    }

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

    private GitCliVersionControlService CreateService(string? projectFile = null)
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(TimeSpan.FromSeconds(30)),
            projectFile: projectFile);
    }

    private async Task WriteProjectFileAsync(string relativePath, string contents)
    {
        string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);
    }
}
