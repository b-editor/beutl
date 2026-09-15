using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Api.Services;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.UnitTests.Api;

public partial class PackageInstallerDataTests
{
    [Test]
    public async Task RecoveryReview_CancellationStopsTheLockWait()
    {
        using var held = new FileStream(Path.Combine(Helper.AppRoot, ".data-install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task recovery = Task.Run(() => PackageInstaller.RecoverDataPackageInstalls(_repository,
            step => { if (step == "waiting-for-lock") waiting.TrySetResult(); }, cancellation.Token));
        try
        {
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await recovery.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            cancellation.Cancel();
            held.Dispose();
            try { await recovery.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    [Test]
    public async Task RecoveryReview_WaitsForPublicationLockToBeReleased()
    {
        using var held = new FileStream(Path.Combine(Helper.AppRoot, ".data-install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task recovery = Task.Run(() =>
        {
            started.SetResult();
            PackageInstaller.RecoverDataPackageInstalls(_repository);
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(150);
            Assert.That(recovery.IsCompleted, Is.False, "Startup should wait for the publisher, rather than fault or bypass recovery.");
        }
        finally
        {
            held.Dispose();
            await recovery.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestCase("missing", false)]
    [TestCase("file", false)]
    [TestCase("wrong-owner", false)]
    [TestCase("missing", true)]
    [TestCase("file", true)]
    [TestCase("wrong-owner", true)]
    public void RecoveryReview_InvalidStagedPayloadPreservesBothOriginals(string damage, bool duringPublication)
    {
        const string name = "Beutl.Package.DataTest.InvalidStaging";
        LocalPackage old = CreateDataPackage(name, [PackageKinds.MaterialTag, PackageKinds.TemplateTag], "1.0.0",
            [("materials/item.txt", "old"), ("templates/item.txt", "old")]);
        LocalPackage current = CreateDataPackage(name, [PackageKinds.MaterialTag, PackageKinds.TemplateTag], "2.0.0",
            [("materials/item.txt", "new"), ("templates/item.txt", "new")]);
        _installer.InstallDataPackage(old);
        using var deployment = _installer.PrepareDataPackage(current, CancellationToken.None);
        string staging = Directory.GetDirectories(Helper.AppRoot, ".data-install-*").Single();
        Track(staging);
        void Damage()
        {
            string templates = Path.Combine(staging, "templates");
            if (damage == "wrong-owner") File.WriteAllText(Path.Combine(templates, ".beutl-package-owner"), "{}");
            else
            {
                Directory.Delete(templates, true);
                if (damage == "file") File.WriteAllText(templates, "not a payload");
            }
        }
        if (duringPublication) _installer.AfterDataInstallStep = step => { if (step == "publish-materials") Damage(); };
        else Damage();
        bool registered = false;
        Assert.Throws<IOException>(() => deployment.Commit(() => registered = true));
        Assert.Multiple(() =>
        {
            Assert.That(registered, Is.False);
            Assert.That(File.ReadAllText(Path.Combine(MaterialsDirectoryOf(name), "item.txt")), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(Path.Combine(TemplatesDirectoryOf(name), "item.txt")), Is.EqualTo("old"));
        });
    }

    [Test]
    public async Task RecoveryReview_ObserversRunAfterCommitAndCanWaitForInstallerWork()
    {
        const string name = "Beutl.Package.DataTest.RecoveryObserver";
        string staging = CreatePublishedRecoveryJournal(name);
        Directory.Delete(Path.Combine(staging, "install.json.tmp"));
        Task<bool>? observerWork = null;
        bool observedTerminalState = false;
        bool completedInsideObserver = false;
        using IDisposable subscription = _repository.GetPackageObservable(name).Subscribe(identity =>
        {
            if (identity?.Version != NuGetVersion.Parse("2.0.0") || observerWork is not null) return;
            string journal = Path.Combine(staging, "install.json");
            observedTerminalState = !File.Exists(journal) || JsonNode.Parse(File.ReadAllText(journal))!["Phase"]!.GetValue<int>() == 3;
            observerWork = Task.Run(() => _installer.UninstallDataPackage(name + ".Observer"));
            completedInsideObserver = observerWork.Wait(TimeSpan.FromSeconds(2));
        });
        PackageInstaller.RecoverDataPackageInstalls(_repository);
        Assert.That(observerWork, Is.Not.Null);
        await observerWork!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(observedTerminalState, Is.True);
            Assert.That(completedInsideObserver, Is.True, "The observer must be able to wait for work requiring both payload locks.");
        });
    }

    [Test]
    public void RecoveryReview_FailedTerminalRecordDoesNotNotifyObservers()
    {
        const string name = "Beutl.Package.DataTest.RecoveryNotificationFailure";
        string staging = CreatePublishedRecoveryJournal(name);
        int notifications = 0;
        using IDisposable subscription = _repository.GetPackageObservable(name).Subscribe(identity =>
        {
            if (identity?.Version == NuGetVersion.Parse("2.0.0")) notifications++;
        });
        PackageInstaller.RecoverDataPackageInstalls(_repository);
        Assert.That(notifications, Is.Zero);
        Assert.That(Directory.Exists(staging), Is.True);
        Directory.Delete(Path.Combine(staging, "install.json.tmp"));
        PackageInstaller.RecoverDataPackageInstalls(_repository);
        Assert.That(notifications, Is.GreaterThan(0));
        Assert.That(Directory.Exists(staging), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecoveryReview_LockSymlinkIsRejectedEvenWhenDangling(bool dangling)
    {
        string path = Path.Combine(Helper.AppRoot, ".data-install.lock");
        string target = Path.Combine(Path.GetTempPath(), "beutl-lock-target-" + Guid.NewGuid().ToString("N"));
        File.Delete(path);
        try
        {
            if (!dangling) File.WriteAllText(target, "unowned");
            try { File.CreateSymbolicLink(path, target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Ignore($"Symbolic links are unavailable here: {ex.Message}");
            }
            Assert.Throws<IOException>(() => PackageInstaller.RecoverDataPackageInstalls(_repository));
            Assert.That(new FileInfo(path).LinkTarget, Is.EqualTo(target));
            if (dangling) Assert.That(File.Exists(target), Is.False);
            else Assert.That(File.ReadAllText(target), Is.EqualTo("unowned"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(target);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecoveryReview_StagingSymlinkDoesNotTraverseOrCleanItsTarget(bool dangling)
    {
        string id = Guid.NewGuid().ToString("N");
        string staging = Path.Combine(Helper.AppRoot, ".data-install-" + id);
        string target = Path.Combine(Path.GetTempPath(), "beutl-stage-target-" + id);
        const string name = "Beutl.Package.DataTest.LinkedStage";
        bool linkCreated = false;
        try
        {
            if (!dangling)
            {
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "keep.txt"), "unowned");
                File.WriteAllText(Path.Combine(target, "install.json"), JsonSerializer.Serialize(new
                {
                    Format = 1,
                    DeploymentId = id,
                    Name = name,
                    Version = "1.0.0",
                    MaterialsDestination = MaterialsDirectoryOf(name),
                    TemplatesDestination = TemplatesDirectoryOf(name),
                    RegisterPackage = false,
                    Phase = 3,
                    Payloads = Array.Empty<object>(),
                }));
            }
            try
            {
                Directory.CreateSymbolicLink(staging, target);
                linkCreated = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Ignore($"Symbolic links are unavailable here: {ex.Message}");
            }
            PackageInstaller.RecoverDataPackageInstalls(_repository);
            Assert.That(new DirectoryInfo(staging).LinkTarget, Is.EqualTo(target));
            if (dangling) Assert.That(Directory.Exists(target), Is.False);
            else Assert.That(File.ReadAllText(Path.Combine(target, "keep.txt")), Is.EqualTo("unowned"));
        }
        finally
        {
            if (linkCreated)
            {
                if (OperatingSystem.IsWindows()) Directory.Delete(staging);
                else File.Delete(staging);
            }
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecoveryReview_UnverifiableLegacyBackupIsRetained(bool replaceBackup)
    {
        const string name = "Beutl.Package.DataTest.LegacyRecovery";
        string staging = CreatePublishedRecoveryJournal(name);
        Directory.Delete(Path.Combine(staging, "install.json.tmp"));
        string journalPath = Path.Combine(staging, "install.json");
        JsonNode journal = JsonNode.Parse(File.ReadAllText(journalPath))!;
        journal["Phase"] = 0;
        journal["Payloads"]![0]!["OriginalOwner"] = null;
        File.WriteAllText(journalPath, journal.ToJsonString());
        string backup = Path.Combine(staging, "backup-materials");
        File.Delete(Path.Combine(backup, ".beutl-package-owner"));
        if (replaceBackup)
        {
            Directory.Delete(backup, true);
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "item.txt"), "unowned replacement");
        }
        PackageInstaller.RecoverDataPackageInstalls(_repository);
        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(staging), Is.True);
            Assert.That(File.Exists(Path.Combine(backup, "item.txt")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(MaterialsDirectoryOf(name), "item.txt")), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(Path.Combine(TemplatesDirectoryOf(name), "item.txt")), Is.EqualTo("new"));
        });
    }

    private string CreatePublishedRecoveryJournal(string name)
    {
        LocalPackage old = CreateDataPackage(name, [PackageKinds.MaterialTag, PackageKinds.TemplateTag], "1.0.0",
            [("materials/item.txt", "old"), ("templates/item.txt", "old")]);
        LocalPackage current = CreateDataPackage(name, [PackageKinds.MaterialTag, PackageKinds.TemplateTag], "2.0.0",
            [("materials/item.txt", "new"), ("templates/item.txt", "new")]);
        _installer.InstallDataPackage(old);
        string? staging = null;
        _installer.AfterDataInstallStep = step =>
        {
            if (step != "registered") return;
            staging = Directory.GetDirectories(Helper.AppRoot, ".data-install-*").Single();
            Track(staging);
            Directory.CreateDirectory(Path.Combine(staging, "install.json.tmp"));
        };
        using (var deployment = _installer.PrepareDataPackage(current, CancellationToken.None)) deployment.Commit(() => { });
        _installer.AfterDataInstallStep = null;
        return staging!;
    }
}
