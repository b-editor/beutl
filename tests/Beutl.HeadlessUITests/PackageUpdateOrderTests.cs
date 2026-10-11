using System.Reflection;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

// An update must not remove the installed version before the new one is in place (#2833).
[TestFixture, NonParallelizable]
public class PackageUpdateOrderTests
{
    private string _name = null!;
    private PackageIdentity _oldId = null!;
    private PackageIdentity _newId = null!;
    private string _registrationFile = null!;
    private byte[]? _originalRegistration;
    private readonly List<string> _directories = [];

    private string Materials => Path.Combine(BeutlEnvironment.GetMaterialsDirectoryPath(), _name);

    private string Templates => Path.Combine(BeutlEnvironment.GetTemplatesDirectoryPath(), _name);

    [SetUp]
    public void SetUp()
    {
        _name = "UpdateOrder." + Guid.NewGuid().ToString("N");
        _oldId = new PackageIdentity(_name, NuGetVersion.Parse("1.0.0"));
        _newId = new PackageIdentity(_name, NuGetVersion.Parse("2.0.0"));
        _registrationFile = Path.Combine(Helper.AppRoot, "installedPackages.json");
        _originalRegistration = File.Exists(_registrationFile) ? File.ReadAllBytes(_registrationFile) : null;
        _directories.AddRange([Materials, Templates]);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string directory in _directories.Where(Directory.Exists))
            Directory.Delete(directory, true);
        _directories.Clear();
        if (_originalRegistration is not null) File.WriteAllBytes(_registrationFile, _originalRegistration);
        else File.Delete(_registrationFile);
    }

    [AvaloniaTest]
    public async Task FailedDownload_KeepsTheInstalledDataPackage()
    {
        await TestReset.ResetShellAsync();
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        var operation = new PackageOperationHandler(app, new EditorService(new ExtensionProvider()), new ProjectService());
        string oldDirectory = InstallOldVersion(app);

        await operation.UpdateOrQueueAsync(_name, _newId,
            _ => throw new HttpRequestException("The download failed."), NullLogger.Instance, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(Materials, "item.txt")), Is.EqualTo("1.0.0"));
            Assert.That(File.ReadAllText(Path.Combine(Templates, "item.txt")), Is.EqualTo("1.0.0"));
            Assert.That(app.GetResource<InstalledPackageRepository>().ExistsPackage(_oldId), Is.True);
            Assert.That(Directory.Exists(oldDirectory), Is.True);
            // The update is still retried by PackageTools.UI at shutdown, as before.
            Assert.That(app.GetResource<PackageChangesQueue>().GetInstalls(), Does.Contain(_newId));
        });
    }

    [AvaloniaTest]
    public async Task CanceledDownload_KeepsTheInstalledDataPackageWithoutQueueing()
    {
        await TestReset.ResetShellAsync();
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        var operation = new PackageOperationHandler(app, new EditorService(new ExtensionProvider()), new ProjectService());
        string oldDirectory = InstallOldVersion(app);
        using var cancellation = new CancellationTokenSource();

        await Assert.CatchAsync<OperationCanceledException>(() => operation.UpdateOrQueueAsync(_name, _newId,
            token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            NullLogger.Instance, cancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(Materials, "item.txt")), Is.EqualTo("1.0.0"));
            Assert.That(app.GetResource<InstalledPackageRepository>().ExistsPackage(_oldId), Is.True);
            Assert.That(Directory.Exists(oldDirectory), Is.True);
            Assert.That(app.GetResource<PackageChangesQueue>().GetInstalls(), Does.Not.Contain(_newId));
        });
    }

    [AvaloniaTest]
    public async Task SuccessfulUpdate_RemovesTheOldVersionAfterTheNewOneIsInPlace()
    {
        await TestReset.ResetShellAsync();
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        var operation = new PackageOperationHandler(app, new EditorService(new ExtensionProvider()), new ProjectService());
        var repository = app.GetResource<InstalledPackageRepository>();
        string oldDirectory = InstallOldVersion(app);
        string newDirectory = CreatePackage(_newId);
        bool oldVersionPresentDuringActivation = false;
        MethodInfo activate = typeof(PackageOperationHandler).GetMethod("ActivateInstalledPackageAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        await operation.UpdateOrQueueAsync(_name, _newId,
            token =>
            {
                oldVersionPresentDuringActivation = Directory.Exists(oldDirectory) && repository.ExistsPackage(_oldId);
                return (Task)activate.Invoke(operation, [_newId, token])!;
            },
            NullLogger.Instance, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(oldVersionPresentDuringActivation, Is.True);
            Assert.That(File.ReadAllText(Path.Combine(Materials, "item.txt")), Is.EqualTo("2.0.0"));
            Assert.That(File.ReadAllText(Path.Combine(Templates, "item.txt")), Is.EqualTo("2.0.0"));
            Assert.That(repository.ExistsPackage(_newId), Is.True);
            Assert.That(repository.ExistsPackage(_oldId), Is.False);
            Assert.That(Directory.Exists(newDirectory), Is.True);
            Assert.That(Directory.Exists(oldDirectory), Is.False);
            Assert.That(app.GetResource<PackageChangesQueue>().GetInstalls(), Does.Not.Contain(_newId));
        });
    }

    // Registers version 1.0.0 and publishes its materials and templates.
    private string InstallOldVersion(BeutlApiApplication app)
    {
        string directory = CreatePackage(_oldId);
        app.GetResource<InstalledPackageRepository>().UpgradePackages(_oldId);
        app.GetResource<PackageInstaller>().InstallDataPackage(
            new LocalPackage { Name = _name, Version = "1.0.0", Tags = [PackageKinds.MaterialTag, PackageKinds.TemplateTag], InstalledPath = directory });
        return directory;
    }

    // An extracted material and template package whose payload files contain its version.
    private string CreatePackage(PackageIdentity identity)
    {
        string version = identity.Version.ToString();
        string directory = Path.Combine(Helper.InstallPath, $"{identity.Id}.{version}");
        _directories.Add(directory);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{identity.Id}.{version}.nupkg"), "");
        File.WriteAllText(Path.Combine(directory, identity.Id + ".nuspec"),
            $"<package><metadata><id>{identity.Id}</id><version>{version}</version><authors>test</authors><description>test</description><tags>{PackageKinds.MaterialTag} {PackageKinds.TemplateTag}</tags></metadata></package>");
        foreach (string kind in new[] { "materials", "templates" })
        {
            Directory.CreateDirectory(Path.Combine(directory, kind));
            File.WriteAllText(Path.Combine(directory, kind, "item.txt"), version);
        }

        return directory;
    }
}
