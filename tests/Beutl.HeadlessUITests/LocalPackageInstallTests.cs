using System.Net;
using System.Text;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.PackageTools.UI.Models;
using Beutl.PackageTools.UI.ViewModels;
using Beutl.Testing.Headless;
using NuGet.Packaging.Core;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class LocalPackageInstallTests
{
    private readonly List<string> _cleanup = [];
    private AppRootFileBackup _nugetConfig = null!;
    private AppRootFileBackup _installedPackages = null!;

    [SetUp]
    public void SetUp()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        _nugetConfig = new AppRootFileBackup("nuget.config");
        _installedPackages = new AppRootFileBackup("installedPackages.json");
        Directory.CreateDirectory(Helper.LocalSourcePath);
        // Keep dependency resolution entirely within the isolated local source.
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
        _installedPackages.Dispose();
    }

    [AvaloniaTest]
    [TestCase("1.0.0")]
    [TestCase("1.0.0-preview.1")]
    public async Task LocalPackage_NotPublishedToTheStore_IsInstalledFromTheLocalSource(string version)
    {
        PackageIdentity identity = TestPackages.CreateNupkg("Beutl.LocalInstallTest", version, _cleanup);
        using var handler = new MissingStorePackageHandler();
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());

        LocalPackage listed = app.GetResource<PackageManager>().GetLocalSourcePackages()
            .Single(package => package.Name == identity.Id);
        PackageChangeModel? model = await PackageChangeModel.TryParse(
            app, $"{listed.Name}/{listed.Version}", PackageChangeAction.Install, CancellationToken.None);
        Assert.That(model, Is.Not.Null);
        Assert.That(model!.IsRemote, Is.False);
        int requestsBeforeDownload = handler.RequestCount;

        var changes = new ChangesModel();
        changes.InstallItems.Add(model);
        var install = new InstallViewModel(app, changes, model);
        await install.Run(CancellationToken.None);
        Assert.That(install.Succeeded.Value, Is.True,
            install.Download.Value?.ErrorMessage.Value ?? install.Resolve.Value?.ErrorMessage.Value);
        Assert.Multiple(() =>
        {
            Assert.That(install.Download.Value.DownloadSkipped.Value, Is.True);
            Assert.That(app.GetResource<InstalledPackageRepository>().ExistsPackage(identity), Is.True);
            Assert.That(handler.RequestCount, Is.EqualTo(requestsBeforeDownload),
                "A local installation must not require a package or release from the web store.");
        });

        string? installedPath = Helper.PackagePathResolver.GetInstalledPath(identity);
        Assert.That(installedPath, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(installedPath!, "content/payload.txt")), Is.EqualTo("local payload"));
            Assert.That(File.Exists(Helper.GetNupkgFilePath(identity.Id, version)), Is.True);
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task RemotePackage_WithALocalConflict_HonorsSourcePreference(bool preferLocal)
    {
        PackageIdentity identity = TestPackages.CreateNupkg("Beutl.LocalInstallTest", "1.0.0", _cleanup);
        using var handler = new MissingStorePackageHandler();
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        var model = new PackageChangeModel(identity.Id, identity.Version, identity.Id, true, PackageChangeAction.Install)
        {
            Conflict = true
        };
        var download = new DownloadTaskModel(model, app);
        download.SetPreferLocalSource(preferLocal);

        Assert.That(await download.Run(CancellationToken.None), Is.EqualTo(preferLocal));
        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(preferLocal ? 0 : 1));
            Assert.That(download.DownloadSkipped.Value, Is.EqualTo(preferLocal));
            if (!preferLocal)
                Assert.That(download.ErrorMessage.Value, Is.EqualTo("パッケージが見つかりません"));
        });
    }

    private sealed class MissingStorePackageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
                Content = new StringContent(
                    """{"error_code":"packageNotFound","message":"パッケージが見つかりません","documentation_url":null}""",
                    Encoding.UTF8, "application/json")
            });
        }
    }
}
