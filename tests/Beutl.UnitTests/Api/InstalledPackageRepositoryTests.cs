using Beutl.Api.Services;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public class InstalledPackageRepositoryTests
{
    private string? _previousHome;
    private string? _tempHome;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _previousHome = Environment.GetEnvironmentVariable("BEUTL_HOME");
        _tempHome = Path.Combine(Path.GetTempPath(), $"beutl-repo-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempHome);
        Environment.SetEnvironmentVariable("BEUTL_HOME", _tempHome);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        Environment.SetEnvironmentVariable("BEUTL_HOME", _previousHome);
        try
        {
            if (_tempHome is not null && Directory.Exists(_tempHome))
            {
                Directory.Delete(_tempHome, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // Helper.AppRoot is pinned by its static ctor at first use; skip I/O tests when isolation
    // took effect too late, so they never write to the developer's real ~/.beutl.
    private bool IsHomeIsolated => _tempHome is not null
        && Helper.AppRoot.StartsWith(_tempHome, StringComparison.OrdinalIgnoreCase);

    [Test]
    public void IsNewerThanInstalled_NullInstalled_ReturnsTrue()
    {
        Assert.That(InstalledPackageRepository.IsNewerThanInstalled("1.0.0", null), Is.True);
    }

    [TestCase("2.0.0", "1.0.0", true)]
    [TestCase("1.0.0", "1.0.0", false)]
    [TestCase("0.9.0", "1.0.0", false)]
    public void IsNewerThanInstalled_ComparesVersions(string release, string installed, bool expected)
    {
        var installedId = new PackageIdentity("P", NuGetVersion.Parse(installed));
        Assert.That(InstalledPackageRepository.IsNewerThanInstalled(release, installedId), Is.EqualTo(expected));
    }

    [Test]
    public void GetPackageObservable_EmitsNull_WhenNotInstalled()
    {
        if (!IsHomeIsolated)
        {
            Assert.Ignore("BEUTL_HOME isolation took effect after Helper.AppRoot was pinned; skipping I/O test.");
        }

        const string name = "Beutl.Package.UpdateTest.None";
        var repo = new InstalledPackageRepository();

        PackageIdentity? emitted = new(name, NuGetVersion.Parse("0.0.0"));
        repo.GetPackageObservable(name).Subscribe(x => emitted = x);

        Assert.That(emitted, Is.Null);
    }

    [Test]
    public void GetPackageObservable_EmitsInstalledIdentity_AfterUpgrade()
    {
        if (!IsHomeIsolated)
        {
            Assert.Ignore("BEUTL_HOME isolation took effect after Helper.AppRoot was pinned; skipping I/O test.");
        }

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
        if (!IsHomeIsolated)
        {
            Assert.Ignore("BEUTL_HOME isolation took effect after Helper.AppRoot was pinned; skipping I/O test.");
        }

        const string name = "Beutl.Package.UpdateTest.Flash";
        var repo = new InstalledPackageRepository();
        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse("1.0.0")));

        var emissions = new List<bool>();
        repo.GetObservable(name).Subscribe(x => emissions.Add(x));

        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse("2.0.0")));

        Assert.That(emissions, Does.Not.Contains(false));
    }
}
