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

    [SetUp]
    public void SetUp()
    {
        Assert.That(
            Helper.AppRoot,
            Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        File.Delete(InstalledPackagesFile);
    }

    [TearDown]
    public void TearDown()
    {
        Assert.That(
            Helper.AppRoot,
            Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        File.Delete(InstalledPackagesFile);
    }

    [Test]
    public void FailedUpgradePersistence_KeepsThePreviousInMemoryRegistration()
    {
        var repo = new InstalledPackageRepository();
        var old = new PackageIdentity("Beutl.Package.UpdateTest.FailedSave", NuGetVersion.Parse("1.0.0"));
        var next = new PackageIdentity(old.Id, NuGetVersion.Parse("2.0.0"));
        repo.UpgradePackages(old);
        string backup = InstalledPackagesFile + ".test-backup";
        File.Move(InstalledPackagesFile, backup);
        Directory.CreateDirectory(InstalledPackagesFile);
        try
        {
            Assert.Catch(() => repo.UpgradePackages(next));
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
        Assert.DoesNotThrow(() => repo.UpgradePackages(next));
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

        repository.UpgradePackages(first);
        repository.UpgradePackages(second);

        Assert.That(received, Does.Contain(first));
        Assert.That(received.Last(), Is.EqualTo(second));
        Assert.That(new InstalledPackageRepository().ExistsPackage(second), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ThrowingBooleanPackageObserver_DoesNotStarveLaterObservers(bool specificVersion)
    {
        var repository = new InstalledPackageRepository();
        const string name = "Beutl.Package.UpdateTest.BooleanObserverIsolation";
        var identity = new PackageIdentity(name, NuGetVersion.Parse("1.0.0"));
        var observable = repository.GetObservable(name, specificVersion ? "1.0.0" : null);
        using var broken = observable.Subscribe(value =>
        {
            if (value) throw new InvalidOperationException("observer failed");
        });
        var received = new List<bool>();
        using var healthy = observable.Subscribe(received.Add);
        repository.UpgradePackages(identity);
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

        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse("1.0.0")));
        Assert.That(emitted?.Version.ToString(), Is.EqualTo("1.0.0"));

        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse("2.0.0")));
        Assert.That(emitted?.Version.ToString(), Is.EqualTo("2.0.0"));
    }

    [Test]
    public void GetObservable_Bool_DoesNotFlashFalse_DuringUpgrade()
    {
        const string name = "Beutl.Package.UpdateTest.Flash";
        var repo = new InstalledPackageRepository();
        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse("1.0.0")));

        var emissions = new List<bool>();
        repo.GetObservable(name).Subscribe(x => emissions.Add(x));

        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse("2.0.0")));

        Assert.That(emissions, Does.Not.Contains(false));
    }

    [Test]
    public void GetObservable_ForVersion_TracksEquivalentIdentityInstances()
    {
        const string name = "Beutl.Package.UpdateTest.Version";
        const string version = "1.0.0";
        var repo = new InstalledPackageRepository();
        var emissions = new List<bool>();
        repo.GetObservable(name, version).Subscribe(emissions.Add);

        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse(version)));
        repo.RemovePackage(new PackageIdentity(name, NuGetVersion.Parse(version)));

        Assert.That(emissions, Is.EqualTo(new[] { false, true, false }));
    }
}
