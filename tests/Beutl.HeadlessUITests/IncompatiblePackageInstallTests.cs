using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.ExtensionsPages;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

// A downloaded version that uses an API this Beutl lacks is removed again and not retried at the next launch (#2877).
[TestFixture, NonParallelizable]
public sealed class IncompatiblePackageInstallTests
{
    private readonly List<string> _cleanup = [];
    private AppRootFileBackup _nugetConfig = null!;
    private AppRootFileBackup _registration = null!;
    private AppRootFileBackup _checks = null!;
    private string _name = null!;

    [SetUp]
    public void SetUp()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        _name = "Incompatible." + Guid.NewGuid().ToString("N");
        _nugetConfig = new AppRootFileBackup("nuget.config");
        _registration = new AppRootFileBackup("installedPackages.json");
        _checks = new AppRootFileBackup("packageApiChecks.json");
        Directory.CreateDirectory(Helper.LocalSourcePath);
        File.WriteAllText(_nugetConfig.Path, $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="Local" value="{{Helper.LocalSourcePath}}" />
              </packageSources>
            </configuration>
            """);
    }

    [TearDown]
    public void TearDown()
    {
        TestPackages.Delete(_cleanup);
        _nugetConfig.Dispose();
        _registration.Dispose();
        _checks.Dispose();
    }

    [AvaloniaTest]
    public async Task An_install_is_removed_with_its_download_and_named_in_a_notice()
    {
        await TestReset.ResetShellAsync();
        var identity = new PackageIdentity(_name, NuGetVersion.Parse("1.0.0"));
        using var store = new ServedPackage(TestPackages.CreateIncompatibleNupkg(identity, _cleanup));
        using var http = new HttpClient(store, disposeHandler: false);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider(), () => new HttpClient(store, disposeHandler: false));
        var operation = new PackageOperationHandler(app, new EditorService(new ExtensionProvider()), new ProjectService());
        using var notifications = new NotificationCapture();

        await operation.InstallOrQueueAsync(
            TestStore.CreateRelease(app, "1.0.0", fileId: "archive", name: _name), identity, NullLogger.Instance, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(notifications.Single().Title, Is.EqualTo(string.Format(ExtensionsStrings.PackageNotInstalled, _name)));
            Assert.That(app.GetResource<InstalledPackageRepository>().ExistsPackage(identity), Is.False);
            Assert.That(Directory.Exists(Helper.PackagePathResolver.GetInstallPath(identity)), Is.False);
            Assert.That(File.Exists(Helper.GetNupkgFilePath(identity.Id, "1.0.0")), Is.False);
            Assert.That(app.GetResource<PackageChangesQueue>().GetInstalls(), Does.Not.Contain(identity));
        });
    }

    [AvaloniaTest]
    public async Task An_update_is_removed_and_the_old_version_is_loaded_again()
    {
        await TestReset.ResetShellAsync();
        var oldId = new PackageIdentity(_name, NuGetVersion.Parse("1.0.0"));
        var newId = new PackageIdentity(_name, NuGetVersion.Parse("2.0.0"));
        using var store = new ServedPackage(TestPackages.CreateIncompatibleNupkg(newId, _cleanup));
        using var http = new HttpClient(store, disposeHandler: false);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider(), () => new HttpClient(store, disposeHandler: false));
        var operation = new PackageOperationHandler(app, new EditorService(new ExtensionProvider()), new ProjectService());
        var manager = app.GetResource<PackageManager>();
        var repository = app.GetResource<InstalledPackageRepository>();
        LocalPackage old = TestPackages.CreateExtracted(oldId, data: false, loadable: true, cleanup: _cleanup);
        repository.UpgradePackagesAndDeferNotifications(oldId)();
        manager.Load(old);
        var release = TestStore.CreateRelease(app, "2.0.0", fileId: "archive", name: _name);
        using var notifications = new NotificationCapture();

        await operation.UpdateOrQueueAsync(
            _name, newId, token => operation.DownloadAndLoadPackage(release, newId, token), NullLogger.Instance, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(notifications.Single().Title, Is.EqualTo(string.Format(ExtensionsStrings.PackageNotUpdated, _name)));
            Assert.That(manager.FindLoadedPackage(_name).Select(package => package.Version), Is.EqualTo(new[] { "1.0.0" }));
            Assert.That(repository.ExistsPackage(oldId), Is.True);
            Assert.That(Directory.Exists(old.InstalledPath), Is.True);
            Assert.That(repository.ExistsPackage(newId), Is.False);
            Assert.That(Directory.Exists(Helper.PackagePathResolver.GetInstallPath(newId)), Is.False);
            Assert.That(app.GetResource<PackageChangesQueue>().GetInstalls(), Does.Not.Contain(newId));
        });
    }

    // Answers the asset request with the package's hash, and the download with the package.
    private sealed class ServedPackage(byte[] nupkg) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith(".nupkg", StringComparison.Ordinal)
                    ? new ByteArrayContent(nupkg)
                    : new StringContent(
                        $$"""{"id":"archive","name":"package.nupkg","contentType":"application/octet-stream","downloadUrl":"https://example.com/archive.nupkg","size":{{nupkg.Length}},"sha256":"{{Convert.ToHexString(SHA256.HashData(nupkg))}}"}""",
                        Encoding.UTF8, "application/json"),
            });
    }
}
