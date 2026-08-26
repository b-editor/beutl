using Beutl.Editor.VersionControl;
using Beutl.ProjectSystem;
using Beutl.Serialization;

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
    public async Task Initialize_accepts_a_selection_with_an_intermediate_symbolic_link_alias()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        string linkedNestedRoot = Path.Combine(Root, "linked-nested");
        CreateDirectorySymbolicLinkOrIgnore(linkedNestedRoot, Path.Combine(Root, "nested"));
        string linkedProjectRoot = Path.Combine(linkedNestedRoot, "project");
        var selectedRepository = new RepositoryInfo(Root, linkedProjectRoot);
        using GitCliVersionControlService service = CreateUnassociatedService();

        await service.InitializeAsync(
            new InitOptions(selectedRepository, UseLfsWhenAvailable: false),
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
    public async Task Initialize_rejects_a_required_project_file_symbolic_link()
    {
        string projectRoot = CreateProjectDirectory();
        string externalRoot = CreateTemporaryDirectory();
        string externalProjectFile = Path.Combine(externalRoot, "project.bep");
        await File.WriteAllTextAsync(externalProjectFile, "{}\n");
        string projectFile = Path.Combine(projectRoot, "project.bep");
        CreateFileSymbolicLinkOrIgnore(projectFile, externalProjectFile);
        var selectedRepository = new RepositoryInfo(Root, projectRoot);
        using GitCliVersionControlService service = CreateUnassociatedService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(selectedRepository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("file symbolic link 'project.bep'"));
            Assert.That(service.Repository, Is.Null);
            Assert.That(new FileInfo(projectFile).LinkTarget, Is.Not.Null);
        });
    }

    [Test]
    public async Task Initialize_rejects_required_content_in_the_Beutl_state_directory()
    {
        string projectRoot = CreateProjectDirectory();
        string projectFile = Path.Combine(projectRoot, "project.bep");
        string stateDirectory = Path.Combine(projectRoot, ".beutl");
        Directory.CreateDirectory(stateDirectory);
        string sceneFile = Path.Combine(stateDirectory, "linked.scene");
        var project = new Project();
        project.Items.Add(new Scene(1920, 1080, "LinkedScene")
        {
            Uri = new Uri(sceneFile),
        });
        CoreSerializer.StoreToUri(project, new Uri(projectFile));
        var selectedRepository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            isWorktreeMutationAllowed: static () => true,
            projectFile: projectFile);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(selectedRepository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(".beutl/linked.scene"));
            Assert.That(service.Repository, Is.Null);
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitignore")), Is.False);
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitattributes")), Is.False);
        });
    }

    [Test]
    public async Task Initialize_stages_with_the_lfs_aware_execution_kind()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        var selectedRepository = new RepositoryInfo(Root, projectRoot);
        var runner = new RecordingRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(selectedRepository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        RecordedCommand add = runner.Commands.Single(static command =>
            command.Arguments.Count > 1
            && command.Arguments[0] == "add"
            && command.Arguments[1] == "-A");
        Assert.That(
            add.Options.ExecutionKind,
            Is.EqualTo(GitCommandExecutionKind.LocalWithLfs));
    }

    [Test]
    public async Task Initialize_rejects_required_content_beneath_a_symbolic_link_directory()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("This regression requires Unix symbolic-link semantics.");
        }

        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        string externalRoot = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(externalRoot, "linked.scene"), "{}\n");
        string linkedDirectory = Path.Combine(projectRoot, "linked");
        CreateDirectorySymbolicLinkOrIgnore(linkedDirectory, externalRoot);
        var selectedRepository = new RepositoryInfo(Root, projectRoot);
        using GitCliVersionControlService service = CreateUnassociatedService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(selectedRepository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("symbolic-link directory 'linked'"));
            Assert.That(service.Repository, Is.Null);
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitignore")), Is.False);
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitattributes")), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Initialize_rejects_required_content_beneath_a_nested_git_repository(
        bool useGitFile)
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        string nestedRoot = Path.Combine(projectRoot, "embedded");
        Directory.CreateDirectory(nestedRoot);
        await File.WriteAllTextAsync(Path.Combine(nestedRoot, "nested.scene"), "{}\n");
        if (useGitFile)
        {
            await File.WriteAllTextAsync(
                Path.Combine(nestedRoot, ".git"),
                "gitdir: ../git-metadata\n");
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(nestedRoot, ".git"));
        }

        var selectedRepository = new RepositoryInfo(Root, projectRoot);
        using GitCliVersionControlService service = CreateUnassociatedService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(selectedRepository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("nested Git repository 'embedded'"));
            Assert.That(service.Repository, Is.Null);
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitignore")), Is.False);
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitattributes")), Is.False);
        });
    }

    [Test]
    public async Task Snapshot_rejects_required_content_beneath_a_nested_git_repository()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "baseline\n");
        await RunGitAsync("add", "--", "nested/project/project.bep");
        await RunGitAsync("commit", "-m", "baseline project");
        string nestedRoot = Path.Combine(projectRoot, "embedded");
        Directory.CreateDirectory(Path.Combine(nestedRoot, ".git"));
        await File.WriteAllTextAsync(Path.Combine(nestedRoot, "nested.scene"), "{}\n");
        var repository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => CreateRunner());

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("nested Git repository 'embedded'"));
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

    [Test]
    public async Task Initialize_leaves_tracked_reserved_project_state_alone_without_consent()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await CommitFileAsync(
            Path.Combine("nested", "project", ".beutl", "view-state.json"),
            "{}\n",
            "track reserved project state");
        using GitCliVersionControlService service = CreateUnassociatedService();

        await service.InitializeAsync(
            new InitOptions(new RepositoryInfo(Root, projectRoot), UseLfsWhenAvailable: false),
            CancellationToken.None);

        GitCommandResult tracked = await RunGitAsync("ls-files", "--", "nested/project");

        Assert.That(tracked.Stdout, Does.Contain(".beutl/view-state.json"));
    }

    [Test]
    public async Task Initialize_reports_the_reserved_project_state_the_repository_already_tracks()
    {
        string projectRoot = CreateProjectDirectory();
        await CommitFileAsync(
            Path.Combine("nested", "project", ".beutl", "view-state.json"),
            "{}\n",
            "track reserved project state");
        await CommitFileAsync(
            Path.Combine("nested", "project", "keep.bep"),
            "{}\n",
            "track a project file");
        using GitCliVersionControlService service = CreateUnassociatedService();

        await service.InitializeAsync(
            new InitOptions(new RepositoryInfo(Root, projectRoot), UseLfsWhenAvailable: false),
            CancellationToken.None);

        IReadOnlyList<string> reserved = await service.GetTrackedReservedPathsAsync(
            CancellationToken.None);

        Assert.That(reserved, Is.EqualTo(new[] { "nested/project/.beutl/view-state.json" }));
    }

    [Test]
    public async Task Initialize_stops_tracking_reserved_project_state_the_repository_already_tracked()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await CommitFileAsync(
            Path.Combine("nested", "project", ".beutl", "view-state.json"),
            "{}\n",
            "track reserved project state");
        await CommitFileAsync(
            Path.Combine("nested", "project", "leftover.tmp"),
            "leftover\n",
            "track a temporary file");
        using GitCliVersionControlService service = CreateUnassociatedService();

        await service.InitializeAsync(
            new InitOptions(new RepositoryInfo(Root, projectRoot), UseLfsWhenAvailable: false),
            CancellationToken.None);
        await service.UntrackReservedPathsAsync(
            await service.GetTrackedReservedPathsAsync(CancellationToken.None),
            CancellationToken.None);

        GitCommandResult tracked = await RunGitAsync("ls-files", "--", "nested/project");
        GitCommandResult status = await RunGitAsync("status", "--porcelain");

        Assert.Multiple(() =>
        {
            Assert.That(tracked.Stdout, Does.Not.Contain(".beutl/"));
            Assert.That(tracked.Stdout, Does.Not.Contain("leftover.tmp"));
            // The pull precondition inspects the whole repository, so the untracking has to be
            // committed rather than left staged - otherwise every pull reports RepositoryDirty.
            Assert.That(status.Stdout.Trim(), Is.Empty);
        });
    }

    [Test]
    public async Task Untracking_reserved_project_state_restores_the_index_when_it_is_cancelled()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await CommitFileAsync(
            Path.Combine("nested", "project", ".beutl", "view-state.json"),
            "{}\n",
            "track reserved project state");
        using var cancellation = new CancellationTokenSource();
        // Cancel exactly between the index change and the commit that would make it durable.
        var runner = new CancelAfterCommandRunner(
            CreateRunner(),
            arguments => arguments.Count > 1
                         && arguments[0] == "read-tree"
                         && arguments[1] != "--reset",
            cancellation);
        using GitCliVersionControlService service = new(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(new RepositoryInfo(Root, projectRoot), UseLfsWhenAvailable: false),
            CancellationToken.None);
        IReadOnlyList<string> reserved = await service.GetTrackedReservedPathsAsync(
            CancellationToken.None);

        Assert.ThrowsAsync<OperationCanceledException>(
            () => service.UntrackReservedPathsAsync(reserved, cancellation.Token));

        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        GitCommandResult tracked = await RunGitAsync("ls-files", "--", "nested/project");
        GitCommandResult head = await RunGitAsync("symbolic-ref", "--quiet", "HEAD");

        Assert.Multiple(() =>
        {
            // A cancelled untrack must not leave the removal staged: the next user commit would
            // otherwise stop tracking the reserved state without anyone asking for it.
            Assert.That(staged.Stdout.Trim(), Is.Empty);
            Assert.That(tracked.Stdout, Does.Contain(".beutl/view-state.json"));
            Assert.That(head.Stdout.Trim(), Is.EqualTo("refs/heads/main"));
            Assert.That(runner.TemporaryIndexPath, Is.Not.Null);
            Assert.That(File.Exists(runner.TemporaryIndexPath!), Is.False);
            Assert.That(runner.TemporaryWorktreePath, Is.Not.Null);
            Assert.That(Directory.Exists(runner.TemporaryWorktreePath!), Is.False);
        });
    }

    [Test]
    public async Task Untracking_reserved_project_state_does_not_capture_an_unrelated_root_stage()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await CommitFileAsync(
            Path.Combine("nested", "project", ".beutl", "view-state.json"),
            "{}\n",
            "track reserved project state");
        using GitCliVersionControlService service = CreateUnassociatedService();
        await service.InitializeAsync(
            new InitOptions(new RepositoryInfo(Root, projectRoot), UseLfsWhenAvailable: false),
            CancellationToken.None);
        IReadOnlyList<string> reserved = await service.GetTrackedReservedPathsAsync(
            CancellationToken.None);

        string unrelatedPath = Path.Combine(Root, "root-unrelated.txt");
        await File.WriteAllTextAsync(unrelatedPath, "keep staged\n");
        var runner = new StageBeforeCleanupPublicationRunner(
            CreateRunner(),
            Repository,
            unrelatedPath);
        using GitCliVersionControlService interleavedService = new(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        await interleavedService.UntrackReservedPathsAsync(reserved, CancellationToken.None);

        GitCommandResult commitFiles = await RunGitAsync(
            "show",
            "--format=",
            "--name-only",
            "HEAD");
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        GitCommandResult tracked = await RunGitAsync("ls-files", "--", "nested/project");
        GitCommandResult status = await RunGitAsync("status", "--porcelain");
        GitCommandResult metadata = await RunGitAsync(
            "show",
            "-s",
            "--format=%an%n%ae%n%s%n%b",
            "HEAD");
        GitCommandResult head = await RunGitAsync("symbolic-ref", "--quiet", "HEAD");
        Assert.Multiple(() =>
        {
            Assert.That(runner.InterceptionCount, Is.EqualTo(1));
            Assert.That(commitFiles.Stdout, Does.Not.Contain("root-unrelated.txt"));
            Assert.That(staged.Stdout.Trim(), Is.EqualTo("root-unrelated.txt"));
            Assert.That(tracked.Stdout, Does.Not.Contain(".beutl/"));
            Assert.That(status.Stdout, Does.Contain("A  root-unrelated.txt"));
            Assert.That(metadata.Stdout, Does.Contain("Beutl Test\nbeutl-test@example.invalid\n"));
            Assert.That(metadata.Stdout, Does.Contain("beutl: stop tracking reserved project state\n"));
            Assert.That(metadata.Stdout, Does.Contain("Beutl-Snapshot: init\n"));
            Assert.That(head.Stdout.Trim(), Is.EqualTo("refs/heads/main"));
            Assert.That(runner.TemporaryIndexPath, Is.Not.Null);
            Assert.That(File.Exists(runner.TemporaryIndexPath!), Is.False);
            Assert.That(runner.TemporaryWorktreePath, Is.Not.Null);
            Assert.That(Directory.Exists(runner.TemporaryWorktreePath!), Is.False);
        });
    }

    [Test]
    public async Task Untracking_reserved_project_state_reconciles_after_one_shot_ref_observation_loss()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await CommitFileAsync(
            Path.Combine("nested", "project", ".beutl", "view-state.json"),
            "{}\n",
            "track reserved project state");
        using (GitCliVersionControlService setupService = CreateUnassociatedService())
        {
            await setupService.InitializeAsync(
                new InitOptions(new RepositoryInfo(Root, projectRoot), UseLfsWhenAvailable: false),
                CancellationToken.None);
        }

        var runner = new LostReservedCleanupObservationRunner(CreateRunner());
        using GitCliVersionControlService service = new(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);
        IReadOnlyList<string> reserved = await service.GetTrackedReservedPathsAsync(
            CancellationToken.None);

        await service.UntrackReservedPathsAsync(reserved, CancellationToken.None);

        GitCommandResult tracked = await RunGitAsync("ls-files", "--", "nested/project");
        GitCommandResult status = await RunGitAsync("status", "--porcelain");
        Assert.Multiple(() =>
        {
            Assert.That(runner.ObservationFailures, Is.EqualTo(1));
            Assert.That(tracked.Stdout, Does.Not.Contain(".beutl/"));
            Assert.That(status.Stdout.Trim(), Is.Empty);
        });
    }

    [Test]
    public async Task Untracking_reserved_project_state_rethrows_a_stale_head_lock()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await CommitFileAsync(
            Path.Combine("nested", "project", ".beutl", "view-state.json"),
            "{}\n",
            "track reserved project state");
        string headRecord = (await RunGitAsync("rev-parse", "--git-path", "HEAD"))
            .Stdout.TrimEnd('\r', '\n');
        string headPath = Path.GetFullPath(
            Path.IsPathFullyQualified(headRecord)
                ? headRecord
                : Path.Combine(Root, headRecord));
        string lockPath = headPath + ".lock";
        await File.WriteAllTextAsync(lockPath, "stale");
        File.SetLastWriteTimeUtc(
            lockPath,
            DateTime.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        using GitCliVersionControlService service = new(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner());
        IReadOnlyList<string> reserved = await service.GetTrackedReservedPathsAsync(
            CancellationToken.None);
        var notification = new TaskCompletionSource<RepositoryLockInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.RecoverableLockAvailable += (_, info) => notification.TrySetResult(info);

        GitOperationException? exception = Assert.ThrowsAsync<GitOperationException>(
            () => service.UntrackReservedPathsAsync(reserved, CancellationToken.None));
        RepositoryLockInfo lockInfo = await notification.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.IsRepositoryLockFailure, Is.True);
            Assert.That(
                RepositoryPathComparer.AreEquivalent(lockInfo.LockPath, lockPath),
                Is.True);
            Assert.That(service.RecoverableLock, Is.EqualTo(lockInfo));
            Assert.That(File.Exists(lockPath), Is.True);
        });
    }

    [Test]
    public async Task Untracking_reserved_project_state_refuses_a_moved_branch_without_losing_staged_state()
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await CommitFileAsync(
            Path.Combine("nested", "project", ".beutl", "view-state.json"),
            "{}\n",
            "track reserved project state");
        string unrelatedPath = Path.Combine(Root, "root-unrelated.txt");
        await File.WriteAllTextAsync(unrelatedPath, "keep staged\n");
        await RunGitAsync("add", "--", "root-unrelated.txt");
        IReadOnlyList<string> reserved;
        using (GitCliVersionControlService service = CreateUnassociatedService())
        {
            await service.InitializeAsync(
                new InitOptions(new RepositoryInfo(Root, projectRoot), UseLfsWhenAvailable: false),
                CancellationToken.None);
            reserved = await service.GetTrackedReservedPathsAsync(CancellationToken.None);
        }
        string indexBefore = (await RunGitAsync("write-tree")).Stdout.Trim();

        var runner = new MoveBranchBeforePublicationRunner(CreateRunner());
        using GitCliVersionControlService interleavedService = new(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);
        await interleavedService.UntrackReservedPathsAsync(reserved, CancellationToken.None);

        string indexAfter = (await RunGitAsync("write-tree")).Stdout.Trim();
        GitCommandResult tracked = await RunGitAsync("ls-files", "--", "nested/project");
        string branchAfter = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        GitCommandResult head = await RunGitAsync("symbolic-ref", "--quiet", "HEAD");
        Assert.Multiple(() =>
        {
            Assert.That(runner.InterceptionCount, Is.EqualTo(1));
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(tracked.Stdout, Does.Contain(".beutl/"));
            Assert.That(branchAfter, Is.EqualTo(runner.ExternalTip));
            Assert.That(head.Stdout.Trim(), Is.EqualTo("refs/heads/main"));
            Assert.That(runner.TemporaryIndexPath, Is.Not.Null);
            Assert.That(File.Exists(runner.TemporaryIndexPath!), Is.False);
            Assert.That(runner.TemporaryWorktreePath, Is.Not.Null);
            Assert.That(Directory.Exists(runner.TemporaryWorktreePath!), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Untracking_reserved_project_state_restores_index_when_branch_moves_after_live_reconciliation(
        bool stageExternalIndex)
    {
        string projectRoot = CreateProjectDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await CommitFileAsync(
            Path.Combine("nested", "project", ".beutl", "view-state.json"),
            "{}\n",
            "track reserved project state");
        string unrelatedPath = Path.Combine(Root, "root-unrelated.txt");
        await File.WriteAllTextAsync(unrelatedPath, "keep staged\n");
        await RunGitAsync("add", "--", "root-unrelated.txt");
        const string externalStagePath = "external-stage.txt";
        if (stageExternalIndex)
        {
            await File.WriteAllTextAsync(Path.Combine(Root, externalStagePath), "external stage\n");
        }
        IReadOnlyList<string> reserved;
        using (GitCliVersionControlService service = CreateUnassociatedService())
        {
            await service.InitializeAsync(
                new InitOptions(new RepositoryInfo(Root, projectRoot), UseLfsWhenAvailable: false),
                CancellationToken.None);
            reserved = await service.GetTrackedReservedPathsAsync(CancellationToken.None);
        }

        string indexPath = Path.Combine(Root, ".git", "index");
        byte[] indexBefore = await File.ReadAllBytesAsync(indexPath);
        var runner = new MoveBranchAfterLiveReconciliationRunner(
            CreateRunner(),
            Repository,
            stageExternalIndex ? externalStagePath : null);
        using GitCliVersionControlService interleavedService = new(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        await interleavedService.UntrackReservedPathsAsync(reserved, CancellationToken.None);

        byte[] indexAfter = await File.ReadAllBytesAsync(indexPath);
        GitCommandResult tracked = await RunGitAsync("ls-files", "--", "nested/project");
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        string branchAfter = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        GitCommandResult head = await RunGitAsync("symbolic-ref", "--quiet", "HEAD");
        Assert.Multiple(() =>
        {
            Assert.That(runner.InterceptionCount, Is.EqualTo(1));
            Assert.That(runner.LiveReconciliationObserved, Is.True);
            if (stageExternalIndex)
            {
                Assert.That(indexAfter, Is.Not.EqualTo(indexBefore));
            }
            else
            {
                Assert.That(indexAfter, Is.EqualTo(indexBefore));
            }
            if (stageExternalIndex)
            {
                Assert.That(tracked.Stdout, Does.Not.Contain(".beutl/"));
            }
            else
            {
                Assert.That(tracked.Stdout, Does.Contain(".beutl/"));
            }
            Assert.That(staged.Stdout, Does.Contain("root-unrelated.txt"));
            if (stageExternalIndex)
            {
                Assert.That(staged.Stdout, Does.Contain(externalStagePath));
            }
            Assert.That(branchAfter, Is.EqualTo(runner.ExternalTip));
            Assert.That(head.Stdout.Trim(), Is.EqualTo("refs/heads/main"));
            Assert.That(runner.TemporaryIndexPath, Is.Not.Null);
            Assert.That(File.Exists(runner.TemporaryIndexPath!), Is.False);
            Assert.That(runner.TemporaryWorktreePath, Is.Not.Null);
            Assert.That(Directory.Exists(runner.TemporaryWorktreePath!), Is.False);
        });
    }

    [Test]
    public async Task Untracking_staged_only_reserved_project_state_leaves_branch_and_index_untouched()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string projectRoot = CreateProjectDirectory();
        string reservedPath = Path.Combine(projectRoot, ".beutl", "view-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reservedPath)!);
        await File.WriteAllTextAsync(reservedPath, "{}\n");
        string repositoryRelativeReservedPath = Path.GetRelativePath(Root, reservedPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        await RunGitAsync("add", "-f", "--", repositoryRelativeReservedPath);
        string branchBefore = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string indexRecord = (await RunGitAsync("rev-parse", "--git-path", "index"))
            .Stdout.TrimEnd('\r', '\n');
        string indexPath = Path.GetFullPath(
            Path.IsPathFullyQualified(indexRecord)
                ? indexRecord
                : Path.Combine(Root, indexRecord));
        byte[] indexBefore = await File.ReadAllBytesAsync(indexPath);
        IReadOnlyList<string> reserved;
        using (var service = new GitCliVersionControlService(
                   CreateInstalledLocator(),
                   Repository,
                   watcher: null,
                   _ => CreateRunner()))
        {
            reserved = await service.GetTrackedReservedPathsAsync(CancellationToken.None);
            await service.UntrackReservedPathsAsync(reserved, CancellationToken.None);
        }

        string branchAfter = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        byte[] indexAfter = await File.ReadAllBytesAsync(indexPath);
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        Assert.Multiple(() =>
        {
            Assert.That(reserved, Is.EqualTo(new[] { repositoryRelativeReservedPath }));
            Assert.That(branchAfter, Is.EqualTo(branchBefore));
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(staged.Stdout, Does.Contain(repositoryRelativeReservedPath));
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

    // Cancels the shared token once a matching command has run, so a cancellation can be placed
    // between two git invocations of the same operation.
    private sealed class CancelAfterCommandRunner(
        IGitCliRunner inner,
        Func<IReadOnlyList<string>, bool> match,
        CancellationTokenSource cancellation) : IGitCliRunner
    {
        public string? TemporaryIndexPath { get; private set; }

        public string? TemporaryWorktreePath { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            RecordTemporaryPaths(arguments, options);
            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (match(arguments))
            {
                await cancellation.CancelAsync();
            }

            return result;
        }

        private void RecordTemporaryPaths(
            IReadOnlyList<string> arguments,
            GitCommandOptions options)
        {
            if (arguments is ["worktree", "add", "--detach", "--no-checkout", ..])
            {
                TemporaryWorktreePath = arguments[4];
            }

            if (options.EnvironmentOverrides?.TryGetValue(
                    "GIT_INDEX_FILE",
                    out string? indexPath) == true)
            {
                TemporaryIndexPath = indexPath;
            }
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class StageBeforeCleanupPublicationRunner(
        IGitCliRunner inner,
        RepositoryInfo originalRepository,
        string unrelatedPath) : IGitCliRunner
    {
        private int _interceptionPending = 1;

        public int InterceptionCount { get; private set; }

        public string? TemporaryIndexPath { get; private set; }

        public string? TemporaryWorktreePath { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            RecordTemporaryPaths(arguments, options);
            if (arguments.FirstOrDefault() == "update-ref"
                && arguments.Contains("beutl: stop tracking reserved project state")
                && Interlocked.Exchange(ref _interceptionPending, 0) == 1)
            {
                await inner.RunAsync(
                        originalRepository,
                        ["add", "--", Path.GetRelativePath(originalRepository.RepoRoot, unrelatedPath)],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                InterceptionCount++;
            }

            return await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
        }

        private void RecordTemporaryPaths(
            IReadOnlyList<string> arguments,
            GitCommandOptions options)
        {
            if (arguments is ["worktree", "add", "--detach", "--no-checkout", ..])
            {
                TemporaryWorktreePath = arguments[4];
            }

            if (options.EnvironmentOverrides?.TryGetValue(
                    "GIT_INDEX_FILE",
                    out string? indexPath) == true)
            {
                TemporaryIndexPath = indexPath;
            }
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class MoveBranchBeforePublicationRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _interceptionPending = 1;

        public int InterceptionCount { get; private set; }

        public string? ExternalTip { get; private set; }

        public string? TemporaryIndexPath { get; private set; }

        public string? TemporaryWorktreePath { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            RecordTemporaryPaths(arguments, options);
            if (arguments.FirstOrDefault() == "update-ref"
                && arguments.Contains("beutl: stop tracking reserved project state")
                && Interlocked.Exchange(ref _interceptionPending, 0) == 1)
            {
                string branchRef = arguments[3];
                string currentCommit = arguments[^1];
                GitCommandResult tree = await inner.RunAsync(
                        repository,
                        ["rev-parse", currentCommit + "^{tree}"],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                GitCommandResult externalCommit = await inner.RunAsync(
                        repository,
                        [
                            "commit-tree",
                            tree.Stdout.Trim(),
                            "-p",
                            currentCommit,
                            "-m",
                            "external branch movement",
                        ],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                await inner.RunAsync(
                        repository,
                        [
                            "update-ref",
                            branchRef,
                            externalCommit.Stdout.Trim(),
                            currentCommit,
                        ],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                ExternalTip = externalCommit.Stdout.Trim();
                InterceptionCount++;
            }

            return await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
        }

        private void RecordTemporaryPaths(
            IReadOnlyList<string> arguments,
            GitCommandOptions options)
        {
            if (arguments is ["worktree", "add", "--detach", "--no-checkout", ..])
            {
                TemporaryWorktreePath = arguments[4];
            }

            if (options.EnvironmentOverrides?.TryGetValue(
                    "GIT_INDEX_FILE",
                    out string? indexPath) == true)
            {
                TemporaryIndexPath = indexPath;
            }
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class MoveBranchAfterLiveReconciliationRunner(
        IGitCliRunner inner,
        RepositoryInfo liveRepository,
        string? externalStagePath) : IGitCliRunner
    {
        private int _publicationPending = 1;
        private int _movePending;
        private string? _branchRef;
        private string? _cleanupCommit;

        public int InterceptionCount { get; private set; }

        public bool LiveReconciliationObserved { get; private set; }

        public string? ExternalTip { get; private set; }

        public string? TemporaryIndexPath { get; private set; }

        public string? TemporaryWorktreePath { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            RecordTemporaryPaths(arguments, options);
            if (arguments.FirstOrDefault() == "update-ref"
                && arguments.Contains("beutl: stop tracking reserved project state")
                && Interlocked.Exchange(ref _publicationPending, 0) == 1)
            {
                _branchRef = arguments[3];
                _cleanupCommit = arguments[4];
                GitCommandResult result = await inner.RunAsync(
                        repository,
                        arguments,
                        options,
                        cancellationToken,
                        stderrProgress)
                    .ConfigureAwait(false);
                return result;
            }

            if (arguments.FirstOrDefault() == "update-index"
                && arguments.Contains("--force-remove")
                && options.EnvironmentOverrides?.TryGetValue("GIT_INDEX_FILE", out string? indexPath) == true
                && indexPath!.Contains(".beutl-index-", StringComparison.Ordinal))
            {
                GitCommandResult result = await inner.RunAsync(
                        repository,
                        arguments,
                        options,
                        cancellationToken,
                        stderrProgress)
                    .ConfigureAwait(false);
                if (_branchRef is not null && _cleanupCommit is not null)
                {
                    LiveReconciliationObserved = true;
                    _movePending = 1;
                }

                return result;
            }

            if (Volatile.Read(ref _movePending) == 1
                && arguments.FirstOrDefault() == "rev-parse"
                && arguments.Count == 4
                && arguments[1] == "--verify"
                && arguments[2] == "--quiet")
            {
                Interlocked.Exchange(ref _movePending, 0);
                GitCommandResult tree = await inner.RunAsync(
                        repository,
                        ["rev-parse", _cleanupCommit! + "^{tree}"],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                GitCommandResult externalCommit = await inner.RunAsync(
                        repository,
                        [
                            "commit-tree",
                            tree.Stdout.Trim(),
                            "-p",
                            _cleanupCommit!,
                            "-m",
                            "external branch movement after live reconciliation",
                        ],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                await inner.RunAsync(
                        repository,
                        ["update-ref", _branchRef!, externalCommit.Stdout.Trim(), _cleanupCommit!],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (externalStagePath is not null)
                {
                    await inner.RunAsync(
                            liveRepository,
                            ["add", "--", externalStagePath],
                            GitCommandOptions.Local,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                ExternalTip = externalCommit.Stdout.Trim();
                InterceptionCount++;
            }

            return await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
        }

        private void RecordTemporaryPaths(
            IReadOnlyList<string> arguments,
            GitCommandOptions options)
        {
            if (arguments is ["worktree", "add", "--detach", "--no-checkout", ..])
            {
                TemporaryWorktreePath = arguments[4];
            }

            if (options.EnvironmentOverrides?.TryGetValue(
                    "GIT_INDEX_FILE",
                    out string? indexPath) == true)
            {
                TemporaryIndexPath = indexPath;
            }
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class LostReservedCleanupObservationRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _publicationPending = 1;
        private int _observationPending = 1;
        private string? _refUpdateRepositoryRoot;

        public int ObservationFailures { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.FirstOrDefault() == "update-ref"
                && arguments.Contains("beutl: stop tracking reserved project state")
                && Interlocked.Exchange(ref _publicationPending, 0) == 1)
            {
                _refUpdateRepositoryRoot = repository.RepoRoot;
                GitCommandResult result = await inner.RunAsync(
                        repository,
                        arguments,
                        options,
                        cancellationToken,
                        stderrProgress)
                    .ConfigureAwait(false);
                throw new TimeoutException("simulated lost reserved-path ref update result");
            }

            if (_refUpdateRepositoryRoot is not null
                && string.Equals(
                    repository.RepoRoot,
                    _refUpdateRepositoryRoot,
                    StringComparison.Ordinal)
                && arguments.Count == 4
                && arguments[0] == "rev-parse"
                && arguments[1] == "--verify"
                && arguments[2] == "--quiet"
                && Interlocked.Exchange(ref _observationPending, 0) == 1)
            {
                ObservationFailures++;
                throw new TimeoutException("simulated one-shot reserved-path ref observation loss");
            }

            return await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

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
