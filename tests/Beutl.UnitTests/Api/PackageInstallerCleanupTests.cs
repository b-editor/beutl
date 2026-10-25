using Beutl.Api.Services;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public class PackageInstallerCleanupTests : PackageInstallerFixture
{
    [TestCase(false)]
    [TestCase(true)]
    public void Removal_DeletesTheExtractedFiles_WhenTheNupkgIsMissing(bool clean)
    {
        var package = new PackageIdentity("Beutl.Package.CleanTest.NoNupkg", NuGetVersion.Parse("1.0.0"));
        (string directory, long size) = CreateInstalledDirectory(package);
        _repository.UpgradePackagesAndDeferNotifications(package)();

        RemovePackage(clean, package, directory, size);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(directory), Is.False, "the extracted files must be deleted");
            Assert.That(_repository.ExistsPackage(package), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Removal_DropsTheRepositoryEntry_WhenTheInstallDirectoryIsGone(bool clean)
    {
        var package = new PackageIdentity("Beutl.Package.CleanTest.Missing", NuGetVersion.Parse("1.0.0"));
        _repository.UpgradePackagesAndDeferNotifications(package)();
        string missing = Helper.ResolveInstalledDirectory(package);

        Assert.DoesNotThrow(() => RemovePackage(clean, package, missing, 0));

        Assert.That(_repository.ExistsPackage(package), Is.False);
    }

    // Both the dependency scan and the deletion use this. The scan loads Beutl.dll, so it is not
    // unit-tested here.
    [Test]
    public void ResolveInstalledDirectory_FallsBackToTheInstallPath_WhenTheNupkgIsMissing()
    {
        var package = new PackageIdentity("Beutl.Package.ResolveTest.NoNupkg", NuGetVersion.Parse("1.0.0"));
        (string directory, _) = CreateInstalledDirectory(package);

        Assert.That(Helper.ResolveInstalledDirectory(package), Is.EqualTo(directory));
    }

    [Test]
    public void PrepareForClean_UnreadableRegistry_PreservesEveryExtractedPackage()
    {
        // Without this guard an unreadable installedPackages.json makes every extracted
        // package look unnecessary, and clean also deletes its materials/templates payload.
        var package = new PackageIdentity("Beutl.Package.CleanTest.UnreadableRegistry", NuGetVersion.Parse("1.0.0"));
        (string directory, _) = CreateInstalledDirectory(package);
        File.WriteAllText(
            Path.Combine(directory, package.Id + ".nuspec"),
            $"<package><metadata><id>{package.Id}</id><version>{package.Version}</version><authors>tests</authors><description>tests</description></metadata></package>");
        File.WriteAllText(Path.Combine(Helper.AppRoot, "installedPackages.json"), "{ this is not a package array");
        using var client = new HttpClient();
        _repository = new InstalledPackageRepository();
        _installer = new PackageInstaller(client, ownsHttpClient: false, _repository, apiApplication: null!);

        PackageCleanContext context = _installer.PrepareForClean();

        Assert.Multiple(() =>
        {
            Assert.That(_repository.RestoreFailed, Is.True);
            Assert.That(context.RegistryRestoreFailed, Is.True);
            Assert.That(context.UnnecessaryPackages, Is.Empty);
            Assert.That(Directory.Exists(directory), Is.True);
        });
    }

    // No .nupkg: PackagePathResolver.GetInstalledPath resolves such a directory to null even though
    // its files are still on disk, which is the case cleanup must not skip.
    private (string Directory, long Size) CreateInstalledDirectory(PackageIdentity package)
    {
        string directory = Path.Combine(Helper.InstallPath, $"{package.Id}.{package.Version}");
        string libDirectory = Path.Combine(directory, "lib", "net10.0");
        Directory.CreateDirectory(libDirectory);
        Track(directory);

        string file = Path.Combine(libDirectory, $"{package.Id}.dll");
        File.WriteAllText(file, "not a real assembly");

        Assert.That(Helper.PackagePathResolver.GetInstalledPath(package), Is.Null);
        return (directory, new FileInfo(file).Length);
    }
}
