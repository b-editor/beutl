using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public sealed class NestedRepositoryTests : RealGitTestRepository
{
    [Test]
    public async Task Discovery_finds_the_enclosing_repository_and_builds_a_scoped_pathspec()
    {
        string projectRoot = CreateProjectDirectory();
        string expectedRepoRoot = await GetRepositoryTopLevelAsync();
        string expectedProjectRoot = Path.Combine(expectedRepoRoot, "nested", "project");
        using GitCliVersionControlService service = CreateUnassociatedService();

        RepositoryInfo? discovered = await service.DiscoverRepositoryAsync(
            projectRoot,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(discovered, Is.Not.Null);
            Assert.That(discovered!.RepoRoot, Is.EqualTo(expectedRepoRoot));
            Assert.That(discovered.ProjectRoot, Is.EqualTo(expectedProjectRoot));
            Assert.That(discovered.IsNestedInForeignRepo, Is.True);
            Assert.That(discovered.Pathspec, Is.EqualTo("nested/project"));
        });
    }

    [Test]
    public async Task Discovery_uses_git_paths_for_a_symbolic_linked_project_directory()
    {
        string projectRoot = CreateProjectDirectory();
        string expectedRepoRoot = await GetRepositoryTopLevelAsync();
        string expectedProjectRoot = Path.Combine(expectedRepoRoot, "nested", "project");
        string linkRoot = CreateTemporaryDirectory();
        string linkedProjectRoot = Path.Combine(linkRoot, "linked-project");
        CreateDirectorySymbolicLinkOrIgnore(linkedProjectRoot, projectRoot);
        using GitCliVersionControlService service = CreateUnassociatedService();

        RepositoryInfo? discovered = await service.DiscoverRepositoryAsync(
            linkedProjectRoot,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(discovered, Is.Not.Null);
            Assert.That(discovered!.RepoRoot, Is.EqualTo(expectedRepoRoot));
            Assert.That(discovered.ProjectRoot, Is.EqualTo(expectedProjectRoot));
            Assert.That(discovered.IsNestedInForeignRepo, Is.True);
            Assert.That(discovered.Pathspec, Is.EqualTo("nested/project"));
        });
    }

    [Test]
    public async Task Initialize_accepts_a_selection_that_aliases_the_same_repository()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        string linkRoot = CreateTemporaryDirectory();
        string linkedRepositoryRoot = Path.Combine(linkRoot, "linked-repository");
        CreateDirectorySymbolicLinkOrIgnore(linkedRepositoryRoot, Root);
        string linkedProjectRoot = Path.Combine(linkedRepositoryRoot, "nested", "project");
        using GitCliVersionControlService service = CreateUnassociatedService();

        await service.InitializeAsync(
            new InitOptions(
                new RepositoryInfo(linkedRepositoryRoot, linkedProjectRoot),
                UseLfsWhenAvailable: false),
            CancellationToken.None);

        string expectedRepoRoot = await GetRepositoryTopLevelAsync();
        Assert.Multiple(() =>
        {
            Assert.That(service.Repository!.RepoRoot, Is.EqualTo(expectedRepoRoot));
            Assert.That(
                service.Repository.ProjectRoot,
                Is.EqualTo(Path.Combine(expectedRepoRoot, "nested", "project")));
            Assert.That(service.Repository.Pathspec, Is.EqualTo("nested/project"));
        });
    }

    [Test]
    public async Task Initialize_requires_consent_and_commits_only_the_nested_project()
    {
        string projectRoot = CreateProjectDirectory();
        string expectedRepoRoot = await GetRepositoryTopLevelAsync();
        string projectFile = Path.Combine(projectRoot, "project.bep");
        string foreignFile = Path.Combine(Root, "foreign.txt");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(foreignFile, "foreign\n");
        await RunGitAsync("add", "--", "foreign.txt");
        using GitCliVersionControlService service = CreateUnassociatedService();

        EnclosingRepositoryConsentRequiredException? exception
            = Assert.ThrowsAsync<EnclosingRepositoryConsentRequiredException>(
                async () => await service.InitializeAsync(
                    new InitOptions(
                        new RepositoryInfo(projectRoot, projectRoot),
                        UseLfsWhenAvailable: false),
                    CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Repository.RepoRoot, Is.EqualTo(expectedRepoRoot));
            Assert.That(Directory.Exists(Path.Combine(projectRoot, ".git")), Is.False);
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitignore")), Is.False);
        });

        RepositoryInfo selectedRepository = exception!.Repository;
        await service.InitializeAsync(
            new InitOptions(
                selectedRepository,
                UseLfsWhenAvailable: false),
            CancellationToken.None);

        GitCommandResult committed = await RunGitAsync(
            "show",
            "--format=",
            "--name-only",
            "HEAD");
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        Assert.Multiple(() =>
        {
            Assert.That(service.Repository, Is.EqualTo(selectedRepository));
            Assert.That(File.ReadAllText(Path.Combine(projectRoot, ".gitignore")),
                Is.EqualTo("**/.beutl/\n*.tmp\n"));
            Assert.That(committed.Stdout, Does.Contain("nested/project/project.bep"));
            Assert.That(committed.Stdout, Does.Contain("nested/project/.gitignore"));
            Assert.That(committed.Stdout, Does.Not.Contain("foreign.txt"));
            Assert.That(staged.Stdout.Trim(), Is.EqualTo("foreign.txt"));
        });
    }

    [Test]
    public async Task Initialize_refuses_an_enclosing_repository_that_ignores_the_project()
    {
        string projectRoot = CreateProjectDirectory();
        string projectFile = Path.Combine(projectRoot, "project.bep");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "nested/project/\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore nested project");
        var selectedRepository = new RepositoryInfo(Root, projectRoot);
        using GitCliVersionControlService service = CreateUnassociatedService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(selectedRepository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("ignore rules"));
            Assert.That(File.Exists(projectFile), Is.True);
            Assert.That(service.Repository, Is.Null);
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitignore")), Is.False);
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitattributes")), Is.False);
        });
    }

    [Test]
    public async Task Initialize_detects_an_ignored_project_file_symbolic_link()
    {
        string projectRoot = CreateProjectDirectory();
        string externalRoot = CreateTemporaryDirectory();
        string externalProjectFile = Path.Combine(externalRoot, "project.bep");
        await File.WriteAllTextAsync(externalProjectFile, "{}\n");
        string projectFile = Path.Combine(projectRoot, "project.bep");
        CreateFileSymbolicLinkOrIgnore(projectFile, externalProjectFile);
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "*.bep\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore project files");
        var selectedRepository = new RepositoryInfo(Root, projectRoot);
        using GitCliVersionControlService service = CreateUnassociatedService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(selectedRepository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("ignore rules"));
            Assert.That(service.Repository, Is.Null);
            Assert.That(new FileInfo(projectFile).LinkTarget, Is.Not.Null);
        });
    }

    [TestCase(".gitignore")]
    [TestCase(".gitattributes")]
    public async Task Initialize_detects_an_ignored_future_hygiene_file(string fileName)
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitignore"),
            $"nested/project/{fileName}\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore nested hygiene file");
        var selectedRepository = new RepositoryInfo(Root, projectRoot);
        using GitCliVersionControlService service = CreateUnassociatedService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(selectedRepository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("ignore rules"));
            Assert.That(service.Repository, Is.Null);
            Assert.That(File.Exists(Path.Combine(projectRoot, fileName)), Is.False);
        });
    }

    [Test]
    public async Task Initialize_allows_ignored_Beutl_state_files()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        string stateDirectory = Path.Combine(projectRoot, ".beutl");
        Directory.CreateDirectory(stateDirectory);
        string stateFile = Path.Combine(stateDirectory, "recovery.scene");
        await File.WriteAllTextAsync(stateFile, "recovery\n");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "**/.beutl/\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore Beutl state");
        var selectedRepository = new RepositoryInfo(Root, projectRoot);
        using GitCliVersionControlService service = CreateUnassociatedService();

        await service.InitializeAsync(
            new InitOptions(selectedRepository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        GitCommandResult committed = await RunGitAsync(
            "show",
            "--format=",
            "--name-only",
            "HEAD");
        Assert.Multiple(() =>
        {
            Assert.That(service.Repository, Is.Not.Null);
            Assert.That(
                RepositoryPathComparer.AreEquivalent(
                    service.Repository!.ProjectRoot,
                    selectedRepository.ProjectRoot),
                Is.True);
            Assert.That(service.Repository.Pathspec, Is.EqualTo(selectedRepository.Pathspec));
            Assert.That(File.Exists(stateFile), Is.True);
            Assert.That(committed.Stdout, Does.Not.Contain(".beutl"));
        });
    }

    [TestCase("late.scene", "nested/project/*.scene")]
    [TestCase("resources/late.mp4", "nested/project/resources/")]
    [TestCase("late.SCENE", "nested/project/*.SCENE")]
    [TestCase("Resources/late.MP4", "nested/project/Resources/")]
    public async Task Snapshot_rejects_required_data_ignored_after_nested_activation(
        string relativeProjectPath,
        string ignoreRule)
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "baseline project");
        var repository = new RepositoryInfo(Root, projectRoot);
        using GitCliVersionControlService service = CreateUnassociatedService();
        await service.InitializeAsync(
            new InitOptions(repository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), $"{ignoreRule}\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore late project data");
        string requiredPath = Path.Combine(
            projectRoot,
            relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(requiredPath)!);
        await File.WriteAllTextAsync(requiredPath, "required data\n");

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        GitCommandResult staged = await RunGitAsync(
            "diff",
            "--cached",
            "--name-only",
            "--",
            repository.Pathspec);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("ignore rules"));
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(File.Exists(requiredPath), Is.True);
        });
    }

    [Test]
    public async Task Snapshot_keeps_the_associated_outer_repository_after_an_inner_repository_is_created()
    {
        string projectRoot = Path.Combine(Root, "project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "baseline project");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "/project/\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore outer project directory");
        var repository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => CreateRunner());

        var innerRepository = new RepositoryInfo(projectRoot, projectRoot);
        await CreateRunner().RunAsync(
            innerRepository,
            ["init", "-b", "main"],
            GitCommandOptions.Local,
            CancellationToken.None);
        string requiredPath = Path.Combine(projectRoot, "late.scene");
        await File.WriteAllTextAsync(requiredPath, "ignored required data\n");

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        GitCommandResult staged = await RunGitAsync(
            "diff",
            "--cached",
            "--name-only",
            "--",
            repository.Pathspec);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("ignore rules"));
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(File.Exists(requiredPath), Is.True);
        });
    }

    [Test]
    public async Task Snapshot_does_not_treat_glob_characters_in_the_project_prefix_as_pathspec_magic()
    {
        string projectRoot = Path.Combine(Root, "nested", "project[1]");
        string lookalikeRoot = Path.Combine(Root, "nested", "project1");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(lookalikeRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await RunGitAsync("add", "--", "nested/project[1]/project.bep");
        await RunGitAsync("commit", "-m", "baseline project");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "*.scene\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore scenes");
        await File.WriteAllTextAsync(
            Path.Combine(lookalikeRoot, "outside.scene"),
            "ignored outside project\n");
        var repository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => CreateRunner());

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        string requiredPath = Path.Combine(projectRoot, "late.scene");
        await File.WriteAllTextAsync(requiredPath, "ignored required data\n");
        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.NoChanges>());
            Assert.That(exception!.Message, Does.Contain("ignore rules"));
        });
    }

    [Test]
    public async Task Snapshot_fails_closed_when_the_ignored_file_query_is_truncated()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "baseline project");
        var repository = new RepositoryInfo(Root, projectRoot);
        var runner = new TruncatedIgnoredQueryRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => runner);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("safely"));
    }

    [Test]
    public async Task Snapshot_allows_only_unambiguous_Beutl_directory_warnings()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "baseline project");
        var repository = new RepositoryInfo(Root, projectRoot);
        var runner = new DiagnosticIgnoredQueryRunner(
            CreateRunner(),
            "warning: could not open directory 'nested/project/.beutl/': Permission denied\n"
            + "warning: could not open directory 'nested/project/cache/.BeUtL/child/': Permission denied\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => runner);

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<CommitResult.NoChanges>());
    }

    [TestCase("warning: could not open directory 'nested/project/.beutl/': Permission denied")]
    [TestCase("warning: could not open directory 'nested/project/.beutl/': Permission denied\nunexpected\n")]
    [TestCase("warning: could not open directory 'nested/project/.beutl/': Permission denied\n\n")]
    [TestCase("\n")]
    [TestCase("warning: could not open directory 'nested/project/.beutl-other/': Permission denied\n")]
    [TestCase("warning: could not open directory 'nested/project/evil'/.beutl/': Permission denied\n")]
    [TestCase("warning: could not open directory 'nested/project/.beutl/': forged/': Permission denied\n")]
    [TestCase("warning: could not open directory '../nested/project/.beutl/': Permission denied\n")]
    public async Task Snapshot_fails_closed_for_ambiguous_ignored_file_query_warnings(
        string stderr)
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "baseline project");
        var repository = new RepositoryInfo(Root, projectRoot);
        var runner = new DiagnosticIgnoredQueryRunner(CreateRunner(), stderr);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => runner);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("safely"));
    }

    [Test]
    public async Task Snapshot_uses_a_bounded_ignored_file_query_anchored_to_the_associated_repository()
    {
        string projectRoot = Path.Combine(Root, "nested", "project[1]");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "baseline project");
        var repository = new RepositoryInfo(Root, projectRoot);
        var runner = new RecordingRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => runner);

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        RecordedCommand? query = runner.Commands.SingleOrDefault(
            static command => command.Arguments.FirstOrDefault() == "ls-files"
                              && command.Arguments.Contains("--ignored"));
        Assert.That(query, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.NoChanges>());
            Assert.That(query!.Repository.RepoRoot, Is.EqualTo(Root));
            Assert.That(query.Repository.ProjectRoot, Is.EqualTo(projectRoot));
            Assert.That(
                query.Arguments,
                Is.EqualTo(new[]
                {
                    "ls-files",
                    "--others",
                    "--ignored",
                    "--exclude-standard",
                    "-z",
                    "--",
                    ":(top,glob)nested/project\\[1\\]/**/*.[bB][eE][pP]",
                    ":(top,glob)nested/project\\[1\\]/**/*.[sS][cC][eE][nN][eE]",
                    ":(top,glob)nested/project\\[1\\]/**/*.[bB][eE][lL][mM]",
                    ":(top,glob)nested/project\\[1\\]/**/[rR][eE][sS][oO][uU][rR][cC][eE][sS]/**",
                    ":(top,glob)nested/project\\[1\\]/.gitignore",
                    ":(top,glob)nested/project\\[1\\]/.gitattributes",
                    ":(top,exclude,glob)nested/project\\[1\\]/**/.[bB][eE][uU][tT][lL]/**",
                    ":(top,exclude,glob)nested/project\\[1\\]/**/*.[tT][mM][pP]",
                }));
            Assert.That(query.Options.ExecutionKind, Is.EqualTo(GitCommandExecutionKind.Local));
            Assert.That(query.Options.StandardInput, Is.Null);
            Assert.That(query.Options.UseLiteralPathspecs, Is.False);
            Assert.That(query.Options.MaxStdoutBytes, Is.InRange(1, 256 * 1024));
        });
    }

    [Test]
    public async Task Status_scopes_changes_but_reports_conflicts_from_the_enclosing_repository()
    {
        string projectRoot = CreateProjectDirectory();
        string projectFile = Path.Combine(projectRoot, "project.bep");
        string siblingFile = Path.Combine(Root, "sibling.scene");
        await File.WriteAllTextAsync(projectFile, "baseline project\n");
        await File.WriteAllTextAsync(siblingFile, "baseline sibling\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "baseline");
        await RunGitAsync("switch", "-c", "alternate");
        await File.WriteAllTextAsync(siblingFile, "alternate sibling\n");
        await RunGitAsync("add", "--", "sibling.scene");
        await RunGitAsync("commit", "-m", "alternate sibling");
        await RunGitAsync("switch", "main");
        await File.WriteAllTextAsync(siblingFile, "main sibling\n");
        await RunGitAsync("add", "--", "sibling.scene");
        await RunGitAsync("commit", "-m", "main sibling");
        Assert.ThrowsAsync<GitOperationException>(
            async () => await RunGitAsync("merge", "alternate"));
        await File.WriteAllTextAsync(projectFile, "changed project\n");
        string ignorePath = Path.Combine(projectRoot, ".gitignore");
        await File.WriteAllTextAsync(ignorePath, "nested custom ignore\n");
        var repository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => CreateRunner());

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.HasConflicts, Is.True);
            Assert.That(
                status.Changes,
                Does.Contain(new FileChange(
                    "nested/project/project.bep",
                    FileChangeStatus.Modified)));
            Assert.That(
                status.Changes.Select(static change => change.Path),
                Does.Not.Contain("sibling.scene"));
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.CommitAllAsync(
                    "beutl: snapshot on save",
                    SnapshotKind.Save,
                    CancellationToken.None));
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.EnsureRepositoryHygieneAsync(
                    CancellationToken.None));
        });

        GitCommandResult stagedProject = await RunGitAsync(
            "diff",
            "--cached",
            "--name-only",
            "--",
            "nested/project");
        Assert.Multiple(() =>
        {
            Assert.That(stagedProject.Stdout, Is.Empty);
            Assert.That(File.ReadAllText(ignorePath), Is.EqualTo("nested custom ignore\n"));
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitattributes")), Is.False);
        });
    }

    [Test]
    public async Task Commit_and_restore_never_stage_restore_or_clean_sibling_files()
    {
        string projectRoot = CreateProjectDirectory();
        string projectFile = Path.Combine(projectRoot, "project.bep");
        string laterElement = Path.Combine(projectRoot, "later.belm");
        string trackedSibling = Path.Combine(Root, "sibling.scene");
        string untrackedSibling = Path.Combine(Root, "sibling-clean-candidate.txt");

        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, ".gitignore"),
            "**/.beutl/\n*.tmp\n");
        await File.WriteAllTextAsync(projectFile, "target\n");
        await File.WriteAllTextAsync(trackedSibling, "sibling target\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "target");
        string targetSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();

        await File.WriteAllTextAsync(projectFile, "later\n");
        await File.WriteAllTextAsync(laterElement, "later element\n");
        await File.WriteAllTextAsync(trackedSibling, "sibling later\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "later");
        await File.WriteAllTextAsync(untrackedSibling, "must survive git clean\n");

        var repository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => CreateRunner());

        await File.WriteAllTextAsync(projectFile, "snapshot change\n");
        await File.WriteAllTextAsync(trackedSibling, "foreign worktree change\n");
        CommitResult snapshot = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        GitCommandResult snapshotFiles = await RunGitAsync(
            "show",
            "--format=",
            "--name-only",
            "HEAD");

        CheckedOutBranchTip currentTip = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        await service.CommitProjectTreeAsync(
            currentTip,
            targetSha,
            "beutl: restore target",
            SnapshotKind.Restore,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.TypeOf<CommitResult.Committed>());
            Assert.That(snapshotFiles.Stdout, Does.Contain("nested/project/project.bep"));
            Assert.That(snapshotFiles.Stdout, Does.Not.Contain("sibling.scene"));
            Assert.That(File.ReadAllText(projectFile), Is.EqualTo("target\n"));
            Assert.That(File.Exists(laterElement), Is.False);
            Assert.That(File.ReadAllText(trackedSibling), Is.EqualTo("foreign worktree change\n"));
            Assert.That(
                File.ReadAllText(untrackedSibling),
                Is.EqualTo("must survive git clean\n"));
        });
    }

    [Test]
    public async Task Branch_push_and_pull_apply_to_the_whole_enclosing_repository()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "foreign.txt"), "foreign\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "whole repository baseline");

        var repository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => CreateRunner());
        string branchStart = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await service.CreateBranchAsync(
            "whole-repository",
            branchStart,
            CancellationToken.None);
        GitCommandResult branchFiles = await RunGitAsync(
            "ls-tree",
            "-r",
            "--name-only",
            "whole-repository");

        string remoteRoot = CreateTemporaryDirectory();
        var remoteRepository = new RepositoryInfo(remoteRoot, remoteRoot);
        GitCliRunner runner = CreateRunner();
        await runner.RunAsync(
            remoteRepository,
            ["init", "--bare"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        RemoteOpResult push = await service.PushAsync(
            progress: null,
            CancellationToken.None);
        GitCommandResult remoteFiles = await runner.RunAsync(
            remoteRepository,
            ["ls-tree", "-r", "--name-only", "whole-repository"],
            GitCommandOptions.Local,
            CancellationToken.None);

        string peerRoot = CreateTemporaryDirectory();
        await RunGitAsync(
            "clone",
            "--branch",
            "whole-repository",
            remoteRoot,
            peerRoot);
        var peerRepository = new RepositoryInfo(peerRoot, peerRoot);
        await runner.RunAsync(
            peerRepository,
            ["config", "user.name", "Beutl Test Peer"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await runner.RunAsync(
            peerRepository,
            ["config", "user.email", "peer@example.invalid"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(peerRoot, "foreign-from-peer.txt"),
            "whole repository pull\n");
        await runner.RunAsync(
            peerRepository,
            ["add", "--", "foreign-from-peer.txt"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await runner.RunAsync(
            peerRepository,
            ["commit", "-m", "foreign peer update"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await runner.RunAsync(
            peerRepository,
            ["push"],
            GitCommandOptions.Network,
            CancellationToken.None);

        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            Path.Combine(projectRoot, "project.bep"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(branchFiles.Stdout, Does.Contain("foreign.txt"));
            Assert.That(push, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(remoteFiles.Stdout, Does.Contain("foreign.txt"));
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "foreign-from-peer.txt")),
                Is.EqualTo("whole repository pull\n"));
        });
    }

    private string CreateProjectDirectory()
    {
        string projectRoot = Path.Combine(Root, "nested", "project");
        Directory.CreateDirectory(projectRoot);
        return projectRoot;
    }

    private GitCliVersionControlService CreateUnassociatedService()
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner());
    }

    private async Task<string> GetRepositoryTopLevelAsync()
    {
        GitCommandResult topLevel = await RunGitAsync("rev-parse", "--show-toplevel");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(topLevel.Stdout.Trim()));
    }

    private static void CreateDirectorySymbolicLinkOrIgnore(string linkPath, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex)
            when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not creatable in this environment: {ex.Message}");
        }
    }

    private static void CreateFileSymbolicLinkOrIgnore(string linkPath, string target)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex)
            when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not creatable in this environment: {ex.Message}");
        }
    }

    private sealed record RecordedCommand(
        RepositoryInfo Repository,
        IReadOnlyList<string> Arguments,
        GitCommandOptions Options);

    private sealed class RecordingRunner(IGitCliRunner inner) : IGitCliRunner
    {
        public List<RecordedCommand> Commands { get; } = [];

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            Commands.Add(new RecordedCommand(repository, [.. arguments], options));
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

    private sealed class TruncatedIgnoredQueryRunner(IGitCliRunner inner) : IGitCliRunner
    {
        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            return arguments.FirstOrDefault() == "ls-files"
                   && arguments.Contains("--ignored")
                ? result with { StdoutTruncated = true }
                : result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class DiagnosticIgnoredQueryRunner(
        IGitCliRunner inner,
        string stderr) : IGitCliRunner
    {
        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            return arguments.FirstOrDefault() == "ls-files"
                   && arguments.Contains("--ignored")
                ? result with { Stderr = stderr }
                : result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }
}
