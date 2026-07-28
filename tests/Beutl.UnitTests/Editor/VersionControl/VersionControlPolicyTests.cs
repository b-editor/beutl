using Beutl.Configuration;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public sealed class VersionControlPolicyTests : RealGitTestRepository
{
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
            Assert.That(notices[0].Kind,
                Is.EqualTo(VersionControlPolicyNoticeKind.LargeMediaWithoutLfs));
            Assert.That(notices[0].Path, Is.EqualTo("resources/large.mp4"));
            Assert.That(notices[0].SizeBytes, Is.GreaterThan(1024 * 1024));
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
                new VersionControlPolicyNotice(
                    VersionControlPolicyNoticeKind.LfsRemoteQuota),
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

    private GitCliVersionControlService CreateService(
        VersionControlConfig config,
        bool lfsInstalled,
        Func<VersionControlPolicyNotice, Task> presentNotice)
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled, config),
            Repository,
            static () => true,
            (notice, _) => presentNotice(notice));
    }
}
