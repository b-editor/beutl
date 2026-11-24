using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Language;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public sealed class GitRepositoryClonerTests : RealGitTestRepository
{
    private const string RepoId = "a3a8927c-bcf4-4f6e-91f4-64897320378e";
    private const string Url = "https://example.invalid/api/v3/git/" + RepoId + ".git";
    private const string Secret = "example-repository-secret";

    [TestCase("feature/demo", true)]
    [TestCase("refs/heads/feature/demo", true)]
    [TestCase("sample-tag", false)]
    [TestCase("refs/tags/sample-tag", false)]
    [TestCase("commit", false)]
    [TestCase("short-commit", false)]
    [TestCase("7abc123", true)]
    [TestCase("7def456", false)]
    public async Task Clones_a_branch_tag_or_commit_with_projects_in_root_first_order(string revision, bool branch)
    {
        await CommitFileAsync("Root.bep", "project", "first");
        string initial = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("tag", "sample-tag");
        await RunGitAsync("branch", "feature/demo");
        await RunGitAsync("branch", "7abc123");
        await RunGitAsync("tag", "7def456");
        await CommitFileAsync("README", "new", "advance main");
        string remote = await CreateBareCloneAsync();
        string destination = Path.Combine(CreateTemporaryDirectory(), "clone");
        Directory.CreateDirectory(destination);
        var progress = new RecordingProgress();
        var cloner = new GitRepositoryCloner(CreateInstalledLocator(), _ => Runner);

        string requested = revision switch
        {
            "commit" => initial,
            "short-commit" => initial[..8],
            _ => revision,
        };
        await cloner.CloneAsync(remote, destination, requested, progress);

        var workingCopy = new RepositoryInfo(destination, destination);
        Assert.That((await Runner.RunAsync(workingCopy, ["rev-parse", "HEAD"], GitCommandOptions.Local, CancellationToken.None)).Stdout.Trim(),
            Is.EqualTo(initial));
        string branchName = revision.StartsWith("refs/heads/", StringComparison.Ordinal) ? revision["refs/heads/".Length..] : revision;
        Assert.That((await Runner.RunAsync(workingCopy, ["branch", "--show-current"], GitCommandOptions.Local, CancellationToken.None)).Stdout.Trim(),
            Is.EqualTo(branch ? branchName : string.Empty));
        if (branch)
            Assert.That((await Runner.RunAsync(workingCopy, ["rev-parse", "--abbrev-ref", "@{upstream}"],
                GitCommandOptions.Local, CancellationToken.None)).Stdout.Trim(), Is.EqualTo($"origin/{branchName}"));
        Assert.That(progress.Messages, Is.Not.Empty);

        Directory.CreateDirectory(Path.Combine(destination, ".hidden"));
        Directory.CreateDirectory(Path.Combine(destination, "Scenes"));
        await File.WriteAllTextAsync(Path.Combine(destination, ".hidden", "Hidden.bep"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(destination, ".git", "NotAProject.bep"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(destination, "Scenes", "Nested.BEP"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(destination, "Notes.bproj"), string.Empty);

        Assert.That(GitRepositoryCloner.FindProjectFiles(destination).Select(Path.GetFileName),
            Is.EqualTo(new[] { "Root.bep", "Nested.BEP" }));
    }

    [Test]
    public async Task Clone_preserves_the_namespace_of_an_explicit_branch()
    {
        await CommitFileAsync("Project.bep", "tag", "Tag the initial project");
        await RunGitAsync("tag", "release");
        string output = CreateTemporaryDirectory();
        string destination = Path.Combine(output, "clone");
        Directory.CreateDirectory(destination);
        var cloner = new GitRepositoryCloner(CreateInstalledLocator(), _ => Runner);

        const string branchRef = "refs/heads/release";
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => cloner.CloneAsync(Root, destination, branchRef));
        Assert.That(failure!.Message, Is.EqualTo(Strings.HostedGit_InvalidRevision));
        Assert.That(Directory.GetFileSystemEntries(destination), Is.Empty);
        Assert.That(Directory.EnumerateDirectories(output), Is.EqualTo(new[] { destination }));

        await cloner.CloneAsync(Root, Path.Combine(output, "tag"), "release");
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "tag", "Project.bep")), Is.EqualTo("tag"));

        await CommitFileAsync("Project.bep", "branch", "Advance the project");
        await RunGitAsync("branch", "release");
        await cloner.CloneAsync(Root, destination, branchRef);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(destination, "Project.bep")), Is.EqualTo("branch"));
    }

    [Test]
    public async Task Authenticated_clone_sends_secret_to_helper_before_git_and_saves_only_clean_configuration()
    {
        await CommitFileAsync("Project.bproj", "project", "initial");
        string remote = await CreateBareCloneAsync();
        string folder = CreateTemporaryDirectory();
        string config = Path.Combine(folder, "gitconfig");
        // Git sees the clean HTTPS origin, while the transport reads a real local bare repo.
        string fileUrl = new Uri(remote + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/');
        await File.WriteAllTextAsync(config, $"[url \"{fileUrl}\"]\n\tinsteadOf = {Url}\n[clone]\n\tdefaultRemoteName = upstream\n");
        var environment = new Dictionary<string, string?>(IsolatedGitEnvironment) { ["GIT_CONFIG_GLOBAL"] = config };
        var runner = new CredentialProbeRunner(new GitCliRunner(GitPath, TimeSpan.FromSeconds(15), environment));
        var recorder = new RecordingGitCliRunner(runner);
        var cloner = new GitRepositoryCloner(CreateInstalledLocator(), _ => recorder);
        string target = Path.Combine(folder, "clone");
        var progress = new RecordingProgress();

        GitRemoteUrl firstCredential = GitRemoteUrl.WithHostedCredentials(new Uri(Url), Secret);
        await cloner.CloneAsync(firstCredential, target, progress: progress);

        var commands = recorder.Commands;
        int approve = commands.FindIndex(call => call.Arguments.Last() == "approve");
        int clone = commands.FindIndex(call => GetGitSubcommand(call.Arguments) == "clone");
        Assert.That(approve, Is.GreaterThanOrEqualTo(0).And.LessThan(clone));
        Assert.That(commands[approve].Options.StandardInput, Does.Contain($"password={Secret}\n"));
        Assert.That(commands[clone].Arguments, Does.Contain(Url));
        Assert.That(commands[clone].Arguments, Does.Contain($"credential.{Url}.helper=libsecret"));
        Assert.That(commands.SelectMany(call => call.Arguments).Any(arg => arg.Contains(Secret, StringComparison.Ordinal)), Is.False);
        Assert.That(progress.Messages.Any(line => line.Contains(Secret, StringComparison.Ordinal)), Is.False);
        Assert.That(progress.Messages, Does.Contain("Diagnostic ***"));

        var local = new RepositoryInfo(target, target);
        Assert.That(await GetLocalAsync("remote.origin.url"), Is.EqualTo(Url));
        Assert.That(await GetLocalAsync($"credential.{Url}.useHttpPath"), Is.EqualTo("true"));
        Assert.That(await GetLocalAsync($"credential.{Url}.username"), Is.EqualTo(firstCredential.Username));
        Assert.That(await GetLocalAsync($"credential.{Url}.helper"), Is.EqualTo("libsecret"));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(target, ".git", "config")), Does.Not.Contain(Secret));

        GitRemoteUrl secondCredential = GitRemoteUrl.WithHostedCredentials(new Uri(Url), "another-example-token");
        await Assert.ThrowsAsync<GitOperationException>(() =>
            cloner.CloneAsync(secondCredential, Path.Combine(folder, "failed"), "missing-branch"));
        Assert.That(secondCredential.Username, Is.Not.EqualTo(firstCredential.Username));
        Assert.That(runner.Credentials[firstCredential.Username!], Is.EqualTo(Secret));
        Assert.That(await GetLocalAsync($"credential.{Url}.username"), Is.EqualTo(firstCredential.Username));

        async Task<string> GetLocalAsync(string key)
            => (await Runner.RunAsync(local, ["config", "--local", "--get", key], GitCommandOptions.Local, CancellationToken.None)).Stdout.Trim();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Clone_hydrates_LFS_files_despite_inherited_skip_settings(bool detached)
    {
        await RequireGitLfsAsync();
        await RunGitAsync("lfs", "install", "--local", "--skip-repo");
        await RunGitAsync("lfs", "track", "*.mp4");
        await RunGitAsync("add", "--", ".gitattributes");
        const string media = "sample media content";
        await CommitFileAsync("media.mp4", media, "Store an LFS object");
        string folder = CreateTemporaryDirectory();
        string config = Path.Combine(folder, "gitconfig");
        const string filters = "[lfs]\n\tfetchinclude = *.txt\n\tfetchexclude = *.mp4\n";
        await File.WriteAllTextAsync(config, filters);
        var environment = new Dictionary<string, string?>(IsolatedGitEnvironment)
        {
            ["GIT_CONFIG_GLOBAL"] = config,
            ["GIT_LFS_SKIP_SMUDGE"] = "1",
        };
        var runner = new GitCliRunner(GitPath, TimeSpan.FromSeconds(15), environment);
        string destination = Path.Combine(folder, "clone-with-media");
        string? revision = detached ? (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim() : null;
        var cloner = new GitRepositoryCloner(CreateInstalledLocator(lfsInstalled: true), _ => runner);

        await cloner.CloneAsync(Root, destination, revision);

        Assert.That(await File.ReadAllTextAsync(Path.Combine(destination, "media.mp4")), Is.EqualTo(media));
        Assert.That(await File.ReadAllTextAsync(config), Is.EqualTo(filters));
    }

    [Test]
    public async Task Clone_without_LFS_keeps_the_destination_empty_when_the_target_requires_it()
    {
        await CommitFileAsync("Project.bep", "project", "Initial project");
        string initial = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await CommitMissingLfsObjectAsync();
        string output = CreateTemporaryDirectory();
        string destination = Path.Combine(output, "clone");
        Directory.CreateDirectory(destination);
        var cloner = new GitRepositoryCloner(CreateInstalledLocator(), _ => Runner);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => cloner.CloneAsync(Root, destination));
        Assert.That(failure!.Message, Is.EqualTo(Strings.HostedGit_CloneRequiresLfs));
        Assert.That(Directory.GetFileSystemEntries(output), Is.EqualTo(new[] { destination }));
        Assert.That(Directory.GetFileSystemEntries(destination), Is.Empty);

        await cloner.CloneAsync(Root, destination, initial);
        Assert.That(File.Exists(Path.Combine(destination, "Project.bep")), Is.True);
    }

    [Test]
    public async Task Clone_keeps_the_destination_empty_when_an_LFS_download_fails()
    {
        await RequireGitLfsAsync();
        await CommitMissingLfsObjectAsync();
        string output = CreateTemporaryDirectory();
        string config = Path.Combine(output, "gitconfig");
        const string settings = "[lfs]\n\tskipdownloaderrors = true\n";
        await File.WriteAllTextAsync(config, settings);
        var environment = new Dictionary<string, string?>(IsolatedGitEnvironment)
        {
            ["GIT_CONFIG_GLOBAL"] = config,
            ["GIT_LFS_SKIP_DOWNLOAD_ERRORS"] = "1",
        };
        var runner = new GitCliRunner(GitPath, TimeSpan.FromSeconds(15), environment);
        var cloner = new GitRepositoryCloner(CreateInstalledLocator(lfsInstalled: true), _ => runner);
        string destination = Path.Combine(output, "clone");
        Directory.CreateDirectory(destination);

        await Assert.ThrowsAsync<GitOperationException>(() => cloner.CloneAsync(Root, destination));

        Assert.That(Directory.GetFileSystemEntries(destination), Is.Empty);
        Assert.That(Directory.EnumerateDirectories(output), Is.EqualTo(new[] { destination }));
        Assert.That(await File.ReadAllTextAsync(config), Is.EqualTo(settings));
    }

    private async Task CommitMissingLfsObjectAsync()
    {
        await CommitFileAsync(".gitattributes", "*.mp4 filter=lfs diff=lfs merge=lfs -text\n", "Set LFS attributes");
        string pointer = $"version https://git-lfs.github.com/spec/v1\noid sha256:{new string('a', 64)}\nsize 12\n";
        await CommitFileAsync("media.mp4", pointer, "Store an LFS pointer");
    }

    private async Task RequireGitLfsAsync()
    {
        try
        {
            await RunGitAsync("lfs", "version");
        }
        catch (GitOperationException)
        {
            Assert.Ignore("Git LFS is required for this test.");
        }
    }

    [Test]
    public async Task Cancellation_removes_staging_only_and_keeps_an_existing_empty_destination()
    {
        await CommitFileAsync("Project.bproj", "project", "initial");
        string remote = await CreateBareCloneAsync();
        string output = CreateTemporaryDirectory();
        string target = Path.Combine(output, "existing");
        Directory.CreateDirectory(target);
        using var cancellation = new CancellationTokenSource();
        var cloner = new GitRepositoryCloner(CreateInstalledLocator(), _ => new CancelCloneRunner(Runner, cancellation));

        await Assert.CatchAsync<OperationCanceledException>(() => cloner.CloneAsync(remote, target, cancellationToken: cancellation.Token));
        Assert.That(Directory.GetFileSystemEntries(output), Is.EqualTo(new[] { target }));
        Assert.That(Directory.GetFileSystemEntries(target), Is.Empty);
    }

    [Test]
    public async Task Refuses_a_non_empty_destination()
    {
        string destination = CreateTemporaryDirectory();
        string sentinel = Path.Combine(destination, "keep");
        File.WriteAllText(sentinel, "user data");
        var cloner = new GitRepositoryCloner(CreateInstalledLocator(), _ => Runner);

        await Assert.ThrowsAsync<IOException>(() => cloner.CloneAsync("unused", destination));
        Assert.That(File.ReadAllText(sentinel), Is.EqualTo("user data"));
    }

    [Test]
    public async Task Locator_skips_missing_projects_and_finds_a_nested_project_in_a_push_only_origin()
    {
        string nested = Path.Combine(Root, "nested");
        Directory.CreateDirectory(nested);
        string project = Path.Combine(nested, "current.bproj");
        File.WriteAllText(project, string.Empty);
        await RunGitAsync("config", "remote.origin.pushurl", $"{BeutlApiApplication.BaseUrl}/api/v3/git/{RepoId}.git");
        var view = new ViewConfig();
        view.RecentProjects.Add(Path.Combine(Root, "missing.bproj"));
        view.RecentProjects.Add(project);
        var locator = new LocalRepositoryLocator(CreateInstalledLocator());

        Assert.That(await locator.FindRecentProjectAsync(view, Guid.Parse(RepoId), CancellationToken.None), Is.EqualTo(project));
        Assert.That(await locator.FindRecentProjectAsync(view, Guid.NewGuid(), CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Locator_matches_projects_to_the_advertised_git_origin()
    {
        await RunGitAsync("remote", "add", "origin", Url);
        string first = Path.Combine(Root, "First.bproj");
        string second = Path.Combine(Root, "Second.bproj");
        File.WriteAllText(first, string.Empty);
        File.WriteAllText(second, string.Empty);
        var locator = new LocalRepositoryLocator(CreateInstalledLocator());
        GitRepositorySummary repository = new(RepoId, "Production", Url, default, default);
        string[] paths = [Path.Combine(Root, "Missing.bproj"), first, second];

        var matching = await locator.FindProjectRepositoriesAsync(paths, [repository], CancellationToken.None);
        Assert.That(matching.Keys, Is.EquivalentTo(new[] { first, second }));
        Assert.That(matching.Values, Is.All.EqualTo(Guid.Parse(RepoId)));

        repository = repository with { Url = Url.Replace("example.invalid", "another.invalid", StringComparison.Ordinal) };
        Assert.That(await locator.FindProjectRepositoriesAsync(paths, [repository], CancellationToken.None), Is.Empty);
    }

    private async Task<string> CreateBareCloneAsync()
    {
        string bare = Path.Combine(CreateTemporaryDirectory(), "remote.git");
        await RunGitAsync("clone", "--bare", "--no-hardlinks", Root, bare);
        return bare;
    }

    [Test]
    public async Task Locator_reuses_a_local_checkout_only_for_the_requested_branch_or_commit()
    {
        await CommitFileAsync("Project.bep", "initial", "Initial project");
        await RunGitAsync("branch", "another-branch");
        string branch = (await RunGitAsync("branch", "--show-current")).Stdout.Trim();
        string commit = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("remote", "add", "origin", $"{BeutlApiApplication.BaseUrl}/api/v3/git/{RepoId}.git");
        var locator = new LocalRepositoryLocator(CreateInstalledLocator());
        string project = Path.Combine(Root, "Project.bep");

        Assert.That(await locator.FindProjectAsync([project], Guid.Parse(RepoId), CancellationToken.None, "another-branch"),
            Is.Null);
        Assert.That(await locator.FindProjectAsync([project], Guid.Parse(RepoId), CancellationToken.None, branch),
            Is.EqualTo(project));
        Assert.That(await locator.FindProjectAsync([project], Guid.Parse(RepoId), CancellationToken.None, commit[..8]),
            Is.EqualTo(project));
        await CommitFileAsync("Project.bep", "new version", "Advance the project");
        string advanced = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string abbreviated = commit[..8];
        await RunGitAsync("switch", "-c", abbreviated);
        Assert.That(await locator.FindProjectAsync([project], Guid.Parse(RepoId), CancellationToken.None, abbreviated),
            Is.Null);
        await RunGitAsync("switch", "--detach", commit);
        Assert.That(await locator.FindProjectAsync([project], Guid.Parse(RepoId), CancellationToken.None, abbreviated),
            Is.EqualTo(project));

        string originBranch = $"refs/remotes/origin/{abbreviated}";
        await RunGitAsync("update-ref", originBranch, advanced);
        await RunGitAsync("switch", "-C", abbreviated, "--track", "--", originBranch);
        Assert.That(await locator.FindProjectAsync([project], Guid.Parse(RepoId), CancellationToken.None, abbreviated),
            Is.EqualTo(project));
    }

    [Test]
    public async Task Locator_resolves_a_tag_before_a_local_only_branch_of_the_same_name()
    {
        await CommitFileAsync("Project.bep", "released", "Initial release");
        await RunGitAsync("tag", "-a", "release", "-m", "Initial release");
        await RunGitAsync("switch", "-c", "release");
        await CommitFileAsync("Project.bep", "later", "Advance the local branch");
        await RunGitAsync("remote", "add", "origin", $"{BeutlApiApplication.BaseUrl}/api/v3/git/{RepoId}.git");
        var locator = new LocalRepositoryLocator(CreateInstalledLocator());
        string project = Path.Combine(Root, "Project.bep");
        var repositoryId = Guid.Parse(RepoId);

        Assert.That(await locator.FindProjectAsync([project], repositoryId, CancellationToken.None, "release"), Is.Null);
        Assert.That(await locator.FindProjectAsync([project], repositoryId, CancellationToken.None, "refs/heads/release"),
            Is.EqualTo(project));

        const string originRelease = "refs/remotes/origin/release";
        await RunGitAsync("update-ref", originRelease, "HEAD");
        Assert.That(await locator.FindProjectAsync([project], repositoryId, CancellationToken.None, "release"), Is.EqualTo(project));

        await RunGitAsync("update-ref", "-d", originRelease);
        await RunGitAsync("switch", "--detach", "refs/tags/release");
        Assert.That(await locator.FindProjectAsync([project], repositoryId, CancellationToken.None, "release"), Is.EqualTo(project));
    }

    private sealed class CredentialProbeRunner(IGitCliRunner inner) : DelegatingGitCliRunner(inner)
    {
        public Dictionary<string, string> Credentials { get; } = new(StringComparer.Ordinal);

        public override Task<GitCommandResult> RunAsync(RepositoryInfo repo, IReadOnlyList<string> arguments,
            GitCommandOptions options, CancellationToken token, IProgress<string>? progress = null)
        {
            if (arguments.Contains("--get-urlmatch"))
                return Task.FromResult(new GitCommandResult(0, "store --file=/tmp/cache\0", ""));
            if (arguments.SequenceEqual(["help", "-a"]))
                return Task.FromResult(new GitCommandResult(0, "credential-libsecret", ""));
            if (arguments.SequenceEqual(["--exec-path"]))
                return Task.FromResult(new GitCommandResult(0, Path.GetTempPath(), ""));
            if (GetGitSubcommand(arguments) == "credential")
            {
                string usernameKey = $"credential.{Url}.username=";
                string username = arguments.Single(arg => arg.StartsWith(usernameKey, StringComparison.Ordinal))[usernameKey.Length..];
                if (arguments[^1] == "approve")
                {
                    const string passwordKey = "password=";
                    string password = options.StandardInput!.Split('\n')
                        .Single(line => line.StartsWith(passwordKey, StringComparison.Ordinal))[passwordKey.Length..];
                    Credentials[username] = password;
                }
                string output = arguments[^1] == "fill" ? $"username={username}\npassword={Credentials[username]}\n" : "";
                return Task.FromResult(new GitCommandResult(0, output, ""));
            }
            if (GetGitSubcommand(arguments) == "clone")
                progress?.Report($"Diagnostic {Secret}");
            return base.RunAsync(repo, arguments, options, token, progress);
        }
    }

    private sealed class CancelCloneRunner(IGitCliRunner inner, CancellationTokenSource cancellation) : DelegatingGitCliRunner(inner)
    {
        public override async Task<GitCommandResult> RunAsync(RepositoryInfo repo, IReadOnlyList<string> arguments,
            GitCommandOptions options, CancellationToken token, IProgress<string>? progress = null)
        {
            var result = await base.RunAsync(repo, arguments, options, token, progress);
            if (GetGitSubcommand(arguments) == "clone")
                cancellation.Cancel();
            return result;
        }
    }
}
