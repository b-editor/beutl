using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class PackagePublicationFailureTests
{
    [AvaloniaTest]
    [TestCase("data", true)]
    [TestCase("load", true)]
    [TestCase("register-extension", true)]
    [TestCase("data", false)]
    public async Task FailedActivation_RestoresPayloadAndPreviousRegistration(string mode, bool existing)
    {
        await TestReset.ResetShellAsync();
        using var registration = new AppRootFileBackup("installedPackages.json");
        string registrationFile = registration.Path;
        if (!registration.Existed) File.WriteAllText(registrationFile, "[]");
        string backup = registrationFile + ".test-backup";
        string name = "PublicationFailure." + Guid.NewGuid().ToString("N");
        var oldId = new PackageIdentity(name, NuGetVersion.Parse("1.0.0"));
        var newId = new PackageIdentity(name, NuGetVersion.Parse("2.0.0"));
        LocalPackage old = TestPackages.CreateExtracted(oldId, data: true);
        LocalPackage next = TestPackages.CreateExtracted(newId, data: mode == "data", loadable: mode == "register-extension");
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        var installer = app.GetResource<PackageInstaller>();
        var repository = app.GetResource<InstalledPackageRepository>();
        var operation = new PackageOperationHandler(app, new EditorService(new ExtensionProvider()), new ProjectService());
        string materials = Path.Combine(BeutlEnvironment.GetMaterialsDirectoryPath(), name);
        string templates = Path.Combine(BeutlEnvironment.GetTemplatesDirectoryPath(), name);
        bool extensionRegistered = false;
        var manager = app.GetResource<PackageManager>();
        manager.AfterExtensionRegistration = () => extensionRegistered = true;
        try
        {
            if (existing)
            {
                repository.UpgradePackagesAndDeferNotifications(oldId)();
                installer.InstallDataPackage(old);
            }
            byte[]? oldMarker = existing ? File.ReadAllBytes(Path.Combine(materials, ".beutl-package-owner")) : null;
            if (mode != "load")
            {
                File.Move(registrationFile, backup);
                Directory.CreateDirectory(registrationFile);
            }
            Exception? failure = null;
            try { await operation.ActivateInstalledPackageAsync(newId, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception ex) { failure = ex; }
            Assert.That(failure, Is.Not.Null.And.Not.TypeOf<TimeoutException>());
            Assert.Multiple(() =>
            {
                if (existing)
                {
                    Assert.That(File.ReadAllText(Path.Combine(materials, "item.txt")), Is.EqualTo("1.0.0"));
                    Assert.That(File.ReadAllText(Path.Combine(templates, "item.txt")), Is.EqualTo("1.0.0"));
                    Assert.That(File.ReadAllBytes(Path.Combine(materials, ".beutl-package-owner")), Is.EqualTo(oldMarker));
                }
                else
                {
                    Assert.That(Directory.Exists(materials), Is.False);
                    Assert.That(Directory.Exists(templates), Is.False);
                }
                Assert.That(extensionRegistered, Is.EqualTo(mode == "register-extension"));
                Assert.That(manager.FindLoadedPackage(name), Is.Empty);
                Assert.That(repository.ExistsPackage(oldId), Is.EqualTo(existing));
                Assert.That(repository.ExistsPackage(newId), Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(registrationFile)) Directory.Delete(registrationFile);
            if (File.Exists(backup)) File.Move(backup, registrationFile, true);
            foreach (string directory in new[] { old.InstalledPath!, next.InstalledPath!, materials, templates })
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [AvaloniaTest]
    public async Task CommittedPackageObserver_CanUseTheInstallerFromAnotherThread()
    {
        await TestReset.ResetShellAsync();
        using var registration = new AppRootFileBackup("installedPackages.json");
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        string name = "PublicationObserver." + Guid.NewGuid().ToString("N");
        var identity = new PackageIdentity(name, NuGetVersion.Parse("1.0.0"));
        LocalPackage package = TestPackages.CreateExtracted(identity, data: true);
        var installer = app.GetResource<PackageInstaller>();
        var repository = app.GetResource<InstalledPackageRepository>();
        var operation = new PackageOperationHandler(app, new EditorService(new ExtensionProvider()), new ProjectService());
        Task? nested = null;
        bool completedInCallback = false;
        using var subscription = repository.GetPackageObservable(name).Subscribe(value =>
        {
            if (value is null) return;
            nested = Task.Run(() => installer.UninstallDataPackage(name + ".absent"));
            completedInCallback = nested.Wait(TimeSpan.FromSeconds(3));
        });
        try
        {
            await operation.ActivateInstalledPackageAsync(identity, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(completedInCallback, Is.True, "Repository observers must run after releasing the payload gate.");
            Assert.That(repository.ExistsPackage(identity), Is.True);
        }
        finally
        {
            if (nested is not null) await nested;
            installer.UninstallDataPackage(name);
            Directory.Delete(package.InstalledPath!, true);
        }
    }
}
