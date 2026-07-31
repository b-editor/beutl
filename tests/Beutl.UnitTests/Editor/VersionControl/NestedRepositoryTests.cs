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
        await service.CreateBranchAsync(
            "whole-repository",
            "HEAD",
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
}
