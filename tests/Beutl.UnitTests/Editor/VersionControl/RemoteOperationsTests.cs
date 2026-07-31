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
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("from peer\n"));
        });
    }

    [Test]
    public async Task PullFastForward_does_not_invoke_the_repository_post_checkout_hook()
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
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        string hooksDirectory = Path.Combine(Root, ".git", "beutl-test-hooks");
        Directory.CreateDirectory(hooksDirectory);
        string hookPath = Path.Combine(hooksDirectory, "post-checkout");
        string markerPath = Path.Combine(Root, ".git", "post-checkout-invoked");
        await File.WriteAllTextAsync(
            hookPath,
            "#!/bin/sh\n"
            + "touch \"$(git rev-parse --git-common-dir)/post-checkout-invoked\"\n"
            + "exit 93\n");
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

        await RunGitAsync("config", "core.hooksPath", hooksDirectory);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.Applied));
            Assert.That(File.Exists(markerPath), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("from peer\n"));
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
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);
        CheckedOutBranchTip actual = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(pull.Tip, Is.EqualTo(expected));
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
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.Applied));
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
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.Applied));
            Assert.That(File.ReadAllText(Path.Combine(Root, "ignored.txt")),
                Is.EqualTo("from peer\n"));
        });
    }

    [Test]
    public async Task PullFastForward_refuses_a_different_checked_out_branch()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        using var service = CreateService();
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        await RunGitAsync("switch", "-c", "external");
        CheckedOutBranchTip external = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        Assert.That(
            async () => await service.PullFastForwardAsync(
                expected,
                checkpoint: null,
                CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(
                service.GetCheckedOutBranchTipAsync(CancellationToken.None).GetAwaiter().GetResult(),
                Is.EqualTo(external));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("initial\n"));
        });
    }

    [Test]
    public async Task PullFastForward_treats_a_post_CAS_runner_exception_as_durable_success()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        var interceptingRunner = new InterceptingRunner(
            CreateRunner(),
            static (_, arguments, _) =>
                arguments is ["update-ref", "-m", "pull: fast-forward", ..],
            before: null,
            static (_, _, _) => throw new IOException("simulated lost CAS response"));
        using var service = CreateService(runner: interceptingRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);
        string mainTip = (await RunGitAsync("rev-parse", "refs/heads/main")).Stdout.Trim();
        string currentBranch = (await RunGitAsync("branch", "--show-current")).Stdout.Trim();
        string worktrees = (await RunGitAsync("worktree", "list", "--porcelain")).Stdout;

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.Applied));
            Assert.That(mainTip, Is.EqualTo(pull.Tip.Commit));
            Assert.That(mainTip, Is.Not.EqualTo(expected.Commit));
            Assert.That(currentBranch, Is.EqualTo("main"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("from peer\n"));
            Assert.That(worktrees, Does.Not.Contain("beutl-git-ref-update-"));
            Assert.That(interceptingRunner.InterceptionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PullFastForward_reports_ownership_loss_without_repository_dirty_before_final_CAS()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        GitCliRunner commandRunner = CreateRunner();
        CheckedOutBranchTip? expected = null;
        RepositoryInfo? externalProxy = null;
        string? externalCommit = null;
        var interceptingRunner = new InterceptingRunner(
            CreateRunner(),
            static (_, arguments, _) =>
                arguments is ["update-ref", "-m", "pull: fast-forward", ..],
            async (_, _, _) =>
            {
                await commandRunner.RunAsync(
                    externalProxy!,
                    [
                        "update-ref",
                        expected!.RefName,
                        externalCommit!,
                        expected!.Commit,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None);
            },
            after: null);
        using var service = CreateService(runner: interceptingRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        string originalTree = (await RunGitAsync(
            "rev-parse",
            $"{expected.Commit}^{{tree}}")).Stdout.Trim();
        externalCommit = (await RunGitAsync(
            "commit-tree",
            originalTree,
            "-p",
            expected.Commit,
            "-m",
            "external concurrent update")).Stdout.Trim();
        string externalProxyRoot = CreateTemporaryDirectory();
        await RunGitAsync(
            "worktree",
            "add",
            "--detach",
            "--no-checkout",
            externalProxyRoot,
            expected.Commit);
        externalProxy = new RepositoryInfo(externalProxyRoot, externalProxyRoot);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);
        string mainTip = (await RunGitAsync("rev-parse", "refs/heads/main")).Stdout.Trim();
        string currentBranch = (await RunGitAsync("branch", "--show-current")).Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.OwnershipLost));
            Assert.That(pull.Tip.Commit, Is.EqualTo(externalCommit));
            Assert.That(mainTip, Is.EqualTo(externalCommit));
            Assert.That(currentBranch, Is.EqualTo("main"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("from peer\n"));
            Assert.That(interceptingRunner.InterceptionCount, Is.EqualTo(1));
        });
    }

    [TestCase("tracked")]
    [TestCase("untracked")]
    [TestCase("ignored")]
    public async Task PullFastForward_preserves_a_late_worktree_collision(string collisionKind)
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        string collisionPath = collisionKind == "tracked" ? "project.bep" : "incoming.txt";
        string localContents = $"late {collisionKind} contents\n";
        var interceptingRunner = new InterceptingRunner(
            CreateRunner(),
            static (_, arguments, _) =>
                arguments is
                [
                    "-c",
                    "core.hooksPath=/dev/null",
                    "checkout",
                    "--detach",
                    "--no-overwrite-ignore",
                    ..
                ],
            async (_, _, _) =>
            {
                if (collisionKind == "ignored")
                {
                    await File.AppendAllTextAsync(
                        Path.Combine(Root, ".git", "info", "exclude"),
                        "incoming.txt\n");
                }

                await File.WriteAllTextAsync(
                    Path.Combine(Root, collisionPath),
                    localContents);
            },
            after: null);
        using var service = CreateService(runner: interceptingRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(
            peer,
            collisionPath,
            $"remote {collisionKind} contents\n",
            "peer update");
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);
        string actualTip = (await RunGitAsync("rev-parse", "refs/heads/main")).Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.Not.TypeOf<RemoteOpResult.Success>());
            Assert.That(
                pull.TransitionState,
                Is.EqualTo(collisionKind == "ignored"
                    ? PullTransitionState.Unchanged
                    : PullTransitionState.OwnershipLost));
            Assert.That(pull.Tip, Is.EqualTo(expected));
            Assert.That(actualTip, Is.EqualTo(expected.Commit));
            Assert.That(File.ReadAllText(Path.Combine(Root, collisionPath)),
                Is.EqualTo(localContents));
            Assert.That(interceptingRunner.InterceptionCount, Is.EqualTo(1));
        });
    }

    [TestCase("tracked")]
    [TestCase("untracked")]
    [TestCase("ignored")]
    public async Task Checkpointed_pull_restores_the_index_after_a_late_worktree_collision(
        string collisionKind)
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        string collisionPath = collisionKind == "tracked" ? "project.bep" : "incoming.txt";
        string localContents = $"late {collisionKind} contents\n";
        var interceptingRunner = new InterceptingRunner(
            CreateRunner(),
            static (_, arguments, _) =>
                arguments is
                [
                    "-c",
                    "core.hooksPath=/dev/null",
                    "checkout",
                    "--detach",
                    "--no-overwrite-ignore",
                    ..
                ],
            async (_, _, _) =>
            {
                if (collisionKind == "ignored")
                {
                    await File.AppendAllTextAsync(
                        Path.Combine(Root, ".git", "info", "exclude"),
                        "incoming.txt\n");
                }

                await File.WriteAllTextAsync(
                    Path.Combine(Root, collisionPath),
                    localContents);
            },
            after: null);
        using var service = CreateService(runner: interceptingRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(
            peer,
            collisionPath,
            $"remote {collisionKind} contents\n",
            "peer update");
        await File.WriteAllTextAsync(Path.Combine(Root, "local.belm"), "local edit\n");
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: late collision checkpoint",
            CancellationToken.None);
        string cachedBefore = (await RunGitAsync("diff", "--cached", "--binary")).Stdout;

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint,
            CancellationToken.None);
        string cachedAfter = (await RunGitAsync("diff", "--cached", "--binary")).Stdout;
        string actualTip = (await RunGitAsync("rev-parse", expected.RefName)).Stdout.Trim();
        string checkpointTip = (await RunGitAsync("rev-parse", checkpoint.RefName)).Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.Not.TypeOf<RemoteOpResult.Success>());
            Assert.That(
                pull.TransitionState,
                Is.EqualTo(collisionKind == "ignored"
                    ? PullTransitionState.Unchanged
                    : PullTransitionState.OwnershipLost));
            Assert.That(pull.Tip, Is.EqualTo(expected));
            Assert.That(actualTip, Is.EqualTo(expected.Commit));
            Assert.That(checkpointTip, Is.EqualTo(checkpoint.Commit));
            Assert.That(File.ReadAllText(Path.Combine(Root, collisionPath)),
                Is.EqualTo(localContents));
            Assert.That(File.ReadAllText(Path.Combine(Root, "local.belm")),
                Is.EqualTo("local edit\n"));
            Assert.That(cachedAfter, Is.EqualTo(cachedBefore));
            Assert.That(interceptingRunner.InterceptionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PullFastForward_reports_and_preserves_a_stale_HEAD_lock_before_mutation()
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
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        string headLockPath = Path.Combine(Root, ".git", "HEAD.lock");
        await File.WriteAllTextAsync(headLockPath, "stale");
        File.SetLastWriteTimeUtc(headLockPath, DateTime.UtcNow - TimeSpan.FromMinutes(20));

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);
        string worktrees = (await RunGitAsync("worktree", "list", "--porcelain")).Stdout;

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.Unchanged));
            Assert.That(pull.Tip, Is.EqualTo(expected));
            Assert.That(service.RecoverableLock?.LockPath, Is.EqualTo(headLockPath));
            Assert.That(File.Exists(headLockPath), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("initial\n"));
            Assert.That(worktrees, Does.Not.Contain("beutl-git-ref-update-"));
        });

        Assert.That(
            await service.RemoveRecoverableLockAsync(CancellationToken.None),
            Is.True);
        Assert.That(File.Exists(headLockPath), Is.False);
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
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);
        RemoteOpResult push = await service.PushAsync(progress: null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Diverged>());
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
    public async Task Dirty_project_checkpoint_is_reapplied_after_fast_forward_pull()
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
        await File.WriteAllTextAsync(Path.Combine(Root, "local.belm"), "local edit\n");
        CheckedOutBranchTip originalHead = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);

        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: safety checkpoint before pull",
            CancellationToken.None);
        CheckedOutBranchTip headAfterCheckpoint = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
        string resolvedCheckpoint = (await RunGitAsync(
            "rev-parse",
            checkpoint.RefName)).Stdout.Trim();
        FastForwardPullResult pull = await service.PullFastForwardAsync(
            originalHead,
            checkpoint,
            CancellationToken.None);
        CheckedOutBranchTip pulledHead = pull.Tip;
        bool checkpointDeleted = await service.DeleteProjectCheckpointAsync(
            checkpoint,
            CancellationToken.None);
        string safetyParent = (await RunGitAsync("rev-parse", "HEAD^1")).Stdout.Trim();
        string remoteHead = await ReadRemoteHeadAsync(remoteRoot);

        Assert.Multiple(() =>
        {
            Assert.That(checkpoint.BaseTip, Is.EqualTo(originalHead));
            Assert.That(headAfterCheckpoint, Is.EqualTo(originalHead));
            Assert.That(resolvedCheckpoint, Is.EqualTo(checkpoint.Commit));
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(pulledHead.Commit, Is.Not.EqualTo(originalHead.Commit));
            Assert.That(safetyParent, Is.EqualTo(remoteHead));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("from peer\n"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "local.belm")),
                Is.EqualTo("local edit\n"));
            Assert.That(checkpointDeleted, Is.True);
        });
    }

    [Test]
    public async Task Checkpointed_pull_reports_ownership_loss_without_repository_dirty_during_preparation()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        GitCliRunner commandRunner = CreateRunner();
        CheckedOutBranchTip? expected = null;
        string? externalCommit = null;
        var interceptingRunner = new InterceptingRunner(
            CreateRunner(),
            static (_, arguments, _) =>
                arguments is
                [
                    "commit-tree",
                    _,
                    "-p",
                    _,
                    "-m",
                    "beutl: safety snapshot before pull",
                    ..
                ],
            before: null,
            async (repository, _, _) => await commandRunner.RunAsync(
                repository,
                [
                    "update-ref",
                    expected!.RefName,
                    externalCommit!,
                    expected.Commit,
                ],
                GitCommandOptions.Local,
                CancellationToken.None));
        using var service = CreateService(runner: interceptingRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        expected = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
        string originalTree = (await RunGitAsync(
            "rev-parse",
            $"{expected.Commit}^{{tree}}")).Stdout.Trim();
        externalCommit = (await RunGitAsync(
            "commit-tree",
            originalTree,
            "-p",
            expected.Commit,
            "-m",
            "external update during checkpoint preparation")).Stdout.Trim();
        await File.WriteAllTextAsync(Path.Combine(Root, "local.belm"), "local edit\n");
        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: safety checkpoint before pull",
            CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint,
            CancellationToken.None);
        string actualRef = (await RunGitAsync("rev-parse", expected.RefName)).Stdout.Trim();
        string checkpointRef = (await RunGitAsync("rev-parse", checkpoint.RefName)).Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.OwnershipLost));
            Assert.That(pull.Tip.Commit, Is.EqualTo(externalCommit));
            Assert.That(actualRef, Is.EqualTo(externalCommit));
            Assert.That(checkpointRef, Is.EqualTo(checkpoint.Commit));
            Assert.That(File.ReadAllText(Path.Combine(Root, "local.belm")),
                Is.EqualTo("local edit\n"));
            Assert.That(interceptingRunner.InterceptionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Checkpointed_pull_restores_the_real_index_when_prepare_observation_fails()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        string? checkpointCommit = null;
        var interceptingRunner = new InterceptingRunner(
            CreateRunner(),
            (_, arguments, _) =>
                checkpointCommit is not null
                && arguments is ["read-tree", "--reset", var commit]
                && string.Equals(commit, checkpointCommit, StringComparison.OrdinalIgnoreCase),
            before: null,
            static (_, _, _) => throw new IOException("simulated prepare observation failure"));
        using var service = CreateService(runner: interceptingRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        await File.WriteAllTextAsync(Path.Combine(Root, "local.belm"), "local edit\n");
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: prepare failure checkpoint",
            CancellationToken.None);
        checkpointCommit = checkpoint.Commit;

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint,
            CancellationToken.None);
        string cachedDiff = (await RunGitAsync(
            "diff",
            "--cached",
            "--name-only",
            expected.Commit,
            "--",
            ".")).Stdout;
        string worktrees = (await RunGitAsync("worktree", "list", "--porcelain")).Stdout;

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.Unchanged));
            Assert.That(pull.Tip, Is.EqualTo(expected));
            Assert.That(cachedDiff, Is.Empty);
            Assert.That(File.ReadAllText(Path.Combine(Root, "local.belm")),
                Is.EqualTo("local edit\n"));
            Assert.That(
                (RunGitAsync("rev-parse", checkpoint.RefName).GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(checkpoint.Commit));
            Assert.That(worktrees, Does.Not.Contain("beutl-git-ref-update-"));
            Assert.That(interceptingRunner.InterceptionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Checkpointed_pull_does_not_report_unchanged_after_a_secondary_prepare_failure()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        var faultRunner = new PrepareObserverFaultRunner(CreateRunner());
        using var service = CreateService(runner: faultRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        await File.WriteAllTextAsync(Path.Combine(Root, "local.belm"), "local edit\n");
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: secondary prepare failure checkpoint",
            CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint,
            CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.RecoveryFailed));
            Assert.That(
                (RunGitAsync("rev-parse", expected.RefName).GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(expected.Commit));
            Assert.That(File.ReadAllText(Path.Combine(Root, "local.belm")),
                Is.EqualTo("local edit\n"));
            Assert.That(
                (RunGitAsync("rev-parse", checkpoint.RefName).GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(checkpoint.Commit));
            Assert.That(faultRunner.PrepareFaulted, Is.True);
            Assert.That(faultRunner.ObserverFaulted, Is.True);
        });
    }

    [Test]
    public async Task Restore_checkpoint_reverses_tree_and_index_when_final_reset_observation_fails()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        CheckedOutBranchTip? baseTip = null;
        var interceptingRunner = new InterceptingRunner(
            CreateRunner(),
            (_, arguments, _) =>
                baseTip is not null
                && arguments is ["read-tree", "--reset", var commit]
                && string.Equals(commit, baseTip.Commit, StringComparison.OrdinalIgnoreCase),
            before: null,
            static (_, _, _) => throw new IOException("simulated final index observation failure"));
        using var service = CreateService(runner: interceptingRunner);
        baseTip = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "checkpointed\n");
        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: final reset failure checkpoint",
            CancellationToken.None);
        await RunGitAsync("restore", "--source=HEAD", "--worktree", "--", "project.bep");

        Assert.That(
            async () => await service.RestoreProjectCheckpointAsync(
                checkpoint,
                CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());

        string cachedDiff = (await RunGitAsync(
            "diff",
            "--cached",
            "--name-only",
            baseTip.Commit,
            "--",
            ".")).Stdout;
        string worktrees = (await RunGitAsync("worktree", "list", "--porcelain")).Stdout;
        Assert.Multiple(() =>
        {
            Assert.That(
                service.GetCheckedOutBranchTipAsync(CancellationToken.None).GetAwaiter().GetResult(),
                Is.EqualTo(baseTip));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("base\n"));
            Assert.That(cachedDiff, Is.Empty);
            Assert.That(
                (RunGitAsync("rev-parse", checkpoint.RefName).GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(checkpoint.Commit));
            Assert.That(worktrees, Does.Not.Contain("beutl-git-ref-update-"));
            Assert.That(interceptingRunner.InterceptionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Pull_recovers_when_the_worktree_advances_before_the_temporary_HEAD()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        var faultRunner = new WorktreeBeforeTemporaryHeadFaultRunner(CreateRunner());
        using var service = CreateService(runner: faultRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);
        string cachedDiff = (await RunGitAsync(
            "diff",
            "--cached",
            "--name-only",
            expected.Commit,
            "--",
            ".")).Stdout;
        string worktrees = (await RunGitAsync("worktree", "list", "--porcelain")).Stdout;

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.Unchanged));
            Assert.That(pull.Tip, Is.EqualTo(expected));
            Assert.That(
                service.GetCheckedOutBranchTipAsync(CancellationToken.None).GetAwaiter().GetResult(),
                Is.EqualTo(expected));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("initial\n"));
            Assert.That(cachedDiff, Is.Empty);
            Assert.That(faultRunner.CheckoutCount, Is.EqualTo(2));
            Assert.That(faultRunner.AlignmentCount, Is.EqualTo(1));
            Assert.That(worktrees, Does.Not.Contain("beutl-git-ref-update-"));
        });
    }

    [Test]
    public async Task Pull_reports_recovery_failed_when_reverse_checkout_fails()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        var faultRunner = new CheckoutRecoveryFaultRunner(
            CreateRunner(),
            failReverse: true,
            afterReverse: null);
        using var service = CreateService(runner: faultRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);
        string actualRef = (await RunGitAsync("rev-parse", expected.RefName)).Stdout.Trim();
        string worktrees = (await RunGitAsync("worktree", "list", "--porcelain")).Stdout;

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.RecoveryFailed));
            Assert.That(actualRef, Is.EqualTo(expected.Commit));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("from peer\n"));
            Assert.That(faultRunner.CheckoutCount, Is.EqualTo(2));
            Assert.That(worktrees, Does.Not.Contain("beutl-git-ref-update-"));
        });
    }

    [Test]
    public async Task Pull_reports_recovery_failed_when_post_checkout_ref_observation_fails()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        var faultRunner = new CheckoutObserverFaultRunner(CreateRunner());
        using var service = CreateService(runner: faultRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        CheckedOutBranchTip expected = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.RecoveryFailed));
            Assert.That(
                (RunGitAsync("rev-parse", expected.RefName).GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(expected.Commit));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("from peer\n"));
            Assert.That(faultRunner.CheckoutFaulted, Is.True);
            Assert.That(faultRunner.ObserverFaulted, Is.True);
        });
    }

    [Test]
    public async Task Pull_reports_ownership_lost_without_repository_dirty_during_recovery()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        CheckedOutBranchTip? expected = null;
        RepositoryInfo? externalProxy = null;
        string? externalCommit = null;
        GitCliRunner commandRunner = CreateRunner();
        var faultRunner = new CheckoutRecoveryFaultRunner(
            CreateRunner(),
            failReverse: false,
            async () => await commandRunner.RunAsync(
                externalProxy!,
                ["update-ref", expected!.RefName, externalCommit!, expected!.Commit],
                GitCommandOptions.Local,
                CancellationToken.None));
        using var service = CreateService(runner: faultRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "from peer\n", "peer update");
        expected = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
        string originalTree = (await RunGitAsync(
            "rev-parse",
            $"{expected.Commit}^{{tree}}")).Stdout.Trim();
        externalCommit = (await RunGitAsync(
            "commit-tree",
            originalTree,
            "-p",
            expected.Commit,
            "-m",
            "external update during recovery")).Stdout.Trim();
        string externalProxyRoot = CreateTemporaryDirectory();
        await RunGitAsync(
            "worktree",
            "add",
            "--detach",
            "--no-checkout",
            externalProxyRoot,
            expected.Commit);
        externalProxy = new RepositoryInfo(externalProxyRoot, externalProxyRoot);

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expected,
            checkpoint: null,
            CancellationToken.None);
        string actualRef = (await RunGitAsync("rev-parse", expected.RefName)).Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.OwnershipLost));
            Assert.That(pull.Tip.Commit, Is.EqualTo(externalCommit));
            Assert.That(actualRef, Is.EqualTo(externalCommit));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("initial\n"));
            Assert.That(faultRunner.CheckoutCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Conflicting_checkpoint_fails_before_CAS_while_ref_remains_reachable()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        string remoteRoot = await CreateBareRemoteAsync();
        string lateEdit = Path.Combine(Root, "late-edit.txt");
        var interceptingRunner = new InterceptingRunner(
            CreateRunner(),
            static (_, arguments, options) =>
                arguments is ["read-tree", "-m", ..]
                && options.EnvironmentOverrides?.ContainsKey("GIT_INDEX_FILE") == true,
            async (_, _, _) => await File.WriteAllTextAsync(lateEdit, "keep late edit\n"),
            after: null);
        using var service = CreateService(runner: interceptingRunner);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        RepositoryInfo peer = await CloneRemoteAsync(remoteRoot);
        await CommitInRepositoryAsync(peer, "project.bep", "remote\n", "peer update");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "local\n");

        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: safety checkpoint before pull",
            CancellationToken.None);
        Assert.That(
            async () => await service.PullFastForwardAsync(
                checkpoint.BaseTip,
                checkpoint,
                CancellationToken.None),
            Throws.TypeOf<GitOperationException>());

        string retainedCheckpoint = (await RunGitAsync(
            "rev-parse",
            checkpoint.RefName)).Stdout.Trim();
        string statusAfterConflict = (await RunGitAsync(
            "status",
            "--porcelain=v1",
            "--untracked-files=all")).Stdout;
        bool cherryPickHeadExists = File.Exists(Path.Combine(Root, ".git", "CHERRY_PICK_HEAD"));

        Assert.Multiple(() =>
        {
            Assert.That(retainedCheckpoint, Is.EqualTo(checkpoint.Commit));
            Assert.That(
                service.GetCheckedOutBranchTipAsync(CancellationToken.None).GetAwaiter().GetResult(),
                Is.EqualTo(checkpoint.BaseTip));
            Assert.That(statusAfterConflict, Does.Contain("project.bep"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("local\n"));
            Assert.That(File.ReadAllText(lateEdit), Is.EqualTo("keep late edit\n"));
            Assert.That(cherryPickHeadExists, Is.False);
            Assert.That(interceptingRunner.Commands,
                Has.None.Matches<IReadOnlyList<string>>(
                    static arguments => arguments.FirstOrDefault() == "cherry-pick"));
        });
    }

    [Test]
    public async Task Rollback_refuses_when_branch_changed_after_expected_head_was_captured()
    {
        await CommitFileAsync("project.bep", "one\n", "one");
        using var service = CreateService();
        CheckedOutBranchTip target = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
        await CommitFileAsync("project.bep", "two\n", "two");
        CheckedOutBranchTip expectedCurrent = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
        await CommitFileAsync("project.bep", "external\n", "external");
        CheckedOutBranchTip externallyChanged = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);

        BranchTipRollbackResult rollback = await service.TryRollbackBranchTipAsync(
            expectedCurrent,
            target,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(rollback,
                Is.EqualTo(new BranchTipRollbackResult.RefChanged(externallyChanged.Commit)));
            Assert.That(
                service.GetCheckedOutBranchTipAsync(CancellationToken.None).GetAwaiter().GetResult(),
                Is.EqualTo(externallyChanged));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("external\n"));
        });
    }

    [Test]
    public async Task Project_tree_restore_and_recovery_preserve_enclosing_repository_staging()
    {
        string projectRoot = Path.Combine(Root, "project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "original\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "outside.txt"), "outside base\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "original");
        string originalCommit = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "current\n");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "current.belm"), "current asset\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "current");
        var nestedRepository = new RepositoryInfo(Root, projectRoot);
        using var service = CreateService(nestedRepository);
        CheckedOutBranchTip currentTip = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(Root, "outside.txt"), "outside staged\n");
        await RunGitAsync("add", "--", "outside.txt");

        CommitResult restore = await service.CommitProjectTreeAsync(
            currentTip,
            originalCommit,
            "beutl: restore project tree",
            SnapshotKind.Restore,
            CancellationToken.None);
        var restored = (CommitRevision.Known)((CommitResult.Committed)restore).Revision;
        var restoredTip = new CheckedOutBranchTip(currentTip.RefName, restored.Sha);
        string stagedAfterRestore = (await RunGitAsync("show", ":outside.txt")).Stdout;
        string committedOutsideAfterRestore = (await RunGitAsync(
            "show",
            "HEAD:outside.txt")).Stdout;
        string projectAfterRestore = File.ReadAllText(Path.Combine(projectRoot, "project.bep"));
        bool currentAssetAfterRestore = File.Exists(Path.Combine(projectRoot, "current.belm"));
        string restoreTrailer = (await RunGitAsync(
            "show",
            "-s",
            "--format=%(trailers:key=Beutl-Snapshot,valueonly)",
            "HEAD")).Stdout.Trim();

        CommitResult recovery = await service.CommitProjectTreeAsync(
            restoredTip,
            currentTip.Commit,
            "beutl: recover original project tree",
            SnapshotKind.Recovery,
            CancellationToken.None);
        string stagedAfterRecovery = (await RunGitAsync("show", ":outside.txt")).Stdout;
        string recoveryTrailer = (await RunGitAsync(
            "show",
            "-s",
            "--format=%(trailers:key=Beutl-Snapshot,valueonly)",
            "HEAD")).Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(restore, Is.TypeOf<CommitResult.Committed>());
            Assert.That(recovery, Is.TypeOf<CommitResult.Committed>());
            Assert.That(projectAfterRestore, Is.EqualTo("original\n"));
            Assert.That(currentAssetAfterRestore, Is.False);
            Assert.That(File.ReadAllText(Path.Combine(projectRoot, "project.bep")),
                Is.EqualTo("current\n"));
            Assert.That(File.ReadAllText(Path.Combine(projectRoot, "current.belm")),
                Is.EqualTo("current asset\n"));
            Assert.That(stagedAfterRestore, Is.EqualTo("outside staged\n"));
            Assert.That(stagedAfterRecovery, Is.EqualTo("outside staged\n"));
            Assert.That(committedOutsideAfterRestore, Is.EqualTo("outside base\n"));
            Assert.That(restoreTrailer, Is.EqualTo("restore"));
            Assert.That(recoveryTrailer, Is.EqualTo("recovery"));
        });
    }

    [Test]
    public async Task Linked_worktree_transition_uses_and_cleans_its_private_HEAD_lock()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        string baseCommit = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await CommitFileAsync("project.bep", "current\n", "current");
        string mainTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string linkedRoot = CreateTemporaryDirectory();
        await RunGitAsync("worktree", "add", "-b", "linked", linkedRoot, mainTip);
        var linkedRepository = new RepositoryInfo(linkedRoot, linkedRoot);
        using var service = CreateService(linkedRepository);
        CheckedOutBranchTip linkedTip = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        string gitFile = await File.ReadAllTextAsync(Path.Combine(linkedRoot, ".git"));
        string linkedGitDirectory = gitFile["gitdir:".Length..].Trim();
        if (!Path.IsPathFullyQualified(linkedGitDirectory))
        {
            linkedGitDirectory = Path.Combine(linkedRoot, linkedGitDirectory);
        }

        string linkedHeadLock = Path.Combine(Path.GetFullPath(linkedGitDirectory), "HEAD.lock");
        string mainHeadLock = Path.Combine(Root, ".git", "HEAD.lock");
        await File.WriteAllTextAsync(mainHeadLock, "main worktree sentinel");

        CommitResult result = await service.CommitProjectTreeAsync(
            linkedTip,
            baseCommit,
            "beutl: linked worktree transition",
            SnapshotKind.Restore,
            CancellationToken.None);
        string worktrees = (await RunGitAsync("worktree", "list", "--porcelain")).Stdout;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(File.ReadAllText(Path.Combine(linkedRoot, "project.bep")),
                Is.EqualTo("base\n"));
            Assert.That(File.Exists(linkedHeadLock), Is.False);
            Assert.That(File.ReadAllText(mainHeadLock), Is.EqualTo("main worktree sentinel"));
            Assert.That(
                (RunGitAsync("rev-parse", "refs/heads/main").GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(mainTip));
            Assert.That(worktrees, Does.Not.Contain("beutl-git-ref-update-"));
        });
        File.Delete(mainHeadLock);
    }

    [Test]
    public async Task Checkpoint_preserves_enclosing_repository_staging_and_pull_refuses_it()
    {
        string projectRoot = Path.Combine(Root, "project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "base\n");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "deleted.belm"), "delete me\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "outside.txt"), "outside base\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "initial");
        var nestedRepository = new RepositoryInfo(Root, projectRoot);
        using var service = CreateService(nestedRepository);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "project dirty\n");
        File.Delete(Path.Combine(projectRoot, "deleted.belm"));
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "added.belm"), "added\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "outside.txt"), "outside staged\n");
        await RunGitAsync("add", "--", "outside.txt");
        string stagedOutsideBefore = (await RunGitAsync("show", ":outside.txt")).Stdout;

        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: scoped safety checkpoint",
            CancellationToken.None);
        FastForwardPullResult pull = await service.PullFastForwardAsync(
            checkpoint.BaseTip,
            checkpoint,
            CancellationToken.None);
        string stagedOutsideAfter = (await RunGitAsync("show", ":outside.txt")).Stdout;
        string checkpointOutside = (await RunGitAsync(
            "show",
            $"{checkpoint.Commit}:outside.txt")).Stdout;
        await service.RestoreProjectCheckpointAsync(checkpoint, CancellationToken.None);
        string stagedOutsideAfterRestore = (await RunGitAsync("show", ":outside.txt")).Stdout;

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.RepositoryDirty>());
            Assert.That(stagedOutsideAfter, Is.EqualTo(stagedOutsideBefore));
            Assert.That(stagedOutsideAfterRestore, Is.EqualTo(stagedOutsideBefore));
            Assert.That(stagedOutsideAfter, Is.EqualTo("outside staged\n"));
            Assert.That(checkpointOutside, Is.EqualTo("outside base\n"));
            Assert.That(File.ReadAllText(Path.Combine(projectRoot, "project.bep")),
                Is.EqualTo("project dirty\n"));
            Assert.That(File.Exists(Path.Combine(projectRoot, "deleted.belm")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(projectRoot, "added.belm")),
                Is.EqualTo("added\n"));
            Assert.That(
                (RunGitAsync("rev-parse", checkpoint.RefName).GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(checkpoint.Commit));
        });
    }

    [Test]
    public async Task Checkpoint_creation_observes_a_ref_published_before_the_runner_response_failed()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        var interceptingRunner = new InterceptingRunner(
            CreateRunner(),
            static (_, arguments, _) =>
                arguments is
                [
                    "update-ref",
                    "--create-reflog",
                    "-m",
                    "beutl safety checkpoint",
                    ..
                ],
            before: null,
            static (_, _, _) => throw new IOException(
                "simulated lost checkpoint ref publication response"));
        using var service = CreateService(runner: interceptingRunner);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "checkpointed\n");
        CheckedOutBranchTip before = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: checkpoint",
            CancellationToken.None);
        CheckedOutBranchTip after = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        string published = (await RunGitAsync("rev-parse", checkpoint.RefName)).Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(checkpoint.BaseTip, Is.EqualTo(before));
            Assert.That(after, Is.EqualTo(before));
            Assert.That(published, Is.EqualTo(checkpoint.Commit));
            Assert.That(interceptingRunner.InterceptionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Detached_head_rejects_checkpoint_creation()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        await RunGitAsync("switch", "--detach");
        using var service = CreateService();

        Assert.That(
            async () => await service.CreateProjectCheckpointAsync(
                "beutl: checkpoint",
                CancellationToken.None),
            Throws.TypeOf<DetachedHeadNotSupportedException>());
    }

    [Test]
    public async Task Checkpoint_rejects_staged_project_changes_without_touching_the_index()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        using var service = CreateService();
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "staged\n");
        await RunGitAsync("add", "--", "project.bep");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "worktree\n");

        Assert.That(
            async () => await service.CreateProjectCheckpointAsync(
                "beutl: checkpoint",
                CancellationToken.None),
            Throws.TypeOf<ProjectCheckpointStagedChangesException>());

        string staged = (await RunGitAsync("show", ":project.bep")).Stdout;
        string refs = (await RunGitAsync(
            "for-each-ref",
            "--format=%(refname)",
            "refs/beutl/safety/")).Stdout;
        Assert.Multiple(() =>
        {
            Assert.That(staged, Is.EqualTo("staged\n"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("worktree\n"));
            Assert.That(refs, Is.Empty);
        });
    }

    [Test]
    public async Task Pull_refuses_project_changes_made_after_checkpoint_creation()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        using var service = CreateService();
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "checkpointed\n");
        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: checkpoint",
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "newer edit\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "after.belm"), "after checkpoint\n");

        FastForwardPullResult pull = await service.PullFastForwardAsync(
            checkpoint.BaseTip,
            checkpoint,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.RepositoryDirty>());
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("newer edit\n"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "after.belm")),
                Is.EqualTo("after checkpoint\n"));
            Assert.That(
                (RunGitAsync("rev-parse", checkpoint.RefName).GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(checkpoint.Commit));
        });
    }

    [Test]
    public async Task Detached_head_rejects_checkpointed_pull_and_preserves_new_edit()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        using var service = CreateService();
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "checkpointed\n");
        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: checkpoint",
            CancellationToken.None);
        await RunGitAsync("switch", "--detach");
        await File.WriteAllTextAsync(Path.Combine(Root, "detached.belm"), "keep me\n");

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await service.PullFastForwardAsync(
                    checkpoint.BaseTip,
                    checkpoint,
                    CancellationToken.None),
                Throws.TypeOf<DetachedHeadNotSupportedException>());
        });
        Assert.That(File.ReadAllText(Path.Combine(Root, "detached.belm")), Is.EqualTo("keep me\n"));
    }

    [Test]
    public async Task Checkpointed_pull_rejects_unrelated_history_on_the_same_branch_ref()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        using var service = CreateService();
        await File.WriteAllTextAsync(Path.Combine(Root, "local.belm"), "local edit\n");
        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: checkpoint",
            CancellationToken.None);
        await RunGitAsync("restore", "--source=HEAD", "--worktree", "--", ".");
        await RunGitAsync("clean", "-fd", "--", ".");
        string tree = (await RunGitAsync("rev-parse", "HEAD^{tree}")).Stdout.Trim();
        string unrelatedCommit = (await RunGitAsync(
            "commit-tree",
            tree,
            "-m",
            "unrelated history")).Stdout.Trim();
        await RunGitAsync(
            "update-ref",
            checkpoint.BaseTip.RefName,
            unrelatedCommit,
            checkpoint.BaseTip.Commit);

        Assert.That(
            async () => await service.PullFastForwardAsync(
                checkpoint.BaseTip,
                checkpoint,
                CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(
                service.GetCheckedOutBranchTipAsync(CancellationToken.None).GetAwaiter().GetResult().Commit,
                Is.EqualTo(unrelatedCommit));
            Assert.That(File.Exists(Path.Combine(Root, "local.belm")), Is.False);
            Assert.That(
                (RunGitAsync("rev-parse", checkpoint.RefName).GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(checkpoint.Commit));
        });
    }

    [Test]
    public async Task Restore_checkpoint_refuses_dirty_project_and_preserves_new_edit()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        using var service = CreateService();
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "checkpointed\n");
        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: checkpoint",
            CancellationToken.None);
        await RunGitAsync("restore", "--source=HEAD", "--worktree", "--", ".");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "new recovery edit\n");

        Assert.That(
            async () => await service.RestoreProjectCheckpointAsync(
                checkpoint,
                CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("new recovery edit\n"));
            Assert.That(
                (RunGitAsync("rev-parse", checkpoint.RefName).GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(checkpoint.Commit));
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
        var remote = new RepositoryInfo(remoteRoot, remoteRoot);
        GitCommandResult result = await CreateRunner().RunAsync(
            remote,
            ["rev-parse", "refs/heads/main"],
            GitCommandOptions.Local,
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

    private sealed class WorktreeBeforeTemporaryHeadFaultRunner(IGitCliRunner inner)
        : IGitCliRunner
    {
        private int _alignmentCount;
        private int _checkoutCount;
        private int _forwardFaulted;

        public bool HasActiveProcess => inner.HasActiveProcess;

        public int AlignmentCount => Volatile.Read(ref _alignmentCount);

        public int CheckoutCount => Volatile.Read(ref _checkoutCount);

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments is
                [
                    "-c",
                    "core.hooksPath=/dev/null",
                    "checkout",
                    "--detach",
                    "--no-overwrite-ignore",
                    ..
                ])
            {
                Interlocked.Increment(ref _checkoutCount);
                if (Interlocked.CompareExchange(ref _forwardFaulted, 1, 0) == 0)
                {
                    string targetCommit = arguments[^1];
                    await inner.RunAsync(
                        repository,
                        ["read-tree", "-u", "-m", targetCommit],
                        options,
                        cancellationToken,
                        stderrProgress);
                    throw new IOException(
                        "simulated checkout failure after the worktree and index advanced");
                }
            }

            if (arguments is
                [
                    "update-ref",
                    "--no-deref",
                    "-m",
                    "beutl align temporary transition head for recovery",
                    "HEAD",
                    _,
                    _
                ])
            {
                Interlocked.Increment(ref _alignmentCount);
            }

            return await inner.RunAsync(
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

    private sealed class CheckoutRecoveryFaultRunner(
        IGitCliRunner inner,
        bool failReverse,
        Func<Task>? afterReverse) : IGitCliRunner
    {
        private int _checkoutCount;

        public bool HasActiveProcess => inner.HasActiveProcess;

        public int CheckoutCount => Volatile.Read(ref _checkoutCount);

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments is
                [
                    "-c",
                    "core.hooksPath=/dev/null",
                    "checkout",
                    "--detach",
                    "--no-overwrite-ignore",
                    ..
                ])
            {
                int invocation = Interlocked.Increment(ref _checkoutCount);
                if (invocation == 2 && failReverse)
                {
                    throw new IOException("simulated reverse checkout failure");
                }

                GitCommandResult result = await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress);
                if (invocation == 1)
                {
                    throw new IOException("simulated lost forward checkout response");
                }

                if (invocation == 2 && afterReverse is not null)
                {
                    await afterReverse();
                }

                return result;
            }

            return await inner.RunAsync(
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

    private sealed class PrepareObserverFaultRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _prepareFaulted;
        private int _observerFaulted;

        public bool HasActiveProcess => inner.HasActiveProcess;

        public bool PrepareFaulted => Volatile.Read(ref _prepareFaulted) != 0;

        public bool ObserverFaulted => Volatile.Read(ref _observerFaulted) != 0;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (PrepareFaulted
                && !ObserverFaulted
                && arguments is ["rev-parse", "--verify", "--quiet", var revision]
                && revision.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                Interlocked.Exchange(ref _observerFaulted, 1);
                throw new IOException("simulated secondary ref observation failure");
            }

            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (!PrepareFaulted && arguments is ["read-tree", "--reset", ..])
            {
                Interlocked.Exchange(ref _prepareFaulted, 1);
                throw new IOException("simulated prepare reset observation failure");
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

    private sealed class CheckoutObserverFaultRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _checkoutFaulted;
        private int _observerFaulted;

        public bool HasActiveProcess => inner.HasActiveProcess;

        public bool CheckoutFaulted => Volatile.Read(ref _checkoutFaulted) != 0;

        public bool ObserverFaulted => Volatile.Read(ref _observerFaulted) != 0;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (CheckoutFaulted
                && !ObserverFaulted
                && arguments is ["rev-parse", "--verify", "--quiet", var revision]
                && revision.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                Interlocked.Exchange(ref _observerFaulted, 1);
                throw new IOException("simulated post-checkout ref observation failure");
            }

            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (!CheckoutFaulted
                && arguments is
                [
                    "-c",
                    "core.hooksPath=/dev/null",
                    "checkout",
                    "--detach",
                    "--no-overwrite-ignore",
                    ..
                ])
            {
                Interlocked.Exchange(ref _checkoutFaulted, 1);
                throw new IOException("simulated lost checkout response");
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
