using System.Text.Json;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using NuGet.Packaging.Core;

namespace Beutl.UnitTests.Api;

// An installer over a fresh installedPackages.json. Directories passed to Track are deleted after each test.
public abstract class PackageInstallerFixture
{
    private static string InstalledPackagesFile => Path.Combine(Helper.AppRoot, "installedPackages.json");

    private readonly List<string> _createdDirectories = [];
    private HttpClient _httpClient = null!;

    protected InstalledPackageRepository _repository = null!;
    protected PackageInstaller _installer = null!;

    [SetUp]
    public void SetUpInstaller()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        File.Delete(InstalledPackagesFile);
        _httpClient = new HttpClient();
        _repository = new InstalledPackageRepository();
        _installer = new PackageInstaller(_httpClient, ownsHttpClient: false, _repository, apiApplication: null!);
    }

    [TearDown]
    public void TearDownInstaller()
    {
        _httpClient.Dispose();
        foreach (string directory in _createdDirectories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }

        _createdDirectories.Clear();
        File.Delete(InstalledPackagesFile);
    }

    // Registers the packages the way an installedPackages.json from an earlier Beutl does, which can hold
    // several versions of one package. Only restoring the file does that, so the repository and the
    // installer are created again over it.
    protected void RestoreRegistrations(params PackageIdentity[] packages)
    {
        File.WriteAllText(
            InstalledPackagesFile,
            JsonSerializer.Serialize(packages.Select(package => new
            {
                Name = package.Id,
                Version = package.Version.ToString(),
                BeutlVersion = BeutlApplication.Version,
            })));
        _repository = new InstalledPackageRepository();
        _installer = new PackageInstaller(_httpClient, ownsHttpClient: false, _repository, apiApplication: null!);
    }

    // Removes the package through Clean or Uninstall and returns the directories that could not be removed.
    protected IReadOnlyList<string> RemovePackage(
        bool clean,
        PackageIdentity package,
        string installedPath,
        long sizeToBeReleased)
    {
        if (clean)
        {
            var cleanContext = new PackageCleanContext([package], sizeToBeReleased);
            _installer.Clean(cleanContext, new Progress<double>());
            return cleanContext.FailedPackages;
        }

        var uninstallContext = new PackageUninstallContext(package, installedPath)
        {
            UnnecessaryPackages = [package],
            SizeToBeReleased = sizeToBeReleased,
        };
        _installer.Uninstall(uninstallContext, new Progress<double>());
        return uninstallContext.FailedPackages;
    }

    protected void Track(string directory)
    {
        if (!_createdDirectories.Contains(directory))
        {
            _createdDirectories.Add(directory);
        }
    }
}
