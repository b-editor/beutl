using Beutl.Editor;
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
    public async Task Initial_snapshot_force_adds_only_the_serialized_temporary_sidecar()
    {
        (string projectFile, string sourceFile) = CreateProjectWithTemporarySidecar();
        await File.WriteAllTextAsync(sourceFile, "required plugin state\n");
        await WriteProjectFileAsync("render-cache.tmp", "scratch state\n");
        var runner = new RecordingGitRunner(CreateRunner(TimeSpan.FromSeconds(30)));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner,
            projectFile: projectFile);

        await service.InitializeAsync(
            new InitOptions(Repository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        GitCommandResult committedFiles = await RunGitAsync(
            "ls-tree",
            "-r",
            "--name-only",
            "HEAD");
        Assert.That(
            runner.Commands,
            Has.Some.Matches<IReadOnlyList<string>>(command =>
                command.Contains("add")
                && command.Contains("-f")
                && command.Contains(":(top,literal)assets/state.tmp")));
        Assert.That(
            committedFiles.Stdout,
            Does.Contain("assets/state.tmp\n"),
            $"Committed files were:\n{committedFiles.Stdout}");
        GitCommandResult committedSource = await RunGitAsync(
            "show",
            "HEAD:assets/state.tmp");
        Assert.Multiple(() =>
        {
            Assert.That(committedSource.Stdout, Is.EqualTo("required plugin state\n"));
            Assert.That(committedFiles.Stdout, Does.Contain("assets/state.tmp\n"));
            Assert.That(committedFiles.Stdout, Does.Not.Contain("render-cache.tmp"));
        });
    }

    [Test]
    public async Task Generated_hygiene_ignores_mixed_case_scratch_and_force_adds_required_sidecar()
    {
        const string requiredRelativePath = "assets/state.TMP";
        const string scratchRelativePath = "foo.TMP";
        await RunGitAsync("config", "core.ignorecase", "false");
        (string projectFile, string sourceFile) =
            CreateProjectWithTemporarySidecarAtPath(requiredRelativePath);
        await File.WriteAllTextAsync(sourceFile, "required plugin state\n");
        await WriteProjectFileAsync(scratchRelativePath, "scratch state\n");
        var runner = new RecordingGitRunner(CreateRunner(TimeSpan.FromSeconds(30)));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner,
            projectFile: projectFile);

        await service.InitializeAsync(
            new InitOptions(Repository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);
        GitCommandResult committedFiles = await RunGitAsync(
            "ls-tree",
            "-r",
            "--name-only",
            "HEAD");

        Assert.Multiple(() =>
        {
            Assert.That(
                File.ReadAllText(Path.Combine(Root, ".gitignore")),
                Is.EqualTo("**/.beutl/\n*.[tT][mM][pP]\n"));
            Assert.That(status.IsClean, Is.True);
            Assert.That(committedFiles.Stdout, Does.Contain(requiredRelativePath + "\n"));
            Assert.That(committedFiles.Stdout, Does.Not.Contain(scratchRelativePath));
            Assert.That(
                runner.Commands,
                Has.Some.Matches<IReadOnlyList<string>>(command =>
                    command.Contains("add")
                    && command.Contains("-f")
                    && command.Contains(":(top,literal)" + requiredRelativePath)));
        });
    }

    [Test]
    public async Task Snapshot_and_checkpoint_force_add_only_the_serialized_temporary_sidecar()
    {
        (string projectFile, string sourceFile) = CreateProjectWithTemporarySidecar();
        await File.WriteAllTextAsync(sourceFile, "required plugin state\n");
        await WriteProjectFileAsync("render-cache.tmp", "scratch state\n");
        await WriteProjectFileAsync(".gitignore", "*.tmp\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("commit", "-m", "saved project without ignored sidecars");
        using var service = CreateService(projectFile);

        var snapshot = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;
        GitCommandResult snapshotFiles = await RunGitAsync(
            "ls-tree",
            "-r",
            "--name-only",
            snapshot.Sha);

        await File.WriteAllTextAsync(sourceFile, "checkpoint plugin state\n");
        await WriteProjectFileAsync("render-cache.tmp", "new scratch state\n");
        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: safety checkpoint",
            CancellationToken.None);
        GitCommandResult checkpointSource = await RunGitAsync(
            "show",
            $"{checkpoint.Commit}:assets/state.tmp");
        GitCommandResult checkpointFiles = await RunGitAsync(
            "ls-tree",
            "-r",
            "--name-only",
            checkpoint.Commit);

        Assert.Multiple(() =>
        {
            Assert.That(snapshotFiles.Stdout, Does.Contain("assets/state.tmp\n"));
            Assert.That(snapshotFiles.Stdout, Does.Not.Contain("render-cache.tmp"));
            Assert.That(checkpointSource.Stdout, Is.EqualTo("checkpoint plugin state\n"));
            Assert.That(checkpointFiles.Stdout, Does.Not.Contain("render-cache.tmp"));
        });
    }

    [Test]
    public async Task Snapshot_removes_a_dereferenced_required_temporary_sidecar_without_staging_scratch()
    {
        (string projectFile, string sourceFile) = CreateProjectWithTemporarySidecar();
        string nextSourceFile = Path.Combine(Root, "assets", "next.tmp");
        string scratchFile = Path.Combine(Root, "render-cache.tmp");
        await File.WriteAllTextAsync(sourceFile, "old required state\n");
        await File.WriteAllTextAsync(nextSourceFile, "new required state\n");
        await File.WriteAllTextAsync(scratchFile, "scratch baseline\n");
        await WriteProjectFileAsync(".gitignore", "*.tmp\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("add", "-f", "--", "assets/state.tmp", "render-cache.tmp");
        await RunGitAsync("commit", "-m", "saved project baseline");

        string elementFile = Path.Combine(
            Root,
            "elements",
            "11111111111111111111111111111111.belm");
        string elementJson = await File.ReadAllTextAsync(elementFile);
        await File.WriteAllTextAsync(
            elementFile,
            elementJson.Replace("state.tmp", "next.tmp", StringComparison.Ordinal));
        await File.WriteAllTextAsync(scratchFile, "machine-local scratch\n");
        using var service = CreateService(projectFile);

        var revision = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;
        GitCommandResult treeFiles = await RunGitAsync(
            "ls-tree",
            "-r",
            "--name-only",
            revision.Sha);
        GitCommandResult nextContents = await RunGitAsync(
            "show",
            $"{revision.Sha}:assets/next.tmp");
        GitCommandResult scratchContents = await RunGitAsync(
            "show",
            $"{revision.Sha}:render-cache.tmp");
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        GitCommandResult trackedOldSidecar = await RunGitAsync(
            "ls-files",
            "--",
            "assets/state.tmp");
        string scratchWorktreeContents = await File.ReadAllTextAsync(scratchFile);

        Assert.Multiple(() =>
        {
            Assert.That(treeFiles.Stdout, Does.Not.Contain("assets/state.tmp\n"));
            Assert.That(treeFiles.Stdout, Does.Contain("assets/next.tmp\n"));
            Assert.That(nextContents.Stdout, Is.EqualTo("new required state\n"));
            Assert.That(scratchContents.Stdout, Is.EqualTo("scratch baseline\n"));
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(trackedOldSidecar.Stdout, Is.Empty);
            Assert.That(File.Exists(sourceFile), Is.True);
            Assert.That(scratchWorktreeContents, Is.EqualTo("machine-local scratch\n"));
        });
    }

    [Test]
    public async Task Snapshot_records_deletion_of_a_still_required_temporary_sidecar()
    {
        (string projectFile, string sourceFile) = CreateProjectWithTemporarySidecar();
        await File.WriteAllTextAsync(sourceFile, "required state\n");
        await WriteProjectFileAsync(".gitignore", "*.tmp\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("add", "-f", "--", "assets/state.tmp");
        await RunGitAsync("commit", "-m", "saved project baseline");
        File.Delete(sourceFile);
        using var service = CreateService(projectFile);

        var revision = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;
        IReadOnlyList<FileChange> changes = await service.GetCommitFilesAsync(
            revision.Sha,
            CancellationToken.None);
        GitCommandResult treeFiles = await RunGitAsync(
            "ls-tree",
            "-r",
            "--name-only",
            revision.Sha);
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");

        Assert.Multiple(() =>
        {
            Assert.That(changes, Does.Contain(
                new FileChange("assets/state.tmp", FileChangeStatus.Deleted)));
            Assert.That(treeFiles.Stdout, Does.Not.Contain("assets/state.tmp\n"));
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(File.Exists(sourceFile), Is.False);
        });
    }

    [Test]
    public async Task Enclosing_repository_snapshot_through_an_alias_removes_a_dereferenced_temporary_sidecar()
    {
        string projectRoot = Path.Combine(Root, "nested", "project");
        Directory.CreateDirectory(projectRoot);
        (string projectFile, string sourceFile) =
            CreateProjectWithTemporarySidecar(projectRoot);
        string nextSourceFile = Path.Combine(projectRoot, "assets", "next.tmp");
        await File.WriteAllTextAsync(sourceFile, "old required state\n");
        await File.WriteAllTextAsync(nextSourceFile, "new required state\n");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, ".gitignore"), "*.tmp\n");
        await RunGitAsync("add", "-A", "--", "nested/project");
        await RunGitAsync("add", "-f", "--", "nested/project/assets/state.tmp");
        await RunGitAsync("commit", "-m", "saved nested project baseline");
        string elementFile = Path.Combine(
            projectRoot,
            "elements",
            "11111111111111111111111111111111.belm");
        string elementJson = await File.ReadAllTextAsync(elementFile);
        await File.WriteAllTextAsync(
            elementFile,
            elementJson.Replace("state.tmp", "next.tmp", StringComparison.Ordinal));
        string aliasedProjectRoot = Path.Combine(CreateTemporaryDirectory(), "project");
        CreateDirectorySymbolicLinkOrIgnore(aliasedProjectRoot, projectRoot);
        var nestedRepository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            nestedRepository,
            watcher: null,
            _ => CreateRunner(TimeSpan.FromSeconds(30)),
            projectFile: Path.Combine(aliasedProjectRoot, Path.GetFileName(projectFile)));

        var revision = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: nested snapshot",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;
        GitCommandResult treeFiles = await RunGitAsync(
            "ls-tree",
            "-r",
            "--name-only",
            revision.Sha,
            "--",
            "nested/project");
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");

        Assert.Multiple(() =>
        {
            Assert.That(
                treeFiles.Stdout,
                Does.Not.Contain("nested/project/assets/state.tmp\n"));
            Assert.That(
                treeFiles.Stdout,
                Does.Contain("nested/project/assets/next.tmp\n"));
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(File.Exists(sourceFile), Is.True);
        });
    }

    [Test]
    public async Task Sparse_embedded_graph_removes_a_dereferenced_required_temporary_sidecar()
    {
        string projectFile = Path.Combine(Root, "project.bep");
        string sceneFile = Path.Combine(Root, "main.scene");
        string sourceFile = Path.Combine(Root, "assets", "state.tmp");
        string nextSourceFile = Path.Combine(Root, "assets", "next.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        var project = new Project { Uri = new Uri(projectFile) };
        var scene = new Scene(640, 480, "main") { Uri = new Uri(sceneFile) };
        var element = new Element
        {
            Uri = new Uri(Path.Combine(Root, "embedded.belm")),
        };
        var imageSource = new ImageSource();
        imageSource.ReadFrom(new Uri(sourceFile));
        var image = new SourceImage();
        image.Source.CurrentValue = imageSource;
        element.Objects.Add(image);
        scene.Children.Add(element);
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, new Uri(projectFile), CoreSerializationMode.Write);
        CoreSerializer.StoreToUri(
            scene,
            new Uri(sceneFile),
            CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects);
        await File.WriteAllTextAsync(sourceFile, "old required state\n");
        await File.WriteAllTextAsync(nextSourceFile, "new required state\n");
        await WriteProjectFileAsync(".gitignore", "*.tmp\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("add", "-f", "--", "assets/state.tmp");
        await RunGitAsync("commit", "-m", "saved sparse project baseline");
        string sceneJson = await File.ReadAllTextAsync(sceneFile);
        await File.WriteAllTextAsync(
            sceneFile,
            sceneJson.Replace("state.tmp", "next.tmp", StringComparison.Ordinal));
        using var service = CreateService(projectFile);

        var revision = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: sparse snapshot",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;
        GitCommandResult treeFiles = await RunGitAsync(
            "ls-tree",
            "-r",
            "--name-only",
            revision.Sha);

        Assert.Multiple(() =>
        {
            Assert.That(treeFiles.Stdout, Does.Not.Contain("assets/state.tmp\n"));
            Assert.That(treeFiles.Stdout, Does.Contain("assets/next.tmp\n"));
            Assert.That(treeFiles.Stdout, Does.Not.Contain(".belm\n"));
        });
    }

    [Test]
    public async Task Reserved_path_probe_uses_actual_volume_identity_for_required_tmp_casing()
    {
        (string projectFile, string sourceFile) = CreateProjectWithTemporarySidecar();
        await File.WriteAllTextAsync(sourceFile, "required state\n");
        string differentlyCasedSource = Path.Combine(Root, "assets", "STATE.tmp");
        if (!File.Exists(differentlyCasedSource))
        {
            Assert.Ignore("The test requires a case-insensitive filesystem volume.");
        }

        string elementFile = Path.Combine(
            Root,
            "elements",
            "11111111111111111111111111111111.belm");
        string elementJson = await File.ReadAllTextAsync(elementFile);
        await File.WriteAllTextAsync(
            elementFile,
            elementJson.Replace("state.tmp", "STATE.tmp", StringComparison.Ordinal));
        await WriteProjectFileAsync(".gitignore", "*.tmp\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("add", "-f", "--", "assets/state.tmp");
        await RunGitAsync("commit", "-m", "saved project baseline");
        using var watcher = new RepositoryWatcher(
            Repository,
            TimeProvider.System,
            startWatching: false);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher,
            _ => CreateRunner(TimeSpan.FromSeconds(30)),
            projectFile: projectFile);

        IReadOnlyList<string> reserved = await service.GetTrackedReservedPathsAsync(
            CancellationToken.None);

        Assert.That(reserved, Does.Not.Contain("assets/state.tmp"));
    }

    [Test]
    public async Task Snapshot_without_tracked_temporary_files_does_not_materialize_historical_graph()
    {
        string projectFile = Path.Combine(Root, "project.bep");
        string sceneFile = Path.Combine(Root, "main.scene");
        string elementFile = Path.Combine(
            Root,
            "elements",
            "11111111111111111111111111111111.belm");
        Directory.CreateDirectory(Path.GetDirectoryName(elementFile)!);
        var project = new Project { Uri = new Uri(projectFile) };
        var scene = new Scene(640, 480, "main") { Uri = new Uri(sceneFile) };
        var element = new Element { Uri = new Uri(elementFile) };
        scene.Children.Add(element);
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, new Uri(projectFile));
        CoreSerializer.StoreToUri(scene, new Uri(sceneFile));
        CoreSerializer.StoreToUri(element, new Uri(elementFile));
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("commit", "-m", "saved project baseline");
        await File.AppendAllTextAsync(elementFile, "\n");
        var runner = new RecordingGitRunner(CreateRunner(TimeSpan.FromSeconds(30)));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            projectFile: projectFile);

        Assert.That(
            await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None),
            Is.TypeOf<CommitResult.Committed>());

        Assert.That(runner.Commands, Has.None.Matches<IReadOnlyList<string>>(
            static command => command.Contains("archive")));
    }

    [Test]
    public async Task Consecutive_required_temporary_snapshots_materialize_each_base_at_most_once()
    {
        (string projectFile, string sourceFile) = CreateProjectWithTemporarySidecar();
        await File.WriteAllTextAsync(sourceFile, "baseline state\n");
        await WriteProjectFileAsync(".gitignore", "*.tmp\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("add", "-f", "--", "assets/state.tmp");
        await RunGitAsync("commit", "-m", "saved project baseline");
        var runner = new RecordingGitRunner(CreateRunner(TimeSpan.FromSeconds(30)));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            projectFile: projectFile);

        await File.WriteAllTextAsync(sourceFile, "first state\n");
        Assert.That(
            await service.CommitAllAsync(
                "beutl: first snapshot",
                SnapshotKind.Save,
                CancellationToken.None),
            Is.TypeOf<CommitResult.Committed>());
        await File.WriteAllTextAsync(sourceFile, "second state\n");
        Assert.That(
            await service.CommitAllAsync(
                "beutl: second snapshot",
                SnapshotKind.Save,
                CancellationToken.None),
            Is.TypeOf<CommitResult.Committed>());

        Assert.That(
            runner.Commands.Count(static command => command.Contains("archive")),
            Is.EqualTo(1));
    }

    [Test]
    public async Task Required_temporary_sidecar_is_not_reported_as_reserved_or_widened_by_scratch_changes()
    {
        (string projectFile, string sourceFile) = CreateProjectWithTemporarySidecar();
        await File.WriteAllTextAsync(sourceFile, "required plugin state\n");
        await WriteProjectFileAsync("render-cache.tmp", "tracked scratch baseline\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("add", "-f", "--", "assets/state.tmp", "render-cache.tmp");
        await RunGitAsync("commit", "-m", "saved project baseline");
        await WriteProjectFileAsync(".gitignore", "*.tmp\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore temporary files");
        await WriteProjectFileAsync("render-cache.tmp", "changed scratch state\n");
        using var service = CreateService(projectFile);

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        IReadOnlyList<string> reserved = await service.GetTrackedReservedPathsAsync(
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.NoChanges>());
            Assert.That(reserved, Does.Contain("render-cache.tmp"));
            Assert.That(reserved, Does.Not.Contain("assets/state.tmp"));
        });
    }

    [Test]
    public async Task Service_keeps_the_watcher_required_temporary_paths_in_sync_with_the_graph()
    {
        (string projectFile, string sourceFile) = CreateProjectWithTemporarySidecar();
        await File.WriteAllTextAsync(sourceFile, "first required state\n");
        string nextSourceFile = Path.Combine(Root, "assets", "next.tmp");
        await File.WriteAllTextAsync(nextSourceFile, "next required state\n");
        using var watcher = new RepositoryWatcher(
            Repository,
            TimeProvider.System,
            startWatching: false);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher,
            _ => CreateRunner(TimeSpan.FromSeconds(30)),
            projectFile: projectFile);

        Assert.Multiple(() =>
        {
            Assert.That(watcher.ShouldExcludeWatchedPath(sourceFile), Is.False);
            Assert.That(watcher.ShouldExcludeWatchedPath(nextSourceFile), Is.True);
        });

        string elementFile = Path.Combine(
            Root,
            "elements",
            "11111111111111111111111111111111.belm");
        string elementJson = await File.ReadAllTextAsync(elementFile);
        await File.WriteAllTextAsync(
            elementFile,
            elementJson.Replace("state.tmp", "next.tmp", StringComparison.Ordinal));
        await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(watcher.ShouldExcludeWatchedPath(sourceFile), Is.True);
            Assert.That(watcher.ShouldExcludeWatchedPath(nextSourceFile), Is.False);
        });
    }

    [Test]
    public async Task Snapshot_rejects_a_hook_that_changes_the_required_temporary_graph()
    {
        (string projectFile, string sourceFile) = CreateProjectWithTemporarySidecar();
        string scratchFile = Path.Combine(Root, "assets", "scratch.tmp");
        await File.WriteAllTextAsync(sourceFile, "required state\n");
        await File.WriteAllTextAsync(scratchFile, "tracked scratch\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("add", "-f", "--", "assets/state.tmp", "assets/scratch.tmp");
        await RunGitAsync("commit", "-m", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string elementPath = Path.Combine(
            Root,
            "elements",
            "11111111111111111111111111111111.belm");
        string alternateElement = Path.Combine(Root, ".git", "hook-element.json");
        string elementJson = await File.ReadAllTextAsync(elementPath);
        await File.WriteAllTextAsync(
            alternateElement,
            elementJson.Replace("state.tmp", "scratch.tmp", StringComparison.Ordinal));
        await WriteHookAsync(
            "pre-commit",
            "cp .git/hook-element.json elements/11111111111111111111111111111111.belm\n"
            + "git add -- elements/11111111111111111111111111111111.belm\n");
        await File.WriteAllTextAsync(sourceFile, "snapshot state\n");
        using var service = CreateService(projectFile);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "manual snapshot",
                SnapshotKind.Manual,
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("required temporary"));
            Assert.That((RunGitAsync("rev-parse", "HEAD").GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(baseTip));
        });
    }

    [Test]
    public async Task Snapshot_rejects_a_hook_graph_reference_to_reserved_state()
    {
        (string projectFile, string sourceFile) = CreateProjectWithTemporarySidecar();
        string reservedFile = Path.Combine(Root, ".beutl", "secret.png");
        Directory.CreateDirectory(Path.GetDirectoryName(reservedFile)!);
        await File.WriteAllTextAsync(sourceFile, "required state\n");
        await File.WriteAllTextAsync(reservedFile, "reserved state\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("add", "-f", "--", "assets/state.tmp");
        await RunGitAsync("commit", "-m", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string elementPath = Path.Combine(
            Root,
            "elements",
            "11111111111111111111111111111111.belm");
        string alternateElement = Path.Combine(Root, ".git", "hook-element.json");
        string elementJson = await File.ReadAllTextAsync(elementPath);
        string reservedJson = elementJson.Replace(
            "assets/state.tmp",
            ".beutl/secret.png",
            StringComparison.Ordinal);
        Assert.That(reservedJson, Is.Not.EqualTo(elementJson));
        await File.WriteAllTextAsync(alternateElement, reservedJson);
        await WriteHookAsync(
            "pre-commit",
            "cp .git/hook-element.json elements/11111111111111111111111111111111.belm\n"
            + "git add -- elements/11111111111111111111111111111111.belm\n");
        await File.WriteAllTextAsync(sourceFile, "snapshot state\n");
        using var service = CreateService(projectFile);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "manual snapshot",
                SnapshotKind.Manual,
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("reserved state directory"));
            Assert.That((RunGitAsync("rev-parse", "HEAD").GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(baseTip));
        });
    }

    [Test]
    public async Task Snapshot_includes_direct_addressable_file_source_even_when_tmp_is_ignored()
    {
        string projectFile = Path.Combine(Root, "project.bep");
        string sceneFile = Path.Combine(Root, "main.scene");
        string elementFile = Path.Combine(Root, "elements", "11111111111111111111111111111111.belm");
        const string sourceRelativePath = "assets/state.tmp";
        string sourceFile = Path.Combine(Root, "assets", "state.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(elementFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);

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
        await RunGitAsync("add", "-f", "--", sourceRelativePath);
        await RunGitAsync("commit", "-m", "saved project baseline");
        await File.WriteAllTextAsync(sourceFile, "plugin state updated\n");

        IReadOnlySet<string> referenced = SerializedProjectGraph.GetRelativePaths(projectFile, Root);
        Assert.That(referenced, Does.Contain(sourceRelativePath));

        using var service = CreateService(projectFile);
        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;
        IReadOnlyList<FileChange> committedFiles = await service.GetCommitFilesAsync(
            commit.Sha,
            CancellationToken.None);
        GitCommandResult committedSource = await RunGitAsync(
            "show",
            $"{commit.Sha}:{sourceRelativePath}");

        Assert.Multiple(() =>
        {
            Assert.That(committedFiles, Has.Some.Matches<FileChange>(
                change => change.Path == sourceRelativePath
                          && change.Status == FileChangeStatus.Modified));
            Assert.That(committedSource.Stdout, Is.EqualTo("plugin state updated\n"));
        });
    }

    [Test]
    public async Task Serialized_graph_preserves_literal_backslash_in_Unix_file_source_paths()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Backslashes are directory separators on Windows.");
        }

        const string sourceRelativePath = @"assets/clip\state.customstate";
        const string substitutedPath = "assets/clip/state.customstate";
        string projectFile = Path.Combine(Root, "project.bep");
        string sceneFile = Path.Combine(Root, "main.scene");
        string elementFile = Path.Combine(
            Root,
            "elements",
            "11111111111111111111111111111111.belm");
        string sourceFile = Path.Combine(Root, sourceRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(elementFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);

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
        await File.WriteAllTextAsync(
            sourceFile,
            "<<<<<<< ours\n{\"value\":1}\n=======\n{\"value\":2}\n>>>>>>> theirs\n");

        IReadOnlySet<string> referenced = SerializedProjectGraph.GetRelativePaths(projectFile, Root);
        string? markerFile = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(referenced, Does.Contain(sourceRelativePath));
            Assert.That(referenced, Does.Not.Contain(substitutedPath));
            Assert.That(
                markerFile is not null
                && VersionControlPathComparison.AreSameCanonicalPath(markerFile, sourceFile),
                Is.True);
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

    private (string ProjectFile, string SourceFile) CreateProjectWithTemporarySidecar()
        => CreateProjectWithTemporarySidecar(Root);

    private (string ProjectFile, string SourceFile) CreateProjectWithTemporarySidecarAtPath(
        string sourceRelativePath)
        => CreateProjectWithTemporarySidecar(Root, sourceRelativePath);

    private static (string ProjectFile, string SourceFile) CreateProjectWithTemporarySidecar(
        string projectRoot,
        string sourceRelativePath = "assets/state.tmp")
    {
        string projectFile = Path.Combine(projectRoot, "project.bep");
        string sceneFile = Path.Combine(projectRoot, "main.scene");
        string elementFile = Path.Combine(
            projectRoot,
            "elements",
            "11111111111111111111111111111111.belm");
        string sourceFile = Path.Combine(
            projectRoot,
            sourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(elementFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);

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
        return (projectFile, sourceFile);
    }

    private async Task WriteProjectFileAsync(string relativePath, string contents)
    {
        string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);
    }

    private async Task WriteHookAsync(string name, string body)
    {
        string hookRecord = (await RunGitAsync(
                "rev-parse",
                "--git-path",
                $"hooks/{name}"))
            .Stdout.TrimEnd('\r', '\n');
        string hookPath = Path.GetFullPath(
            Path.IsPathFullyQualified(hookRecord)
                ? hookRecord
                : Path.Combine(Root, hookRecord));
        Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
        await File.WriteAllTextAsync(hookPath, "#!/bin/sh\n" + body);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                hookPath,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }
    }

    private static void CreateDirectorySymbolicLinkOrIgnore(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex)
            when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not creatable in this environment: {ex.Message}");
        }
    }

    private sealed class RecordingGitRunner(IGitCliRunner inner) : IGitCliRunner
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            Commands.Add([.. arguments]);
            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }
}
