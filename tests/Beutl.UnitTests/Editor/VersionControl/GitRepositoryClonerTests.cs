using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Configuration;
using Beutl.Editor.VersionControl;

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
    public async Task Clones_a_branch_tag_or_commit_with_projects_in_root_first_order(string revision, bool branch)
    {
        await CommitFileAsync("Root.bep", "project", "first");
        string initial = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("tag", "sample-tag");
        await RunGitAsync("branch", "feature/demo");
        await CommitFileAsync("README", "new", "advance main");
        string remote = await CreateBareCloneAsync();
        string destination = Path.Combine(CreateTemporaryDirectory(), "clone");
        Directory.CreateDirectory(destination);
        var progress = new RecordingProgress();
        var cloner = new GitRepositoryCloner(CreateInstalledLocator(), _ => Runner);

        await cloner.CloneAsync(remote, destination, revision == "commit" ? initial : revision, progress);

        var workingCopy = new RepositoryInfo(destination, destination);
        Assert.That((await Runner.RunAsync(workingCopy, ["rev-parse", "HEAD"], GitCommandOptions.Local, CancellationToken.None)).Stdout.Trim(),
            Is.EqualTo(initial));
        Assert.That((await Runner.RunAsync(workingCopy, ["branch", "--show-current"], GitCommandOptions.Local, CancellationToken.None)).Stdout.Trim(),
            Is.EqualTo(branch ? "feature/demo" : string.Empty));
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
    public async Task Authenticated_clone_sends_secret_to_helper_before_git_and_saves_only_clean_configuration()
    {
        await CommitFileAsync("Project.bproj", "project", "initial");
        string remote = await CreateBareCloneAsync();
        string folder = CreateTemporaryDirectory();
        string config = Path.Combine(folder, "gitconfig");
        // Git sees the clean HTTPS origin, while the transport reads a real local bare repo.
        string fileUrl = new Uri(remote + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/');
        await File.WriteAllTextAsync(config, $"[url \"{fileUrl}\"]\n\tinsteadOf = {Url}\n");
        var environment = new Dictionary<string, string?>(IsolatedGitEnvironment) { ["GIT_CONFIG_GLOBAL"] = config };
        var runner = new CredentialProbeRunner(new GitCliRunner(GitPath, TimeSpan.FromSeconds(15), environment));
        var recorder = new RecordingGitCliRunner(runner);
        var cloner = new GitRepositoryCloner(CreateInstalledLocator(), _ => recorder);
        string target = Path.Combine(folder, "clone");
        var progress = new RecordingProgress();

        await cloner.CloneAsync(GitRemoteUrl.WithHostedCredentials(new Uri(Url), Secret), target, progress: progress);

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
        Assert.That(await GetLocalAsync($"credential.{Url}.username"), Is.EqualTo("git"));
        Assert.That(await GetLocalAsync($"credential.{Url}.helper"), Is.EqualTo("libsecret"));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(target, ".git", "config")), Does.Not.Contain(Secret));

        async Task<string> GetLocalAsync(string key)
            => (await Runner.RunAsync(local, ["config", "--local", "--get", key], GitCommandOptions.Local, CancellationToken.None)).Stdout.Trim();
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

    private sealed class CredentialProbeRunner(IGitCliRunner inner) : DelegatingGitCliRunner(inner)
    {
        public override Task<GitCommandResult> RunAsync(RepositoryInfo repo, IReadOnlyList<string> arguments,
            GitCommandOptions options, CancellationToken token, IProgress<string>? progress = null)
        {
            if (arguments.Contains("--get-urlmatch"))
                return Task.FromResult(new GitCommandResult(0, "store\0", ""));
            if (arguments.SequenceEqual(["help", "-a"]))
                return Task.FromResult(new GitCommandResult(0, "credential-libsecret", ""));
            if (arguments.SequenceEqual(["--exec-path"]))
                return Task.FromResult(new GitCommandResult(0, Path.GetTempPath(), ""));
            if (GetGitSubcommand(arguments) == "credential")
                return Task.FromResult(new GitCommandResult(0, arguments[^1] == "fill" ? $"username=git\npassword={Secret}\n" : "", ""));
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
