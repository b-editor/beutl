using System.IO.Compression;
using System.Net;
using System.Text;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.PackageTools.UI.Models;
using Beutl.PackageTools.UI.ViewModels;
using Beutl.Testing.Headless;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class LocalPackageInstallTests
{
    private readonly List<string> _sourceFiles = [];
    private readonly List<string> _installedDirectories = [];
    private string? _previousNuGetConfig;
    private string? _previousInstalledPackages;

    private static string NuGetConfigPath => Path.Combine(Helper.AppRoot, "nuget.config");
    private static string InstalledPackagesPath => Path.Combine(Helper.AppRoot, "installedPackages.json");

    [SetUp]
    public void SetUp()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        _previousNuGetConfig = File.Exists(NuGetConfigPath) ? File.ReadAllText(NuGetConfigPath) : null;
        _previousInstalledPackages = File.Exists(InstalledPackagesPath) ? File.ReadAllText(InstalledPackagesPath) : null;
        Directory.CreateDirectory(Helper.LocalSourcePath);
        // Keep dependency resolution entirely within the isolated local source.
        File.WriteAllText(NuGetConfigPath, $$"""
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
        foreach (string file in _sourceFiles)
            File.Delete(file);
        foreach (string directory in _installedDirectories.Where(Directory.Exists))
            Directory.Delete(directory, recursive: true);
        _sourceFiles.Clear();
        _installedDirectories.Clear();

        if (_previousNuGetConfig is null)
            File.Delete(NuGetConfigPath);
        else
            File.WriteAllText(NuGetConfigPath, _previousNuGetConfig);

        if (_previousInstalledPackages is null)
            File.Delete(InstalledPackagesPath);
        else
            File.WriteAllText(InstalledPackagesPath, _previousInstalledPackages);
    }

    [AvaloniaTest]
    [TestCase("1.0.0")]
    [TestCase("1.0.0-preview.1")]
    public async Task LocalPackage_NotPublishedToTheStore_IsInstalledFromTheLocalSource(string version)
    {
        PackageIdentity identity = CreateLocalPackage(version);
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
        PackageIdentity identity = CreateLocalPackage("1.0.0");
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

    private PackageIdentity CreateLocalPackage(string version)
    {
        var identity = new PackageIdentity($"Beutl.LocalInstallTest.{Guid.NewGuid():N}", NuGetVersion.Parse(version));
        string sourcePath = Helper.GetNupkgFilePath(identity.Id, version);
        _sourceFiles.Add(sourcePath);
        _installedDirectories.Add(Helper.PackagePathResolver.GetInstallPath(identity));
        using var archive = ZipFile.Open(sourcePath, ZipArchiveMode.Create);
        using (StreamWriter writer = new(archive.CreateEntry($"{identity.Id}.nuspec").Open(), new UTF8Encoding(false)))
        {
            writer.Write($$"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{{identity.Id}}</id>
                    <version>{{version}}</version>
                    <title>Local install test</title>
                    <authors>tester</authors>
                    <description>A package placed directly in packageSource</description>
                  </metadata>
                </package>
                """);
        }
        using (StreamWriter writer = new(archive.CreateEntry("content/payload.txt").Open(), new UTF8Encoding(false)))
        {
            writer.Write("local payload");
        }
        return identity;
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
