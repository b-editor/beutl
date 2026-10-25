using System.Text.Json;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public class InstalledPackageRepositoryTests
{
    private static string InstalledPackagesFile => Path.Combine(Helper.AppRoot, "installedPackages.json");

    private static string BackupFile => InstalledPackagesFile + ".bak";

    [SetUp]
    public void SetUp()
    {
        Assert.That(
            Helper.AppRoot,
            Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        File.Delete(InstalledPackagesFile);
        File.Delete(BackupFile);
    }

    [TearDown]
    public void TearDown()
    {
        Assert.That(
            Helper.AppRoot,
            Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        File.Delete(InstalledPackagesFile);
        File.Delete(BackupFile);
    }

    [Test]
    public void Restore_SkipsOnlyTheInvalidEntryAndKeepsTheOtherRegistrations()
    {
        const string original = """
            [
              { "Name": "Beutl.Package.RestoreTest.One", "Version": "1.0.0" },
              { "Name": "Beutl.Package.RestoreTest.Two", "Version": "not-a-version" },
              null,
              { "Name": "Beutl.Package.RestoreTest.BadType", "Version": 123 },
              { "Name": "Beutl.Package.RestoreTest.Three", "Version": "3.0.0", "BeutlVersion": "0.0.0" }
            ]
            """;
        File.WriteAllText(InstalledPackagesFile, original);

        var repo = new InstalledPackageRepository();

        Assert.Multiple(() =>
        {
            Assert.That(repo.ExistsPackage("Beutl.Package.RestoreTest.One", "1.0.0"), Is.True);
            Assert.That(repo.ExistsPackage("Beutl.Package.RestoreTest.Two"), Is.False);
            Assert.That(repo.ExistsPackage("Beutl.Package.RestoreTest.Three", "3.0.0"), Is.True);
            Assert.That(repo.RestoreFailed, Is.False,
                "valid entries remain, so saving may drop the invalid ones");
            Assert.That(File.Exists(BackupFile), Is.True,
                "a later save drops the invalid entry, so the file as read is kept");
            Assert.That(File.ReadAllText(BackupFile), Is.EqualTo(original));
        });

        repo.SetResolvedBeutlVersion("Beutl.Package.RestoreTest.One", "1.0.0");

        Entry[] saved = JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(InstalledPackagesFile))!;
        Assert.That(saved.Select(entry => entry.Name), Is.EquivalentTo(new[]
        {
            "Beutl.Package.RestoreTest.One",
            "Beutl.Package.RestoreTest.Three",
        }));
    }

    [Test]
    public void Restore_UnreadableFile_BacksUpAndBlocksSaving()
    {
        const string original = "{ this is not a package array";
        File.WriteAllText(InstalledPackagesFile, original);

        var repo = new InstalledPackageRepository();

        Assert.Multiple(() =>
        {
            Assert.That(repo.GetLocalPackages(), Is.Empty);
            Assert.That(repo.RestoreFailed, Is.True);
            Assert.That(File.Exists(BackupFile), Is.True);
            Assert.That(File.ReadAllText(BackupFile), Is.EqualTo(original));
        });

        repo.SetResolvedBeutlVersion("Beutl.Package.RestoreTest.One", "1.0.0");

        Assert.That(File.ReadAllText(InstalledPackagesFile), Is.EqualTo(original),
            "an unreadable registry must not be replaced by the empty in-memory list");
    }

    [Test]
    public void Restore_NoEntrySurvives_BlocksSavingTheEmptyList()
    {
        const string original = """
            [
              { "Name": "Beutl.Package.RestoreTest.One", "Version": "not-a-version" }
            ]
            """;
        File.WriteAllText(InstalledPackagesFile, original);

        var repo = new InstalledPackageRepository();

        Assert.Multiple(() =>
        {
            Assert.That(repo.GetLocalPackages(), Is.Empty);
            Assert.That(repo.RestoreFailed, Is.True);
            Assert.That(File.Exists(BackupFile), Is.True);
        });

        repo.SetResolvedBeutlVersion("Beutl.Package.RestoreTest.One", "1.0.0");

        Assert.That(File.ReadAllText(InstalledPackagesFile), Is.EqualTo(original));
    }

    private sealed record Entry(string Name, string Version, string? BeutlVersion);

    [Test]
    public void FailedUpgradePersistence_KeepsThePreviousInMemoryRegistration()
    {
        var repo = new InstalledPackageRepository();
        var old = new PackageIdentity("Beutl.Package.UpdateTest.FailedSave", NuGetVersion.Parse("1.0.0"));
        var next = new PackageIdentity(old.Id, NuGetVersion.Parse("2.0.0"));
        repo.UpgradePackagesAndDeferNotifications(old)();
        string backup = InstalledPackagesFile + ".test-backup";
        File.Move(InstalledPackagesFile, backup);
        Directory.CreateDirectory(InstalledPackagesFile);
        try
        {
            Assert.Catch(() => repo.UpgradePackagesAndDeferNotifications(next)());
            Assert.That(repo.GetLocalPackages(old.Id), Is.EqualTo(new[] { old }));
        }
        finally
        {
            Directory.Delete(InstalledPackagesFile);
            File.Move(backup, InstalledPackagesFile);
        }
        Assert.That(new InstalledPackageRepository().GetLocalPackages(old.Id), Is.EqualTo(new[] { old }));
    }

    [Test]
    public void UpgradeNotificationFailure_DoesNotFailTheCommittedRegistration()
    {
        var repo = new InstalledPackageRepository();
        var next = new PackageIdentity("Beutl.Package.UpdateTest.ObserverFailure", NuGetVersion.Parse("2.0.0"));
        using var subscription = repo.GetPackageObservable(next.Id).Subscribe(value =>
        {
            if (value is not null) throw new InvalidOperationException("observer failed");
        });
        Assert.DoesNotThrow(() => repo.UpgradePackagesAndDeferNotifications(next)());
        Assert.That(new InstalledPackageRepository().ExistsPackage(next), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ThrowingPackageObserver_DoesNotStarveLaterObservers(bool reuseObservable)
    {
        var repository = new InstalledPackageRepository();
        const string name = "Beutl.Package.UpdateTest.ObserverIsolation";
        var first = new PackageIdentity(name, NuGetVersion.Parse("1.0.0"));
        var second = new PackageIdentity(name, NuGetVersion.Parse("2.0.0"));
        var observable = repository.GetPackageObservable(name);
        using var broken = observable.Subscribe(value =>
        {
            if (value is not null) throw new InvalidOperationException("observer failed");
        });
        var received = new List<PackageIdentity?>();
        using var healthy = (reuseObservable ? observable : repository.GetPackageObservable(name)).Subscribe(received.Add);

        repository.UpgradePackagesAndDeferNotifications(first)();
        repository.UpgradePackagesAndDeferNotifications(second)();

        Assert.That(received, Does.Contain(first));
        Assert.That(received.Last(), Is.EqualTo(second));
        Assert.That(new InstalledPackageRepository().ExistsPackage(second), Is.True);
    }

    [Test]
    public void ThrowingBooleanPackageObserver_DoesNotStarveLaterObservers()
    {
        var repository = new InstalledPackageRepository();
        const string name = "Beutl.Package.UpdateTest.BooleanObserverIsolation";
        var identity = new PackageIdentity(name, NuGetVersion.Parse("1.0.0"));
        var observable = repository.GetObservable(name);
        using var broken = observable.Subscribe(value =>
        {
            if (value) throw new InvalidOperationException("observer failed");
        });
        var received = new List<bool>();
        using var healthy = observable.Subscribe(received.Add);
        repository.UpgradePackagesAndDeferNotifications(identity)();
        repository.RemovePackage(identity);
        Assert.That(received, Is.EqualTo(new[] { false, true, false }));
    }

    [Test]
    public void GetPackageObservable_EmitsNull_WhenNotInstalled()
    {
        const string name = "Beutl.Package.UpdateTest.None";
        var repo = new InstalledPackageRepository();

        PackageIdentity? emitted = new(name, NuGetVersion.Parse("0.0.0"));
        repo.GetPackageObservable(name).Subscribe(x => emitted = x);

        Assert.That(emitted, Is.Null);
    }

    [Test]
    public void GetPackageObservable_EmitsInstalledIdentity_AfterUpgrade()
    {
        const string name = "Beutl.Package.UpdateTest.Upgrade";
        var repo = new InstalledPackageRepository();

        PackageIdentity? emitted = new(name, NuGetVersion.Parse("0.0.0"));
        repo.GetPackageObservable(name).Subscribe(x => emitted = x);

        repo.UpgradePackagesAndDeferNotifications(new PackageIdentity(name, NuGetVersion.Parse("1.0.0")))();
        Assert.That(emitted?.Version.ToString(), Is.EqualTo("1.0.0"));

        repo.UpgradePackagesAndDeferNotifications(new PackageIdentity(name, NuGetVersion.Parse("2.0.0")))();
        Assert.That(emitted?.Version.ToString(), Is.EqualTo("2.0.0"));
    }

    [Test]
    public void GetObservable_Bool_DoesNotFlashFalse_DuringUpgrade()
    {
        const string name = "Beutl.Package.UpdateTest.Flash";
        var repo = new InstalledPackageRepository();
        repo.UpgradePackagesAndDeferNotifications(new PackageIdentity(name, NuGetVersion.Parse("1.0.0")))();

        var emissions = new List<bool>();
        repo.GetObservable(name).Subscribe(x => emissions.Add(x));

        repo.UpgradePackagesAndDeferNotifications(new PackageIdentity(name, NuGetVersion.Parse("2.0.0")))();

        Assert.That(emissions, Does.Not.Contains(false));
    }
}
