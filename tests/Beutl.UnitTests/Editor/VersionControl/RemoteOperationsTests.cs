using System.Reflection;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
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
    public async Task Push_offers_Beutls_LFS_agent_to_Git_LFS_for_uploads_only()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        var runner = new InterceptingRunner(CreateRunner(), static (_, _, _) => false, before: null, after: null);
        using var service = CreateService(runner: runner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);

        RemoteOpResult result = await service.PushAsync(progress: null, CancellationToken.None);
        IReadOnlyList<string> expected = HostedGitLfsTransferAgent.GitConfigArguments(
            Environment.ProcessPath, Assembly.GetEntryAssembly()?.Location);
        IReadOnlyList<string> push = runner.Commands.Single(arguments => GetGitSubcommand(arguments) == "push");
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(expected, Is.Not.Empty);
            Assert.That(push.Take(expected.Count), Is.EqualTo(expected));
        });

        // Git LFS reads the configuration and offers the agent for uploads, never downloads.
        try
        {
            await RunGitAsync("lfs", "version");
        }
        catch (GitOperationException)
        {
            Assert.Ignore("Git LFS is not installed");
        }

        GitCommandResult environment = await RunGitAsync([.. expected, "lfs", "env"]);

        Assert.Multiple(() =>
        {
            Assert.That(environment.Stdout, Does.Match(@"(?m)^UploadTransfers=.*\bbeutl-tus\b"));
            Assert.That(environment.Stdout, Does.Not.Match(@"(?m)^DownloadTransfers=.*\bbeutl-tus\b"));
        });
    }

    [Test]
    public async Task Branch_list_includes_origin_only_branches_without_blocking_a_local_name()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        await PublishOriginOnlyBranchAsync("feature", "feature\n");
        await RunGitAsync("remote", "set-head", "origin", "main");

        IReadOnlyList<BranchInfo> branches = await service.GetBranchesAsync(CancellationToken.None);
        bool canCreateSameName = await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            transaction => transaction.CanCreateBranchAsync("feature", CancellationToken.None),
            CancellationToken.None);

        await Assert.MultipleAsync(async () =>
        {
            // origin/HEAD only names the default branch, and origin/main already has a local branch.
            Assert.That(
                branches,
                Is.EqualTo(new[]
                {
                    new BranchInfo("main", true, "origin/main"),
                    new BranchInfo("feature", false, null, IsRemote: true),
                }));
            Assert.That(canCreateSameName, Is.True);
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await service.ExecuteExclusiveAsync(
                    transaction => transaction.SwitchBranchAsync("origin/feature", CancellationToken.None),
                    CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await service.ExecuteExclusiveAsync(
                    transaction => transaction.SwitchBranchAsync("Feature", CancellationToken.None),
                    CancellationToken.None));
        });
    }

    [Test]
    public async Task SwitchBranchAsync_checks_out_an_origin_only_branch_and_tracks_it()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        await PublishOriginOnlyBranchAsync("feature", "feature\n");
        string originTip = (await RunGitAsync("rev-parse", "refs/remotes/origin/feature"))
            .Stdout.Trim();

        // A branch switch prefetches the LFS content of the target before it checks it out.
        await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await transaction.PrefetchBranchLfsObjectsAsync("feature", CancellationToken.None);
                await transaction.SwitchBranchAsync("feature", CancellationToken.None);
                return true;
            },
            CancellationToken.None);

        string currentBranch = (await RunGitAsync("branch", "--show-current")).Stdout.Trim();
        string localTip = (await RunGitAsync("rev-parse", "refs/heads/feature")).Stdout.Trim();
        string upstream = (await RunGitAsync(
                "for-each-ref",
                "--format=%(upstream:short)",
                "refs/heads/feature"))
            .Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(currentBranch, Is.EqualTo("feature"));
            Assert.That(localTip, Is.EqualTo(originTip));
            Assert.That(upstream, Is.EqualTo("origin/feature"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("feature\n"));
        });
    }

    [Test]
    public async Task Push_uses_the_configured_origin_destination_when_branch_names_differ()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string originRoot = await CreateBareRemoteAsync();
        var interceptingRunner = new InterceptingRunner(
            CreateRunner(),
            static (_, arguments, _) => GetGitSubcommand(arguments) == "push",
            async (_, _, _) => await RunGitAsync("switch", "main"),
            after: null,
            interceptOnMatch: 2);
        using var service = CreateService(runner: interceptingRunner);
        await service.SetRemoteAsync(originRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        await RunGitAsync("switch", "-c", "release");
        await RunGitAsync("branch", "--set-upstream-to=origin/main", "release");
        await RunGitAsync("update-ref", "-d", "refs/remotes/origin/main");
        await CommitFileAsync("project.bep", "release\n", "release update");
        string releaseTip = (await RunGitAsync("rev-parse", "refs/heads/release"))
            .Stdout.Trim();

        RemoteOpResult result = await service.PushAsync(
            progress: null,
            CancellationToken.None);
        string originMain = await ReadRemoteRefAsync(originRoot, "refs/heads/main");
        string originRelease = await ListRemoteRefAsync(originRoot, "refs/heads/release");
        string upstream = (await RunGitAsync(
                "for-each-ref",
                "--format=%(upstream:short)",
                "refs/heads/release"))
            .Stdout.Trim();
        string currentBranch = (await RunGitAsync("branch", "--show-current")).Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(interceptingRunner.InterceptionCount, Is.EqualTo(1));
            Assert.That(currentBranch, Is.EqualTo("main"));
            Assert.That(originMain, Is.EqualTo(releaseTip));
            Assert.That(originRelease, Is.Empty);
            Assert.That(upstream, Is.EqualTo("origin/main"));
        });
    }

    [Test]
    public async Task Push_preserves_other_remote_tracking_and_targets_same_named_origin_branch()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string originRoot = await CreateBareRemoteAsync();
        string upstreamRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(originRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        string originMainBefore = await ReadRemoteRefAsync(originRoot, "refs/heads/main");
        await RunGitAsync("remote", "add", "upstream", upstreamRoot);
        await RunGitAsync("switch", "-c", "release");
        await RunGitAsync("push", "upstream", "refs/heads/release:refs/heads/integration");
        await RunGitAsync("branch", "--set-upstream-to=upstream/integration", "release");
        await RunGitAsync("update-ref", "-d", "refs/remotes/upstream/integration");
        string upstreamIntegrationBefore = await ReadRemoteRefAsync(
            upstreamRoot,
            "refs/heads/integration");
        await CommitFileAsync("project.bep", "release\n", "release update");
        string releaseTip = (await RunGitAsync("rev-parse", "refs/heads/release"))
            .Stdout.Trim();

        RemoteOpResult result = await service.PushAsync(
            progress: null,
            CancellationToken.None);
        string originMainAfter = await ReadRemoteRefAsync(originRoot, "refs/heads/main");
        string originRelease = await ReadRemoteRefAsync(originRoot, "refs/heads/release");
        string upstreamIntegrationAfter = await ReadRemoteRefAsync(
            upstreamRoot,
            "refs/heads/integration");
        string upstream = (await RunGitAsync(
                "for-each-ref",
                "--format=%(upstream:short)",
                "refs/heads/release"))
            .Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(originMainAfter, Is.EqualTo(originMainBefore));
            Assert.That(originRelease, Is.EqualTo(releaseTip));
            Assert.That(upstreamIntegrationAfter, Is.EqualTo(upstreamIntegrationBefore));
            Assert.That(upstream, Is.EqualTo("upstream/integration"));
        });
    }

    [Test]
    public async Task SetRemote_keeps_separate_push_urls()
    {
        // Like git remote set-url, changing the URL leaves push URLs the user configured on purpose.
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string originalRemote = await CreateBareRemoteAsync();
        string pushRemote = await CreateBareRemoteAsync();
        string secondPushRemote = await CreateBareRemoteAsync();
        string replacementRemote = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(originalRemote, CancellationToken.None);
        await RunGitAsync(
            "remote",
            "set-url",
            "--push",
            "origin",
            pushRemote);
        await RunGitAsync(
            "remote",
            "set-url",
            "--add",
            "--push",
            "origin",
            secondPushRemote);

        await service.SetRemoteAsync(replacementRemote, CancellationToken.None);
        string fetchUrl = (await RunGitAsync("remote", "get-url", "origin")).Stdout.Trim();
        string[] pushUrls = (await RunGitAsync(
                "remote",
                "get-url",
                "--push",
                "--all",
                "origin"))
            .Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Multiple(() =>
        {
            Assert.That(fetchUrl, Is.EqualTo(replacementRemote));
            Assert.That(pushUrls, Is.EqualTo(new[] { pushRemote, secondPushRemote }));
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

        RemoteOpResult pull = await PullAsync(service);

        Assert.Multiple(() =>
        {
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("from peer\n"));
        });
    }

    [Test]
    public async Task Pull_refuses_a_branch_that_tracks_another_remote()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string originRoot = await CreateBareRemoteAsync();
        string upstreamRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(originRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());

        await RunGitAsync("remote", "add", "upstream", upstreamRoot);
        await RunGitAsync("push", "upstream", "main");
        await RunGitAsync("branch", "--set-upstream-to=upstream/main", "main");

        RepositoryInfo originPeer = await CloneRemoteAsync(originRoot);
        await CommitInRepositoryAsync(originPeer, "project.bep", "from origin\n", "origin update");
        CheckedOutBranchTip expected = await service.ExecuteExclusiveAsync(
            transaction => transaction.GetCheckedOutBranchTipAsync(CancellationToken.None),
            CancellationToken.None);

        // origin/main is not the history this branch follows, so fast-forwarding to it would be a guess.
        PullPreflightResult preflight = await service.ExecuteExclusiveAsync(
            transaction => transaction.PreflightPullAsync(expected, CancellationToken.None),
            CancellationToken.None);
        RemoteOpResult pull = await PullAsync(service);

        var refusal = new RemoteOpResult.Failed(
            Beutl.Language.Strings.VersionControl_PullUpstreamOnAnotherRemote);
        Assert.Multiple(() =>
        {
            Assert.That(preflight.Result, Is.EqualTo(refusal));
            Assert.That(pull, Is.EqualTo(refusal));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("initial\n"));
        });
    }

    [Test]
    public async Task Pull_refuses_another_remote_upstream_even_without_origin()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string upstreamRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await RunGitAsync("remote", "add", "upstream", upstreamRoot);
        await RunGitAsync("push", "upstream", "main");
        await RunGitAsync("branch", "--set-upstream-to=upstream/main", "main");
        RepositoryInfo upstreamPeer = await CloneRemoteAsync(upstreamRoot);
        await CommitInRepositoryAsync(upstreamPeer, "project.bep", "from upstream\n", "upstream update");
        CheckedOutBranchTip expected = await service.ExecuteExclusiveAsync(
            transaction => transaction.GetCheckedOutBranchTipAsync(CancellationToken.None),
            CancellationToken.None);

        // Without origin there is still no remote Beutl pulls from, so the branch's upstream on
        // another remote is refused rather than fetched.
        PullPreflightResult preflight = await service.ExecuteExclusiveAsync(
            transaction => transaction.PreflightPullAsync(expected, CancellationToken.None),
            CancellationToken.None);
        RemoteOpResult pull = await PullAsync(service);

        var refusal = new RemoteOpResult.Failed(
            Beutl.Language.Strings.VersionControl_PullUpstreamOnAnotherRemote);
        Assert.Multiple(() =>
        {
            Assert.That(preflight.Result, Is.EqualTo(refusal));
            Assert.That(pull, Is.EqualTo(refusal));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("initial\n"));
        });
    }

    [Test]
    public async Task Status_counts_against_the_tracked_origin_branch_when_the_names_differ()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string originRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(originRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());

        // Local `release` tracking `origin/main`: a ref synthesized from the local name asks about
        // the non-existent origin/release and reports an up-to-date branch.
        await RunGitAsync("switch", "-c", "release");
        await RunGitAsync("branch", "--set-upstream-to=origin/main", "release");
        RepositoryInfo originPeer = await CloneRemoteAsync(originRoot);
        await CommitInRepositoryAsync(originPeer, "project.bep", "from origin\n", "origin update");
        await RunGitAsync("fetch", "origin", "+refs/heads/main:refs/remotes/origin/main");

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.Ahead, Is.Zero);
            Assert.That(status.Behind, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Pull_fetches_a_differently_named_origin_upstream_for_preflight_and_fast_forward()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string originRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(originRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        await RunGitAsync("switch", "-c", "release");
        await RunGitAsync("branch", "--set-upstream-to=origin/main", "release");

        RepositoryInfo originPeer = await CloneRemoteAsync(originRoot);
        await CommitInRepositoryAsync(originPeer, "project.bep", "from origin\n", "origin update");
        string peerHead = (await CreateRunner().RunAsync(
                originPeer,
                ["rev-parse", "HEAD"],
                GitCommandOptions.Local,
                CancellationToken.None))
            .Stdout.Trim();
        CheckedOutBranchTip expected = await service.ExecuteExclusiveAsync(
            transaction => transaction.GetCheckedOutBranchTipAsync(CancellationToken.None),
            CancellationToken.None);

        PullPreflightResult preflight = await service.ExecuteExclusiveAsync(
            transaction => transaction.PreflightPullAsync(expected, CancellationToken.None),
            CancellationToken.None);
        RemoteOpResult pull = await PullAsync(service);
        string originHead = (await RunGitAsync("rev-parse", "refs/remotes/origin/main"))
            .Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(preflight.Result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(preflight.RequiresTransition, Is.True);
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(originHead, Is.EqualTo(peerHead));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("from origin\n"));
        });
    }

    [Test]
    public async Task Pull_preflight_reports_unrelated_repository_changes_before_the_project_closes()
    {
        string projectRoot = Path.Combine(Root, "project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "base\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "initial");
        string originRoot = await CreateBareRemoteAsync();
        using var service = CreateService(new RepositoryInfo(Root, projectRoot));
        await service.SetRemoteAsync(originRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(originRoot);
        await CommitInRepositoryAsync(peer, "project/project.bep", "from peer\n", "peer update");
        await File.WriteAllTextAsync(Path.Combine(Root, "notes.txt"), "scratch outside the project\n");
        CheckedOutBranchTip expected = await service.ExecuteExclusiveAsync(
            transaction => transaction.GetCheckedOutBranchTipAsync(CancellationToken.None),
            CancellationToken.None);

        PullPreflightResult preflight = await service.ExecuteExclusiveAsync(
            transaction => transaction.PreflightPullAsync(expected, CancellationToken.None),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            // The pull itself refuses this state only after the coordinator has closed the project,
            // so the preflight has to report it while the project is still open.
            Assert.That(preflight.Result, Is.TypeOf<RemoteOpResult.RepositoryDirty>());
            Assert.That(preflight.RequiresTransition, Is.False);
        });
    }

    [Test]
    public async Task Status_uses_origin_counts_when_branch_tracks_another_remote()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string originRoot = await CreateBareRemoteAsync();
        string upstreamRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(originRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        await RunGitAsync("remote", "add", "upstream", upstreamRoot);
        await RunGitAsync("push", "upstream", "main");
        await RunGitAsync("branch", "--set-upstream-to=upstream/main", "main");
        RepositoryInfo originPeer = await CloneRemoteAsync(originRoot);
        await CommitInRepositoryAsync(originPeer, "project.bep", "from origin\n", "origin update");
        await RunGitAsync("fetch", "origin", "+refs/heads/main:refs/remotes/origin/main");

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.Ahead, Is.Zero);
            Assert.That(status.Behind, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PreflightPull_fetches_the_current_origin_branch_outside_the_configured_refspec()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        await RunGitAsync("push", "origin", "main:other");
        await RunGitAsync(
            "config",
            "--replace-all",
            "remote.origin.fetch",
            "+refs/heads/other:refs/remotes/origin/other");
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        string peerHead = (await CreateRunner().RunAsync(
            peer,
            ["rev-parse", "HEAD"],
            GitCommandOptions.Local,
            CancellationToken.None)).Stdout.Trim();
        CheckedOutBranchTip expected = await service.ExecuteExclusiveAsync(
            transaction => transaction.GetCheckedOutBranchTipAsync(CancellationToken.None),
            CancellationToken.None);

        PullPreflightResult preflight = await service.ExecuteExclusiveAsync(
            transaction => transaction.PreflightPullAsync(expected, CancellationToken.None),
            CancellationToken.None);
        string originHead = (await RunGitAsync("rev-parse", "refs/remotes/origin/main"))
            .Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(preflight.Result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(preflight.RequiresTransition, Is.True);
            Assert.That(originHead, Is.EqualTo(peerHead));
        });
    }

    [Test]
    public async Task PullFastForward_refuses_to_overwrite_an_ignored_path_tracked_upstream()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "ignored.txt\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "initial\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await File.WriteAllTextAsync(Path.Combine(peer.ProjectRoot, "ignored.txt"), "from peer\n");
        GitCliRunner runner = CreateRunner();
        await runner.RunAsync(
            peer,
            ["add", "-f", "--", "ignored.txt"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await runner.RunAsync(
            peer,
            ["commit", "-m", "track ignored path"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await runner.RunAsync(
            peer,
            ["push"],
            GitCommandOptions.Network,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(Root, "ignored.txt"), "local secret\n");
        CheckedOutBranchTip expected = await service.ExecuteExclusiveAsync(
            transaction => transaction.GetCheckedOutBranchTipAsync(CancellationToken.None),
            CancellationToken.None);

        RemoteOpResult pull = await PullAsync(service);
        CheckedOutBranchTip actual = await service.ExecuteExclusiveAsync(
            transaction => transaction.GetCheckedOutBranchTipAsync(CancellationToken.None),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(File.ReadAllText(Path.Combine(Root, "ignored.txt")),
                Is.EqualTo("local secret\n"));
        });
    }

    [Test]
    public async Task PullFastForward_updates_a_tracked_file_that_matches_an_ignore_pattern()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "*.txt\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "tracked.txt"), "initial\n");
        await RunGitAsync("add", ".gitignore");
        await RunGitAsync("add", "-f", "tracked.txt");
        await RunGitAsync("commit", "-m", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "tracked.txt", "from peer\n", "peer update");

        RemoteOpResult pull = await PullAsync(service);

        Assert.Multiple(() =>
        {
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(File.ReadAllText(Path.Combine(Root, "tracked.txt")),
                Is.EqualTo("from peer\n"));
        });
    }

    [Test]
    public async Task PullFastForward_allows_an_absent_ignored_path_to_become_tracked()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "ignored.txt\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "initial\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await File.WriteAllTextAsync(Path.Combine(peer.ProjectRoot, "ignored.txt"), "from peer\n");
        GitCliRunner runner = CreateRunner();
        await runner.RunAsync(
            peer,
            ["add", "-f", "--", "ignored.txt"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await runner.RunAsync(
            peer,
            ["commit", "-m", "track absent ignored path"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await runner.RunAsync(
            peer,
            ["push"],
            GitCommandOptions.Network,
            CancellationToken.None);

        RemoteOpResult pull = await PullAsync(service);

        Assert.Multiple(() =>
        {
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(File.ReadAllText(Path.Combine(Root, "ignored.txt")),
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

        RemoteOpResult pull = await PullAsync(service);
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

    [Test]
    public async Task Pull_stashes_local_project_changes_and_restores_them_after_the_fast_forward()
    {
        await CommitFileAsync("scene.belm", "one\ntwo\nthree\nfour\nfive\nsix\n", "scene");
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        await CommitInRepositoryAsync(
            peer,
            "scene.belm",
            "ONE\ntwo\nthree\nfour\nfive\nsix\n",
            "peer scene update");
        // The pull and the local edit touch the same file, which git merge --ff-only refuses
        // while the edit is in the worktree; set aside in a stash it merges back cleanly.
        await File.WriteAllTextAsync(
            Path.Combine(Root, "scene.belm"),
            "one\ntwo\nthree\nfour\nfive\nSIX\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "added.belm"), "new local file\n");
        Directory.CreateDirectory(Path.Combine(Root, ".beutl"));
        await File.WriteAllTextAsync(Path.Combine(Root, ".beutl", "view-state.json"), "{}\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "render.tmp"), "scratch\n");

        RemoteOpResult pull = await PullAsync(service);

        string head = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string remoteHead = await ReadRemoteHeadAsync(remoteRoot);
        GitCommandResult stashes = await RunGitAsync("stash", "list");
        GitCommandResult status = await RunGitAsync("status", "--porcelain", "--untracked-files=all");
        Assert.Multiple(() =>
        {
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Success>());
            // The branch is fast-forwarded to exactly the upstream commit: the local changes come
            // back as local changes, not as a commit on top.
            Assert.That(head, Is.EqualTo(remoteHead));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("from peer\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "scene.belm")),
                Is.EqualTo("ONE\ntwo\nthree\nfour\nfive\nSIX\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "added.belm")),
                Is.EqualTo("new local file\n"));
            Assert.That(status.Stdout, Does.Contain(" M scene.belm"));
            Assert.That(status.Stdout, Does.Contain("?? added.belm"));
            Assert.That(stashes.Stdout, Is.Empty);
            // Beutl's own state and scratch files stay out of the stash, as they stay out of
            // every snapshot.
            Assert.That(
                File.ReadAllText(Path.Combine(Root, ".beutl", "view-state.json")),
                Is.EqualTo("{}\n"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "render.tmp")), Is.EqualTo("scratch\n"));
        });
    }

    [Test]
    public async Task Failed_fast_forward_restores_the_stashed_project_changes()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "remote.belm", "remote\n", "remote update");
        await CommitFileAsync("local.belm", "local\n", "local update");
        await RunGitAsync("fetch", "origin");
        string upstreamCommit = (await RunGitAsync("rev-parse", "refs/remotes/origin/main")).Stdout.Trim();
        string localHead = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "unsaved local edit\n");

        // The branches diverged after the preflight, so git merge --ff-only refuses.
        RemoteOpResult pull = await service.ExecuteExclusiveAsync(
            transaction => transaction.PullFastForwardAsync(upstreamCommit, CancellationToken.None),
            CancellationToken.None);

        string head = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        GitCommandResult stashes = await RunGitAsync("stash", "list");
        Assert.Multiple(() =>
        {
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Diverged>());
            Assert.That(head, Is.EqualTo(localHead));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("unsaved local edit\n"));
            Assert.That(stashes.Stdout, Is.Empty);
        });
    }

    [Test]
    public async Task Conflicting_local_changes_stay_in_the_stash_and_are_reported()
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
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "local edit\n");

        RemoteOpResult pull = await PullAsync(service);

        string head = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string remoteHead = await ReadRemoteHeadAsync(remoteRoot);
        GitCommandResult stashes = await RunGitAsync("stash", "list");
        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);
        string keptInStash = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Beutl.Language.Strings.VersionControl_PullChangesKeptInStashFormat,
            "beutl: local changes before pull");
        Assert.Multiple(() =>
        {
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(((RemoteOpResult.Failed)pull).Stderr, Does.StartWith(keptInStash));
            // The pull itself went through; only reapplying the local changes conflicted.
            Assert.That(head, Is.EqualTo(remoteHead));
            Assert.That(stashes.Stdout, Does.Contain("beutl: local changes before pull"));
            Assert.That(status.HasConflicts, Is.True);
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Does.Contain("<<<<<<<").And.Contain("local edit").And.Contain("from peer"));
        });
    }

    [Test]
    public async Task Pull_brings_back_staged_project_changes_as_staged()
    {
        await CommitFileAsync("scene.belm", "initial scene\n", "scene");
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        await File.WriteAllTextAsync(Path.Combine(Root, "scene.belm"), "staged scene\n");
        await RunGitAsync("add", "--", "scene.belm");
        await File.WriteAllTextAsync(Path.Combine(Root, "scene.belm"), "working scene\n");

        RemoteOpResult pull = await PullAsync(service);

        GitCommandResult staged = await RunGitAsync("show", ":scene.belm");
        GitCommandResult stashes = await RunGitAsync("stash", "list");
        Assert.Multiple(() =>
        {
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("from peer\n"));
            Assert.That(staged.Stdout, Is.EqualTo("staged scene\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "scene.belm")),
                Is.EqualTo("working scene\n"));
            Assert.That(stashes.Stdout, Is.Empty);
        });
    }

    // git stash pop --index refuses when the staged change no longer applies to the pulled index;
    // the change still merges into the worktree, so it comes back unstaged.
    [Test]
    public async Task Staged_changes_the_pull_also_touches_come_back_unstaged()
    {
        await CommitFileAsync("scene.belm", "one\ntwo\nthree\nfour\nfive\nsix\n", "scene");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(
            peer,
            "scene.belm",
            "one\ntwo\nTHREE\nfour\nfive\nsix\n",
            "peer scene update");
        await File.WriteAllTextAsync(
            Path.Combine(Root, "scene.belm"),
            "ONE\ntwo\nthree\nfour\nfive\nsix\n");
        await RunGitAsync("add", "--", "scene.belm");

        RemoteOpResult pull = await PullAsync(service);

        string head = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string remoteHead = await ReadRemoteHeadAsync(remoteRoot);
        GitCommandResult stagedNames = await RunGitAsync("diff", "--cached", "--name-only");
        GitCommandResult stashes = await RunGitAsync("stash", "list");
        Assert.Multiple(() =>
        {
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(head, Is.EqualTo(remoteHead));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "scene.belm")),
                Is.EqualTo("ONE\ntwo\nTHREE\nfour\nfive\nsix\n"));
            Assert.That(stagedNames.Stdout, Is.Empty);
            Assert.That(stashes.Stdout, Is.Empty);
        });
    }

    [Test]
    public async Task Pull_reapplies_its_own_stash_entry_when_a_merge_hook_stashes_another()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "remote.belm", "remote\n", "remote update");
        await WritePostMergeHookAsync(
            "echo hook > hook-note.txt\n"
            + "git stash push --include-untracked -m \"hook entry\" -- hook-note.txt\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "local edit\n");

        RemoteOpResult pull = await PullAsync(service);

        GitCommandResult stashes = await RunGitAsync("stash", "list");
        Assert.Multiple(() =>
        {
            Assert.That(pull, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(File.ReadAllText(Path.Combine(Root, "remote.belm")), Is.EqualTo("remote\n"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("local edit\n"));
            Assert.That(stashes.Stdout, Does.Contain("hook entry"));
            Assert.That(stashes.Stdout, Does.Not.Contain("beutl: local changes before pull"));
        });
    }

    private async Task WritePostMergeHookAsync(string body)
    {
        string hookRecord = (await RunGitAsync("rev-parse", "--git-path", "hooks/post-merge"))
            .Stdout.TrimEnd('\r', '\n');
        string hookPath = Path.GetFullPath(
            Path.IsPathFullyQualified(hookRecord) ? hookRecord : Path.Combine(Root, hookRecord));
        Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
        await File.WriteAllTextAsync(hookPath, "#!/bin/sh\n" + body, new System.Text.UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                hookPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
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

    private GitCliVersionControlService CreateService(
        RepositoryInfo? repository = null,
        IGitCliRunner? runner = null)
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository ?? Repository,
            watcher: null,
            _ => runner ?? CreateRunner());
    }

    // Pulls the way the coordinator does: the preflight fetches and checks for a fast-forward, and
    // the pull then merges the commit the preflight fetched.
    private static Task<RemoteOpResult> PullAsync(GitCliVersionControlService service)
    {
        return service.ExecuteExclusiveAsync(
            async transaction =>
            {
                CheckedOutBranchTip tip = await transaction.GetCheckedOutBranchTipAsync(
                    CancellationToken.None);
                PullPreflightResult preflight = await transaction.PreflightPullAsync(
                    tip,
                    CancellationToken.None);
                return preflight.UpstreamCommit is { } upstreamCommit
                    ? await transaction.PullFastForwardAsync(upstreamCommit, CancellationToken.None)
                    : preflight.Result;
            },
            CancellationToken.None);
    }

    // Leaves the branch only on origin, as a teammate's push that this clone has fetched would.
    private async Task PublishOriginOnlyBranchAsync(string branchName, string projectContents)
    {
        await RunGitAsync("switch", "-c", branchName);
        await CommitFileAsync("project.bep", projectContents, branchName);
        await RunGitAsync("push", "origin", branchName);
        await RunGitAsync("switch", "main");
        await RunGitAsync("branch", "-D", branchName);
    }

    private async Task<string> CreateBareRemoteAsync()
    {
        string remoteRoot = CreateTemporaryDirectory();
        var remote = new RepositoryInfo(remoteRoot, remoteRoot);
        await CreateRunner().RunAsync(
            remote,
            ["init", "--bare", "-b", "main"],
            GitCommandOptions.Local,
            CancellationToken.None);
        return remoteRoot;
    }

    private async Task<RepositoryInfo> CloneRemoteAsync(string remoteRoot)
    {
        string peerRoot = CreateTemporaryDirectory();
        await CreateRunner().RunAsync(
            Repository,
            ["clone", "--branch", "main", remoteRoot, peerRoot],
            GitCommandOptions.Local,
            CancellationToken.None);
        var peer = new RepositoryInfo(peerRoot, peerRoot);
        GitCliRunner runner = CreateRunner();
        await runner.RunAsync(
            peer,
            ["config", "user.name", "Peer Test"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await runner.RunAsync(
            peer,
            ["config", "user.email", "peer@example.invalid"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await runner.RunAsync(
            peer,
            ["config", "commit.gpgsign", "false"],
            GitCommandOptions.Local,
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
            GitCommandOptions.Local,
            CancellationToken.None);
        await runner.RunAsync(
            repository,
            ["commit", "-m", message],
            GitCommandOptions.Local,
            CancellationToken.None);
        await runner.RunAsync(
            repository,
            ["push"],
            GitCommandOptions.Local,
            CancellationToken.None);
    }

    private async Task<string> ReadRemoteHeadAsync(string remoteRoot)
    {
        return await ReadRemoteRefAsync(remoteRoot, "refs/heads/main");
    }

    private async Task<string> ReadRemoteRefAsync(string remoteRoot, string refName)
    {
        var remote = new RepositoryInfo(remoteRoot, remoteRoot);
        GitCommandResult result = await CreateRunner().RunAsync(
            remote,
            ["rev-parse", refName],
            GitCommandOptions.Local,
            CancellationToken.None);
        return result.Stdout.Trim();
    }

    private async Task<string> ListRemoteRefAsync(string remoteRoot, string refName)
    {
        var remote = new RepositoryInfo(remoteRoot, remoteRoot);
        GitCommandResult result = await CreateRunner().RunAsync(
            remote,
            ["for-each-ref", "--format=%(refname)", refName],
            GitCommandOptions.Local,
            CancellationToken.None);
        return result.Stdout.Trim();
    }

    // Commands can carry `-c` overrides (LFS path filters, the upload agent) before the
    // subcommand, so tests match past that prefix instead of pinning its exact shape.
    private static string? GetGitSubcommand(IReadOnlyList<string> arguments)
    {
        int index = SkipConfigOverrides(arguments);
        return index < arguments.Count ? arguments[index] : null;
    }

    private static int SkipConfigOverrides(IReadOnlyList<string> arguments)
    {
        int index = 0;
        while (index + 1 < arguments.Count && arguments[index] == "-c")
        {
            index += 2;
        }

        return index;
    }

    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Messages { get; } = [];

        public void Report(string value)
        {
            Messages.Add(value);
        }
    }

    private sealed class InterceptingRunner(
        IGitCliRunner inner,
        Func<RepositoryInfo, IReadOnlyList<string>, GitCommandOptions, bool> predicate,
        Func<RepositoryInfo, IReadOnlyList<string>, GitCommandOptions, Task>? before,
        Func<RepositoryInfo, IReadOnlyList<string>, GitCommandOptions, Task>? after,
        int interceptOnMatch = 1)
        : IGitCliRunner
    {
        private int _interceptionCount;
        private int _matchingCount;

        public bool HasActiveProcess => inner.HasActiveProcess;

        public int InterceptionCount => Volatile.Read(ref _interceptionCount);

        public List<IReadOnlyList<string>> Commands { get; } = [];

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            lock (Commands)
            {
                Commands.Add(arguments.ToArray());
            }

            bool intercept = predicate(repository, arguments, options)
                             && Interlocked.Increment(ref _matchingCount) == interceptOnMatch;
            if (intercept)
            {
                Interlocked.Increment(ref _interceptionCount);
            }
            if (intercept && before is not null)
            {
                await before(repository, arguments, options);
            }

            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (intercept && after is not null)
            {
                await after(repository, arguments, options);
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }
}
