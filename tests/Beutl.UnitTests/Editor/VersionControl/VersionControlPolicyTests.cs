using Beutl.Configuration;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public sealed class VersionControlPolicyTests : RealGitTestRepository
{
    [Test]
    public async Task InitializeAsync_awaits_large_media_notice_before_initial_add_and_commit()
    {
        var config = new VersionControlConfig
        {
            LargeMediaWarningThresholdMb = 1,
        };
        var notices = new List<VersionControlPolicyNotice>();
        string mediaPath = Path.Combine(Root, "resources", "large.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllBytesAsync(mediaPath, new byte[(1024 * 1024) + 1]);
        using var service = CreateService(
            config,
            lfsInstalled: false,
            async notice =>
            {
                GitCommandResult staged = await RunGitAsync(
                    "diff",
                    "--cached",
                    "--name-only");
                Assert.That(staged.Stdout, Is.Empty);
                notices.Add(notice);
            });

        await service.InitializeAsync(
            new InitOptions(Repository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        GitCommandResult count = await RunGitAsync("rev-list", "--count", "HEAD");
        Assert.Multiple(() =>
        {
            Assert.That(count.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(notices, Has.Count.EqualTo(1));
            Assert.That(notices[0], Is.TypeOf<VersionControlPolicyNotice.LargeMediaWithoutLfs>());
            Assert.That(
                ((VersionControlPolicyNotice.LargeMediaWithoutLfs)notices[0]).Path,
                Is.EqualTo("resources/large.mp4"));
        });
    }

    [Test]
    public async Task Large_media_without_lfs_warns_once_and_does_not_block_commits()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        var config = new VersionControlConfig
        {
            LargeMediaWarningThresholdMb = 1,
        };
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            config,
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });
        string mediaPath = Path.Combine(Root, "resources", "large.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllBytesAsync(mediaPath, new byte[(1024 * 1024) + 1]);

        CommitResult first = await service.CommitAllAsync(
            "large media",
            SnapshotKind.Manual,
            CancellationToken.None);
        await File.AppendAllTextAsync(mediaPath, "more");
        CommitResult second = await service.CommitAllAsync(
            "large media update",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.TypeOf<CommitResult.Committed>());
            Assert.That(second, Is.TypeOf<CommitResult.Committed>());
            Assert.That(notices, Has.Count.EqualTo(1));
            Assert.That(notices[0], Is.TypeOf<VersionControlPolicyNotice.LargeMediaWithoutLfs>());
            var notice = (VersionControlPolicyNotice.LargeMediaWithoutLfs)notices[0];
            Assert.That(notice.Path, Is.EqualTo("resources/large.mp4"));
            Assert.That(notice.SizeBytes, Is.GreaterThan(1024 * 1024));
        });
    }

    [Test]
    public async Task First_remote_with_active_lfs_shows_one_quota_notice()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "resources/**/*.mp4 filter=lfs diff=lfs merge=lfs -text\n");
        var config = new VersionControlConfig { UseLfsWhenAvailable = true };
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            config,
            lfsInstalled: true,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        await service.SetRemoteAsync(
            Path.Combine(Root, "remote-one.git"),
            CancellationToken.None);
        await RunGitAsync("remote", "remove", "origin");
        await service.SetRemoteAsync(
            Path.Combine(Root, "remote-two.git"),
            CancellationToken.None);

        Assert.That(
            notices,
            Is.EqualTo(new[]
            {
                new VersionControlPolicyNotice.LfsRemoteQuota(),
            }));
    }

    [Test]
    public async Task Warning_presentation_failure_never_blocks_the_commit()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        var config = new VersionControlConfig
        {
            LargeMediaWarningThresholdMb = 1,
        };
        using var service = CreateService(
            config,
            lfsInstalled: false,
            _ => throw new InvalidOperationException("Notification surface unavailable."));
        string mediaPath = Path.Combine(Root, "resources", "large.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllBytesAsync(mediaPath, new byte[(1024 * 1024) + 1]);

        CommitResult result = await service.CommitAllAsync(
            "large media",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<CommitResult.Committed>());
    }

    [Test]
    public async Task Missing_identity_notice_is_once_per_repository_and_commit_resumes_after_identity_is_set()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "changed\n");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        foreach (SnapshotKind kind in new[]
                 {
                     SnapshotKind.Save,
                     SnapshotKind.Close,
                     SnapshotKind.Safety,
                     SnapshotKind.Restore,
                     SnapshotKind.Recovery,
                 })
        {
            Assert.That(
                await service.CommitAllAsync("automatic snapshot", kind, CancellationToken.None),
                Is.TypeOf<CommitResult.SkippedNoIdentity>());
        }

        await service.SetLocalIdentityAsync(
            new GitIdentity("Local User", "local@example.invalid"),
            CancellationToken.None);
        CommitResult committed = await service.CommitAllAsync(
            "automatic snapshot",
            SnapshotKind.Save,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.TypeOf<CommitResult.Committed>());
            Assert.That(notices, Has.Count.EqualTo(1));
            Assert.That(notices[0], Is.TypeOf<VersionControlPolicyNotice.MissingIdentity>());
        });
    }

    [Test]
    public async Task Missing_identity_notice_is_repository_local()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "changed\n");

        string secondRoot = CreateTemporaryDirectory();
        var secondRepository = new RepositoryInfo(secondRoot, secondRoot);
        GitCliRunner secondRunner = CreateRunner();
        await secondRunner.RunAsync(
            secondRepository,
            ["init", "-b", "main"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await secondRunner.RunAsync(
            secondRepository,
            ["config", "--local", "user.name", "Second User"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await secondRunner.RunAsync(
            secondRepository,
            ["config", "--local", "user.email", "second@example.invalid"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(secondRoot, "project.bep"), "initial\n");
        await secondRunner.RunAsync(
            secondRepository,
            ["add", "--", "project.bep"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await secondRunner.RunAsync(
            secondRepository,
            ["commit", "-m", "initial"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await secondRunner.RunAsync(
            secondRepository,
            ["config", "--local", "user.name", ""],
            GitCommandOptions.Local,
            CancellationToken.None);
        await secondRunner.RunAsync(
            secondRepository,
            ["config", "--local", "user.email", ""],
            GitCommandOptions.Local,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(secondRoot, "project.bep"), "changed\n");

        var notices = new List<VersionControlPolicyNotice>();
        Task PresentNotice(VersionControlPolicyNotice notice)
        {
            notices.Add(notice);
            return Task.CompletedTask;
        }

        using var first = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            PresentNotice);
        using var second = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            PresentNotice,
            secondRepository);

        await first.CommitAllAsync("automatic snapshot", SnapshotKind.Save, CancellationToken.None);
        await second.CommitAllAsync("automatic snapshot", SnapshotKind.Save, CancellationToken.None);

        Assert.That(
            notices.Select(static notice => notice.GetType()),
            Is.EqualTo(new[]
            {
                typeof(VersionControlPolicyNotice.MissingIdentity),
                typeof(VersionControlPolicyNotice.MissingIdentity),
            }));
    }

    [Test]
    public async Task Project_checkpoint_creation_reports_missing_identity_before_mutation()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await File.WriteAllTextAsync(Path.Combine(Root, "local.belm"), "local edit\n");
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        Assert.ThrowsAsync<GitIdentityRequiredException>(
            async () => await service.CreateProjectCheckpointAsync(
                "safety checkpoint",
                CancellationToken.None));
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        GitCommandResult checkpoints = await RunGitAsync(
            "for-each-ref",
            "--format=%(refname)",
            "refs/beutl/checkpoints");

        Assert.Multiple(() =>
        {
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(checkpoints.Stdout, Is.Empty);
            Assert.That(notices.Single(), Is.TypeOf<VersionControlPolicyNotice.MissingIdentity>());
        });
    }

    [Test]
    public async Task Project_tree_skip_reports_missing_identity()
    {
        await CommitFileAsync("project.bep", "original\n", "original");
        string original = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await CommitFileAsync("project.bep", "current\n", "current");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });
        CheckedOutBranchTip current = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");

        CommitResult result = await service.CommitProjectTreeAsync(
            current,
            original,
            "restore snapshot",
            SnapshotKind.Restore,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.SkippedNoIdentity>());
            Assert.That(notices.Single(), Is.TypeOf<VersionControlPolicyNotice.MissingIdentity>());
        });
    }

    private GitCliVersionControlService CreateService(
        VersionControlConfig config,
        bool lfsInstalled,
        Func<VersionControlPolicyNotice, Task> presentNotice,
        RepositoryInfo? repository = null)
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled, config),
            repository ?? Repository,
            static () => true,
            (notice, _) => presentNotice(notice));
    }
}
