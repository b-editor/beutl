using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public sealed class RemoteOperationsTests : RealGitTestRepository
{
    [Test]
    public async Task SetRemote_and_push_publish_head_with_progress_and_upstream()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        var progress = new RecordingProgress();

        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        RemoteOpResult result = await service.PushAsync(progress, CancellationToken.None);
        IReadOnlyList<RemoteInfo> remotes = await service.GetRemotesAsync(CancellationToken.None);
        IReadOnlyList<BranchInfo> branches = await service.GetBranchesAsync(CancellationToken.None);
        string localHead = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string remoteHead = await ReadRemoteHeadAsync(remoteRoot);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(remotes, Is.EqualTo(new[] { new RemoteInfo("origin", remoteRoot) }));
            Assert.That(branches.Single(branch => branch.Name == "main").UpstreamName,
                Is.EqualTo("origin/main"));
            Assert.That(remoteHead, Is.EqualTo(localHead));
            Assert.That(progress.Messages, Is.Not.Empty);
        });
    }

    [Test]
    public async Task PullFastForward_updates_the_worktree_from_a_local_bare_remote()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");

        RemoteOpResult result = await service.PullFastForwardAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("from peer\n"));
        });
    }

    [Test]
    public async Task Diverged_pull_and_push_preserve_both_sides()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "remote\n", "remote update");
        await CommitFileAsync("project.bep", "local\n", "local update");
        string localHeadBefore = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string remoteHeadBefore = await ReadRemoteHeadAsync(remoteRoot);

        RemoteOpResult pull = await service.PullFastForwardAsync(CancellationToken.None);
        RemoteOpResult push = await service.PushAsync(progress: null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Diverged>());
            Assert.That(push, Is.TypeOf<RemoteOpResult.Diverged>());
            Assert.That(
                (RunGitAsync("rev-parse", "HEAD").GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(localHeadBefore));
            Assert.That(ReadRemoteHeadAsync(remoteRoot).GetAwaiter().GetResult(),
                Is.EqualTo(remoteHeadBefore));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("local\n"));
        });
    }

    [TestCase("fatal: Authentication failed for 'https://example.invalid/repo.git/'")]
    [TestCase("git@example.invalid: Permission denied (publickey).")]
    public void Authentication_failures_are_classified_with_actionable_guidance(string stderr)
    {
        RemoteOpResult result = GitCliVersionControlService.MapRemoteFailure(
            new GitOperationException(128, stderr));

        Assert.That(result, Is.TypeOf<RemoteOpResult.AuthFailed>());
        Assert.That(((RemoteOpResult.AuthFailed)result).Guidance, Is.Not.Empty);
    }

    [TestCase("fatal: unable to access 'https://example.invalid/': Could not resolve host")]
    [TestCase("ssh: connect to host example.invalid port 22: Network is unreachable")]
    public void Network_failures_are_classified_as_offline(string stderr)
    {
        RemoteOpResult result = GitCliVersionControlService.MapRemoteFailure(
            new GitOperationException(128, stderr));

        Assert.That(result, Is.TypeOf<RemoteOpResult.Offline>());
    }

    [Test]
    public void Unclassified_failures_preserve_stderr_verbatim()
    {
        const string stderr = "fatal: the remote rejected an unsupported option\r\n";

        RemoteOpResult result = GitCliVersionControlService.MapRemoteFailure(
            new GitOperationException(128, stderr));

        Assert.That(
            result,
            Is.EqualTo(new RemoteOpResult.Failed(stderr)));
    }

    private GitCliVersionControlService CreateService()
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner());
    }

    private async Task<string> CreateBareRemoteAsync()
    {
        string remoteRoot = CreateTemporaryDirectory();
        var remote = new RepositoryInfo(remoteRoot, remoteRoot);
        await CreateRunner().RunAsync(
            remote,
            ["init", "--bare", "-b", "main"],
            networkOperation: false,
            CancellationToken.None);
        return remoteRoot;
    }

    private async Task<RepositoryInfo> CloneRemoteAsync(string remoteRoot)
    {
        string peerRoot = CreateTemporaryDirectory();
        await CreateRunner().RunAsync(
            Repository,
            ["clone", "--branch", "main", remoteRoot, peerRoot],
            networkOperation: false,
            CancellationToken.None);
        var peer = new RepositoryInfo(peerRoot, peerRoot);
        GitCliRunner runner = CreateRunner();
        await runner.RunAsync(
            peer,
            ["config", "user.name", "Peer Test"],
            networkOperation: false,
            CancellationToken.None);
        await runner.RunAsync(
            peer,
            ["config", "user.email", "peer@example.invalid"],
            networkOperation: false,
            CancellationToken.None);
        await runner.RunAsync(
            peer,
            ["config", "commit.gpgsign", "false"],
            networkOperation: false,
            CancellationToken.None);
        return peer;
    }

    private async Task CommitInRepositoryAsync(
        RepositoryInfo repository,
        string relativePath,
        string contents,
        string message)
    {
        string path = Path.Combine(repository.RepoRoot, relativePath);
        await File.WriteAllTextAsync(path, contents);
        GitCliRunner runner = CreateRunner();
        await runner.RunAsync(
            repository,
            ["add", "--", relativePath],
            networkOperation: false,
            CancellationToken.None);
        await runner.RunAsync(
            repository,
            ["commit", "-m", message],
            networkOperation: false,
            CancellationToken.None);
        await runner.RunAsync(
            repository,
            ["push"],
            networkOperation: false,
            CancellationToken.None);
    }

    private async Task<string> ReadRemoteHeadAsync(string remoteRoot)
    {
        var remote = new RepositoryInfo(remoteRoot, remoteRoot);
        GitCommandResult result = await CreateRunner().RunAsync(
            remote,
            ["rev-parse", "refs/heads/main"],
            networkOperation: false,
            CancellationToken.None);
        return result.Stdout.Trim();
    }

    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Messages { get; } = [];

        public void Report(string value)
        {
            Messages.Add(value);
        }
    }
}
