using Beutl.Api.Services;
using Beutl.Testing.Headless;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public class PackageInstallerCleanupTests
{
    private static string InstalledPackagesFile => Path.Combine(Helper.AppRoot, "installedPackages.json");

    private HttpClient _httpClient = null!;
    private InstalledPackageRepository _repository = null!;
    private PackageInstaller _installer = null!;
    private readonly List<string> _createdDirectories = [];

    [SetUp]
    public void SetUp()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        File.Delete(InstalledPackagesFile);
        _httpClient = new HttpClient();
        _repository = new InstalledPackageRepository();
        _installer = new PackageInstaller(_httpClient, _repository, apiApplication: null!);
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient.Dispose();
        foreach (string directory in _createdDirectories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }

        _createdDirectories.Clear();
        File.Delete(InstalledPackagesFile);
    }

    [Test]
    public void Clean_DeletesTheExtractedFiles_WhenTheNupkgIsMissing()
    {
        var package = new PackageIdentity("Beutl.Package.CleanTest.NoNupkg", NuGetVersion.Parse("1.0.0"));
        (string directory, long size) = CreateInstalledDirectory(package);
        _repository.UpgradePackages(package);

        _installer.Clean(new PackageCleanContext([package], size), new Progress<double>());

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(directory), Is.False, "the extracted files must be deleted");
            Assert.That(_repository.ExistsPackage(package), Is.False);
        });
    }

    [Test]
    public void Clean_DropsTheRepositoryEntry_WhenTheInstallDirectoryIsGone()
    {
        var package = new PackageIdentity("Beutl.Package.CleanTest.Missing", NuGetVersion.Parse("1.0.0"));
        _repository.UpgradePackages(package);

        Assert.DoesNotThrow(() =>
            _installer.Clean(new PackageCleanContext([package], 0), new Progress<double>()));

        Assert.That(_repository.ExistsPackage(package), Is.False);
    }

    [Test]
    public void Uninstall_DeletesTheExtractedFiles_WhenTheNupkgIsMissing()
    {
        var package = new PackageIdentity("Beutl.Package.UninstallTest.NoNupkg", NuGetVersion.Parse("1.0.0"));
        (string directory, long size) = CreateInstalledDirectory(package);
        _repository.UpgradePackages(package);

        _installer.Uninstall(
            new PackageUninstallContext(package, directory)
            {
                UnnecessaryPackages = [package],
                SizeToBeReleased = size
            },
            new Progress<double>());

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(directory), Is.False, "the extracted files must be deleted");
            Assert.That(_repository.ExistsPackage(package), Is.False);
        });
    }

    [Test]
    public void Uninstall_DropsTheRepositoryEntry_WhenTheInstallDirectoryIsGone()
    {
        var package = new PackageIdentity("Beutl.Package.UninstallTest.Missing", NuGetVersion.Parse("1.0.0"));
        _repository.UpgradePackages(package);
        string missing = Helper.ResolveInstalledDirectory(package);

        Assert.DoesNotThrow(() =>
            _installer.Uninstall(
                new PackageUninstallContext(package, missing) { UnnecessaryPackages = [package] },
                new Progress<double>()));

        Assert.That(_repository.ExistsPackage(package), Is.False);
    }

    // Both the dependency scan (UnnecessaryPackages / Helper.GetPackageDependencies) and the deletion
    // pass go through this, so the two agree on which directories exist. The scan itself cannot be
    // unit-tested: it reaches CoreLibraries, which loads Beutl.dll from AppContext.BaseDirectory, and
    // this assembly must not reference the app project.
    [Test]
    public void ResolveInstalledDirectory_FallsBackToTheInstallPath_WhenTheNupkgIsMissing()
    {
        var package = new PackageIdentity("Beutl.Package.ResolveTest.NoNupkg", NuGetVersion.Parse("1.0.0"));
        (string directory, _) = CreateInstalledDirectory(package);

        Assert.That(Helper.ResolveInstalledDirectory(package), Is.EqualTo(directory));
    }

    [Test]
    public void PackageUninstallContext_ResolvesTheDirectory_WhenTheNupkgIsMissing()
    {
        var package = new PackageIdentity("Beutl.Package.ContextTest.NoNupkg", NuGetVersion.Parse("1.0.0"));
        (string directory, _) = CreateInstalledDirectory(package);

        var context = new PackageUninstallContext(package);

        Assert.That(context.InstalledPath, Is.EqualTo(directory));
    }

    [Test]
    public void PackageUninstallContext_Throws_WhenThePackageIsNotInstalled()
    {
        var package = new PackageIdentity("Beutl.Package.UninstallTest.Unknown", NuGetVersion.Parse("1.0.0"));

        Assert.Throws<ArgumentException>(() => new PackageUninstallContext(package));
    }

    // No .nupkg: PackagePathResolver.GetInstalledPath resolves such a directory to null even though
    // its files are still on disk, which is the case cleanup must not skip.
    private (string Directory, long Size) CreateInstalledDirectory(PackageIdentity package)
    {
        string directory = Path.Combine(Helper.InstallPath, $"{package.Id}.{package.Version}");
        string libDirectory = Path.Combine(directory, "lib", "net10.0");
        Directory.CreateDirectory(libDirectory);
        _createdDirectories.Add(directory);

        string file = Path.Combine(libDirectory, $"{package.Id}.dll");
        File.WriteAllText(file, "not a real assembly");

        Assert.That(Helper.PackagePathResolver.GetInstalledPath(package), Is.Null);
        return (directory, new FileInfo(file).Length);
    }
}
