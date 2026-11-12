using Beutl.Services.StartupTasks;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class CheckForUpdatesTaskTests
{
    [Test]
    public void VersionBelowTheMinimum_RequiresAnUpgrade()
    {
        // The update server sends mustLatest only together with isLatest = false (v1 and v3 responses).
        Assert.That(
            CheckForUpdatesTask.Classify(isLatest: false, mustLatest: true),
            Is.EqualTo(CheckForUpdatesTask.UpdateCheckResult.UpgradeRequired));
    }

    [Test]
    public void OlderVersion_GetsTheUpdateNotification()
    {
        Assert.That(
            CheckForUpdatesTask.Classify(isLatest: false, mustLatest: false),
            Is.EqualTo(CheckForUpdatesTask.UpdateCheckResult.UpdateAvailable));
    }

    [Test]
    public void LatestVersion_IsUpToDate()
    {
        Assert.That(
            CheckForUpdatesTask.Classify(isLatest: true, mustLatest: false),
            Is.EqualTo(CheckForUpdatesTask.UpdateCheckResult.UpToDate));
    }

    // Running instances share the update folders. While one holds them, no other can claim them and start an update
    // there unasked, until the first lets go.
    [Test]
    public void OnlyOneHolderAtATimeClaimsTheUpdateFolders()
    {
        string directory = Directory.CreateTempSubdirectory("beutl-update-claim").FullName;
        string path = Path.Combine(directory, "update.lock");
        try
        {
            using (FileStream? first = CheckForUpdatesTask.TryClaim(path))
            {
                Assert.That(first, Is.Not.Null);
                using FileStream? second = CheckForUpdatesTask.TryClaim(path);
                Assert.That(second, Is.Null);
            }

            using FileStream? afterRelease = CheckForUpdatesTask.TryClaim(path);
            Assert.That(afterRelease, Is.Not.Null);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void UpgradeRequired_No_KeepsBeutlRunningWithoutOpeningTheBrowser()
    {
        bool opened = false;
        bool closed = false;

        // The dialog's "No" is its close button, which returns None.
        CheckForUpdatesTask.HandleUpgradeRequiredAnswer(
            FAContentDialogResult.None,
            () => opened = true,
            () => closed = true);

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.False);
            Assert.That(closed, Is.False);
        });
    }

    [Test]
    public void UpgradeRequired_Yes_OpensTheReleasePageAndClosesBeutl()
    {
        bool opened = false;
        bool closed = false;

        CheckForUpdatesTask.HandleUpgradeRequiredAnswer(
            FAContentDialogResult.Primary,
            () => opened = true,
            () => closed = true);

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.True);
            Assert.That(closed, Is.True);
        });
    }

    [Test]
    public void UpgradeRequired_Yes_KeepsBeutlRunningWhenTheReleasePageDoesNotOpen()
    {
        bool closed = false;

        CheckForUpdatesTask.HandleUpgradeRequiredAnswer(
            FAContentDialogResult.Primary,
            () => false,
            () => closed = true);

        Assert.That(closed, Is.False);
    }
}
