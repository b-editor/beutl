using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public sealed class NestedRepositoryTests : RealGitTestRepository
{
    [Test]
    public async Task Discovery_finds_the_enclosing_repository_and_builds_a_scoped_pathspec()
    {
        string projectRoot = CreateProjectDirectory();
        using GitCliVersionControlService service = CreateUnassociatedService();

        RepositoryInfo? discovered = await service.DiscoverRepositoryAsync(
            projectRoot,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(discovered, Is.Not.Null);
            Assert.That(discovered!.RepoRoot, Is.EqualTo(Root));
            Assert.That(discovered.ProjectRoot, Is.EqualTo(projectRoot));
            Assert.That(discovered.IsNestedInForeignRepo, Is.True);
            Assert.That(discovered.Pathspec, Is.EqualTo("nested/project"));
        });
    }

    [Test]
    public async Task Initialize_requires_consent_and_commits_only_the_nested_project()
    {
        string projectRoot = CreateProjectDirectory();
        string projectFile = Path.Combine(projectRoot, "project.bep");
        string foreignFile = Path.Combine(Root, "foreign.txt");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(foreignFile, "foreign\n");
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
            Assert.That(exception!.Repository.RepoRoot, Is.EqualTo(Root));
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
        GitCommandResult foreignStatus = await RunGitAsync(
            "status",
            "--porcelain",
            "--",
            "foreign.txt");
        Assert.Multiple(() =>
        {
            Assert.That(service.Repository, Is.EqualTo(selectedRepository));
            Assert.That(File.ReadAllText(Path.Combine(projectRoot, ".gitignore")),
                Is.EqualTo("**/.beutl/\n*.tmp\n"));
            Assert.That(committed.Stdout, Does.Contain("nested/project/project.bep"));
            Assert.That(committed.Stdout, Does.Contain("nested/project/.gitignore"));
            Assert.That(committed.Stdout, Does.Not.Contain("foreign.txt"));
            Assert.That(foreignStatus.Stdout, Does.StartWith("?? foreign.txt"));
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

        await service.RestoreWorktreeFromAsync(targetSha, CancellationToken.None);

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
}
