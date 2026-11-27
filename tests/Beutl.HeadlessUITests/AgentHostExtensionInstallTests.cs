using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Live;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.Services;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using static Beutl.HeadlessUITests.AgentHostTestHelpers;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AgentHostExtensionInstallTests
{
    private const string ToolName = "test.installed_extension";
    private const string DownloadUrl = "https://packages.example.test/test.nupkg";
    private readonly List<string> _cleanup = [];
    private AppRootFileBackup _config = null!;
    private AppRootFileBackup _registry = null!;
    private AppRootFileBackup _changes = null!;
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = CreateDirectory("beutl-mcp-extensions-");
        _cleanup.Add(_directory);
        _config = new AppRootFileBackup("nuget.config");
        _registry = new AppRootFileBackup("installedPackages.json");
        _changes = new AppRootFileBackup("packageChanges.json");
        File.Delete(_changes.Path);
        Directory.CreateDirectory(Helper.LocalSourcePath);
        File.WriteAllText(_config.Path,
            """<configuration><packageSources><clear /><add key="Beutl Local Packages" value="packageSource" /></packageSources></configuration>""");
    }

    [TearDown]
    public void TearDown()
    {
        TestPackages.Delete(_cleanup);
        _changes.Dispose();
        _registry.Dispose();
        _config.Dispose();
    }

    [AvaloniaTest]
    [TestCase("store")]
    [TestCase("path")]
    [TestCase("url")]
    public async Task Installed_extension_is_listed_and_callable_in_the_running_editor(string source)
    {
        await TestReset.ResetShellAsync();
        PackageIdentity package = NewPackage();
        byte[] archive = CreateToolPackage(package);
        if (source == "path")
        {
            // The configured feed also has this identity, with different bytes and no tool.
            string conflictingPath = Helper.GetNupkgFilePath(package.Id, package.Version.ToString());
            File.WriteAllBytes(conflictingPath, TestPackages.CreateExtensionNupkg(package));
            _cleanup.Add(conflictingPath);
        }
        using var store = new PackageStore(package, archive);
        await using var app = CreateApplication(store, out EditorService editor);
        await using var host = CreateHost(editor, _directory, "extension-install-token");
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);
        var registry = new AgentHostInstanceRegistry(_directory);

        Assert.That((await client.ListToolsAsync()).Select(tool => tool.Name), Does.Contain("install_extension"));
        if (source == "store")
        {
            JsonObject search = Success(await client.CallToolAsync("search_extensions",
                new Dictionary<string, object?> { ["query"] = "test" }));
            Assert.That(search["extensions"]![0]!["packageId"]!.GetValue<string>(), Is.EqualTo(package.Id));
            Assert.That(store.SearchType, Is.EqualTo("extension"));
        }

        var arguments = new Dictionary<string, object?>
        {
            [source == "store" ? "packageId" : source] = source switch
            {
                "store" => package.Id,
                "path" => WriteArchive(archive),
                _ => DownloadUrl
            },
            ["instanceId"] = host.InstanceId
        };
        JsonObject installed = Success(await client.CallToolAsync("install_extension", arguments));
        Assert.That(installed["status"]!.GetValue<string>(), Is.EqualTo("installed"));
        Assert.That(installed["version"]!.GetValue<string>(), Is.EqualTo(package.Version.ToString()));
        Assert.That(registry.Read().Single().ToolsVersion, Is.EqualTo(1));
        Assert.That((await client.ListToolsAsync()).Select(tool => tool.Name), Does.Contain(ToolName));
        CallToolResult result = await client.CallToolAsync(ToolName);
        Assert.That(result.Content.OfType<TextContentBlock>().Single().Text, Is.EqualTo("from the installed extension"));

        JsonObject listed = FindExtension(Success(await client.CallToolAsync("list_installed_extensions")), package.Id);
        Assert.That(listed["loadedVersion"]!.GetValue<string>(), Is.EqualTo(package.Version.ToString()));
        Assert.That(listed["restartRequired"]!.GetValue<bool>(), Is.False);
        JsonObject retried = Success(await client.CallToolAsync("install_extension", arguments));
        Assert.That(retried["status"]!.GetValue<string>(), Is.EqualTo("already_installed"));
        Assert.That(app.GetResource<PackageManager>().FindLoadedPackage(package.Id), Has.Length.EqualTo(1));
        Assert.That(store.Downloads, Is.EqualTo(source == "path" ? 0 : source == "url" ? 2 : 1));
        if (source == "path")
        {
            var replacement = new PackageIdentity(package.Id, NuGetVersion.Parse("2.0.0"));
            AssertError(await client.CallToolAsync("install_extension", new Dictionary<string, object?>
            {
                ["path"] = WriteArchive(TestPackages.CreateExtensionNupkg(replacement))
            }), "extension_version_conflict");
            string otherArchive = Helper.GetNupkgFilePath(package.Id, package.Version.ToString());
            AssertError(await client.CallToolAsync("install_extension", new Dictionary<string, object?>
            {
                ["path"] = otherArchive
            }), "extension_archive_conflict");
        }

        app.GetResource<PackageChangesQueue>().UninstallQueue(package);
        AssertError(await client.CallToolAsync("install_extension", arguments), "extension_change_pending");
        await UnloadAsync(app, package);
    }

    [AvaloniaTest]
    [TestCase("corrupt", "extension_install_failed")]
    [TestCase("hash", "extension_install_failed")]
    [TestCase("api", "extension_incompatible")]
    [TestCase("license", "extension_license_required")]
    [TestCase("unowned", "extension_purchase_required")]
    public async Task Rejected_package_leaves_no_registration_or_restart_work(string problem, string error)
    {
        await TestReset.ResetShellAsync();
        PackageIdentity package = NewPackage();
        byte[] archive = problem switch
        {
            "corrupt" => "not an archive"u8.ToArray(),
            "api" => TestPackages.CreateIncompatibleNupkg(package, _cleanup),
            _ => TestPackages.CreateExtensionNupkg(package, requireLicenseAcceptance: problem == "license")
        };
        using var store = new PackageStore(package, archive)
        {
            CorrectHash = problem != "hash",
            PurchaseRequired = problem == "unowned"
        };
        await using var app = CreateApplication(store, out EditorService editor);
        await using var host = CreateHost(editor, _directory, "extension-install-token");
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);
        Dictionary<string, object?> arguments = problem is "unowned" or "hash"
            ? new() { ["packageId"] = package.Id }
            : new() { ["path"] = WriteArchive(archive) };

        AssertError(await client.CallToolAsync("install_extension", arguments), error);
        Assert.That(new InstalledPackageRepository().ExistsPackage(package), Is.False);
        Assert.That(app.GetResource<PackageManager>().FindLoadedPackage(package.Id), Is.Empty);
        Assert.That(new PackageChangesQueue().GetInstalls(), Does.Not.Contain(package));
        if (problem == "unowned") Assert.That(store.Downloads, Is.Zero);
    }

    [AvaloniaTest]
    public async Task Restricted_mode_persists_the_exact_package_for_the_next_launch()
    {
        await TestReset.ResetShellAsync();
        PackageIdentity package = NewPackage();
        byte[] archive = CreateToolPackage(package);
        string path = WriteArchive(archive);
        using var store = new PackageStore(package, archive);
        await using (var app = CreateApplication(store, out EditorService editor))
        {
            editor.ExtensionStartupRestrictedMode = Task.FromResult(true);
            await using var host = CreateHost(editor, _directory, "extension-install-token");
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);
            JsonObject response = Success(await client.CallToolAsync("install_extension",
                new Dictionary<string, object?> { ["path"] = path }));
            Assert.That(response["status"]!.GetValue<string>(), Is.EqualTo("restart_required"));
            Assert.That((await client.ListToolsAsync()).Select(tool => tool.Name), Does.Not.Contain(ToolName));
            var status = FindExtension(Success(await client.CallToolAsync("list_installed_extensions")), package.Id);
            Assert.That(status["installedVersion"]!.GetValue<string>(), Is.EqualTo(package.Version.ToString()));
            Assert.That(status["restartRequired"]!.GetValue<bool>(), Is.True);
        }
        File.Delete(path);

        await using var reopened = CreateApplication(store, out EditorService reopenedEditor);
        Assert.That(reopened.GetResource<InstalledPackageRepository>().ExistsPackage(package), Is.True);
        reopened.GetResource<PackageManager>().Load(PackageManager.ReadInstalledPackage(package)!);
        await using var reopenedHost = CreateHost(reopenedEditor, _directory, "extension-install-token");
        await reopenedHost.StartAsync();
        await using McpClient reopenedClient = await ConnectAsync(reopenedHost);
        Assert.That((await reopenedClient.ListToolsAsync()).Select(tool => tool.Name), Does.Contain(ToolName));
        await UnloadAsync(reopened, package);
    }

    [AvaloniaTest]
    public async Task Cancellation_during_download_leaves_no_installation()
    {
        await TestReset.ResetShellAsync();
        PackageIdentity package = NewPackage();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var store = new PackageStore(package, TestPackages.CreateExtensionNupkg(package))
        {
            BeforeDownload = async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        };
        await using var app = CreateApplication(store, out EditorService editor);
        var tools = new AgentHostExtensionTools(editor, TestShell.Project);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var call = tools.InstallExtension(url: DownloadUrl, cancellationToken: cancel.Token);
        await started.Task.WaitAsync(cancel.Token);
        cancel.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(async () => await call);
        Assert.That(new InstalledPackageRepository().ExistsPackage(package), Is.False);
        Assert.That(Directory.Exists(Helper.PackagePathResolver.GetInstallPath(package)), Is.False);
    }

    [AvaloniaTest]
    public async Task Concurrent_installs_survive_a_cleanup_prepared_before_registration()
    {
        await TestReset.ResetShellAsync();
        PackageIdentity first = NewPackage();
        PackageIdentity second = NewPackage();
        PackageIdentity anchor = NewPackage();
        string firstPath = WriteArchive(TestPackages.CreateExtensionNupkg(first));
        byte[] archive = TestPackages.CreateExtensionNupkg(second);
        string secondPath = WriteArchive(archive);
        string cached = TestPackages.CreateExtracted(second, data: false, loadable: true).InstalledPath!;
        File.WriteAllBytes(Path.Combine(cached, $"{second.Id}.{second.Version}.nupkg"), archive);
        TestPackages.CreateExtracted(anchor, data: false);
        new InstalledPackageRepository().UpgradePackagesAndDeferNotifications(anchor)();

        using var store = new PackageStore(first, []);
        await using var firstApp = CreateApplication(store, out EditorService firstEditor);
        await using var secondApp = CreateApplication(store, out EditorService secondEditor);
        var cleaner = firstApp.GetResource<PackageInstaller>();
        var cleanup = cleaner.PrepareForClean();
        Assert.That(cleanup.UnnecessaryPackages, Does.Contain(second));
        Assert.That(firstApp.GetResource<InstalledPackageRepository>().ExistsPackage(second), Is.False);
        Assert.That(secondApp.GetResource<InstalledPackageRepository>().ExistsPackage(first), Is.False);
        var firstTools = new AgentHostExtensionTools(firstEditor, TestShell.Project);
        var secondTools = new AgentHostExtensionTools(secondEditor, TestShell.Project);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var results = await Task.WhenAll(
            firstTools.InstallExtension(path: firstPath, cancellationToken: deadline.Token),
            secondTools.InstallExtension(path: secondPath, cancellationToken: deadline.Token));

        Assert.That(results.Select(result => result.Value?.Status), Is.EqualTo(new[] { "installed", "installed" }),
            () => JsonSerializer.Serialize(results));
        cleaner.Clean(cleanup, new Progress<double>());
        Assert.That(new InstalledPackageRepository().GetLocalPackages(), Does.Contain(first).And.Contain(second));
        Assert.That(Directory.Exists(cached), Is.True);
        var firstList = await firstTools.ListInstalledExtensions();
        var secondInFirst = firstList.Value!.Extensions.Single(item => item.PackageId == second.Id);
        Assert.That(secondInFirst.InstalledVersion, Is.EqualTo(second.Version.ToString()));
        Assert.That(secondInFirst.LoadedVersion, Is.Null);
        Assert.That(secondInFirst.RestartRequired, Is.True);
    }

    [AvaloniaTest]
    public async Task Pending_data_package_is_not_listed_as_an_extension()
    {
        await TestReset.ResetShellAsync();
        PackageIdentity data = NewPackage();
        PackageIdentity extension = NewPackage();
        TestPackages.CreateExtracted(data, data: true);
        TestPackages.CreateExtracted(extension, data: false, loadable: true);
        using var store = new PackageStore(data, []);
        await using var app = CreateApplication(store, out EditorService editor);
        PackageChangesQueue queue = app.GetResource<PackageChangesQueue>();
        queue.InstallQueue(data);
        queue.InstallQueue(extension);

        var tools = new AgentHostExtensionTools(editor, TestShell.Project);
        var response = await tools.ListInstalledExtensions();

        string[] ids = [.. response.Value!.Extensions.Select(item => item.PackageId)];
        Assert.That(ids, Does.Contain(extension.Id));
        Assert.That(ids, Does.Not.Contain(data.Id));
        Assert.That(response.Value.Extensions.Single(item => item.PackageId == extension.Id).PendingInstallVersion,
            Is.EqualTo(extension.Version.ToString()));
    }

    [AvaloniaTest]
    public async Task Cleanup_waits_for_an_active_package_file_change()
    {
        await TestReset.ResetShellAsync();
        PackageIdentity anchor = NewPackage();
        TestPackages.CreateExtracted(anchor, data: false);
        new InstalledPackageRepository().UpgradePackagesAndDeferNotifications(anchor)();
        using var store = new PackageStore(anchor, []);
        await using var app = CreateApplication(store, out _);
        var installer = app.GetResource<PackageInstaller>();
        PackageCleanContext context = installer.PrepareForClean();
        string lockPath = Path.Combine(Helper.AppRoot, ".package-file-changes.lock");

        Task clean;
        using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            clean = Task.Run(() => installer.Clean(context, new Progress<double>()));
            await Task.Delay(200);
            Assert.That(clean.IsCompleted, Is.False);
        }

        await clean.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [AvaloniaTest]
    public async Task Insecure_redirect_of_a_url_archive_is_rejected()
    {
        await TestReset.ResetShellAsync();
        PackageIdentity package = NewPackage();
        using var store = new PackageStore(package, TestPackages.CreateExtensionNupkg(package))
        {
            RedirectToInsecureUrl = true
        };
        await using var app = CreateApplication(store, out EditorService editor);
        await using var host = CreateHost(editor, _directory, "extension-install-token");
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);

        AssertError(await client.CallToolAsync("install_extension",
            new Dictionary<string, object?> { ["url"] = DownloadUrl }), "extension_install_failed");
        Assert.That(new InstalledPackageRepository().ExistsPackage(package), Is.False);
        Assert.That(Directory.Exists(Helper.PackagePathResolver.GetInstallPath(package)), Is.False);
        Assert.That(store.Downloads, Is.EqualTo(1));
    }

    private static JsonObject FindExtension(JsonObject response, string packageId)
        => response["extensions"]!.AsArray().OfType<JsonObject>().Single(item => item["packageId"]!.GetValue<string>() == packageId);

    private static async Task UnloadAsync(BeutlApiApplication app, PackageIdentity package)
    {
        var manager = app.GetResource<PackageManager>();
        await manager.Unload(manager.FindLoadedPackage(package.Id).Single());
    }

    private static BeutlApiApplication CreateApplication(PackageStore store, out EditorService editor)
    {
        var provider = new ExtensionProvider(TestShell.Configuration.ExtensionConfig);
        var app = new BeutlApiApplication(new HttpClient(store, disposeHandler: false), provider,
            TestShell.Configuration.ViewConfig, TestShell.Configuration.UpdateConfig, () => new HttpClient(store, disposeHandler: false));
        editor = new EditorService(provider, TestShell.MainViewModel.AppServices) { ApiApplication = app };
        return app;
    }

    private PackageIdentity NewPackage()
    {
        var package = new PackageIdentity($"Agent.Install.{Guid.NewGuid():N}", NuGetVersion.Parse("1.0.0"));
        _cleanup.Add(Helper.PackagePathResolver.GetInstallPath(package));
        return package;
    }

    private string WriteArchive(byte[] archive)
    {
        string path = Path.Combine(_directory, $"{Guid.NewGuid():N}.nupkg");
        File.WriteAllBytes(path, archive);
        return path;
    }

    private byte[] CreateToolPackage(PackageIdentity package)
    {
        string runtime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        string assembly = TestPackages.Compile(_directory, "InstalledMcpExtension", $$"""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Beutl.Extensibility;

            [Export]
            public sealed class InstalledExtension : McpToolExtension
            {
                public override IReadOnlyList<McpToolDefinition> Tools { get; } =
                    new McpToolDefinition[] { new("{{ToolName}}", "Runs from an installed extension.") };

                public override ValueTask<McpToolResult> InvokeAsync(McpToolCall call, CancellationToken cancellationToken)
                    => new(McpToolResult.Text("from the installed extension"));
            }
            """, typeof(McpToolExtension).Assembly.Location, typeof(JsonElement).Assembly.Location, runtime);
        return TestPackages.CreateExtensionNupkg(package, assembly);
    }

    private sealed class PackageStore(PackageIdentity package, byte[] archive) : HttpMessageHandler
    {
        public Func<CancellationToken, Task>? BeforeDownload { get; init; }
        public bool CorrectHash { get; init; } = true;
        public bool PurchaseRequired { get; init; }
        public bool RedirectToInsecureUrl { get; init; }
        public int Downloads { get; private set; }
        public string? SearchType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            string path = uri.AbsolutePath;
            var release = TestStore.CreateReleaseResponse(package.Version.ToString(), fileId: "archive");
            HttpContent content;
            if (uri.AbsoluteUri == DownloadUrl)
            {
                Downloads++;
                if (BeforeDownload is not null) await BeforeDownload(cancellationToken);
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
                if (RedirectToInsecureUrl)
                    response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://packages.example.test/test.nupkg");
                return response;
            }
            else if (path == "/api/v3/discover/search")
            {
                SearchType = System.Web.HttpUtility.ParseQueryString(uri.Query)["type"];
                content = JsonContent.Create(new[] { TestStore.CreatePackageResponse(package.Id) });
            }
            else if (path == "/api/v3/files/archive")
            {
                content = JsonContent.Create(new
                {
                    id = "archive",
                    name = "test.nupkg",
                    contentType = "application/octet-stream",
                    downloadUrl = DownloadUrl,
                    size = archive.Length,
                    sha256 = CorrectHash ? Convert.ToHexString(SHA256.HashData(archive)) : new string('0', 64)
                });
            }
            else if (path.EndsWith("/releases", StringComparison.Ordinal))
            {
                content = JsonContent.Create(new[] { TestStore.CreateReleaseResponse("2.0.0", fileId: "future", targetVersion: "[999.0.0,)"), release });
            }
            else if (path.EndsWith($"/releases/{release.Version}", StringComparison.Ordinal))
            {
                content = JsonContent.Create(release);
            }
            else if (path == $"/api/v3/packages/{package.Id}")
            {
                content = JsonContent.Create(TestStore.CreatePackageResponse(package.Id, paid: PurchaseRequired, owned: !PurchaseRequired));
            }
            else
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
