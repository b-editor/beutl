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
        _repository.UpgradePackages(package);

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
        _repository.UpgradePackages(package);
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
        Track(directory);

        string file = Path.Combine(libDirectory, $"{package.Id}.dll");
        File.WriteAllText(file, "not a real assembly");

        Assert.That(Helper.PackagePathResolver.GetInstalledPath(package), Is.Null);
        return (directory, new FileInfo(file).Length);
    }
}
