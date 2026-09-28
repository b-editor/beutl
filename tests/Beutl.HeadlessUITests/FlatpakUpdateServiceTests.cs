using System.Diagnostics;
using System.Net;
using System.Text;
using Beutl.Api.Clients;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class FlatpakUpdateServiceTests
{
    [OneTimeSetUp]
    public void RequireUnixPaths()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Flatpak deployment paths use Unix filesystem semantics.");
    }

    private const string AppRef = "net.beditor.Beutl/x86_64/master";
    private const string BundleUrl = "https://downloads.example.test/assets/registered-build?token=opaque";
    private static readonly byte[] s_bundle = Encoding.UTF8.GetBytes("test flatpak bundle");

    internal static AppUpdateResponse Update => new()
    {
        LatestVersion = "2.0.0-preview.8",
        Url = "https://github.com/b-editor/beutl/releases/tag/v2.0.0-preview.8",
        DownloadUrl = BundleUrl,
        IsLatest = false,
        MustLatest = false
    };

    [TestCase("user", "--user")]
    [TestCase("system", "--system")]
    [TestCase("system (external)", "--installation=external")]
    public async Task InstallsBundleFromTheRegisteredUrlIntoTheRunningInstallation(string name, string option)
    {
        using var scope = new Scope(name);
        await scope.InstallAsync();

        Assert.Multiple(() =>
        {
            Assert.That(scope.InstalledBytes, Is.EqualTo(s_bundle));
            Assert.That(scope.Requests, Is.EqualTo(new[] { BundleUrl }),
                "The updater must use the update API's URL without querying GitHub or constructing an asset name.");
            Assert.That(scope.Commands[^1].ArgumentList, Is.EqualTo(new[]
            {
                "--host", "--watch-bus", "--directory=/", "--env=LC_ALL=C", "flatpak", "install",
                option, "--bundle", "--or-update", "--noninteractive", "--assumeyes", scope.DownloadPath
            }));
            Assert.That(scope.DownloadPath, Does.StartWith(Path.Combine(scope.Root, "cache", "beutl-updates")));
            Assert.That(File.Exists(scope.DownloadPath), Is.False);
            Assert.That(scope.Progress.Values.Any(x => x.Fraction == 1), Is.True);
            Assert.That(scope.Commands.All(x => !x.UseShellExecute), Is.True);
        });
    }

    [Test]
    public async Task DuplicateUserAndSystemInstalls_SelectsTheRunningCopyEvenWhenItHasBeenUpdated()
    {
        using var scope = new Scope("system");
        scope.List = $"{AppRef}\tuser\n{AppRef}\tsystem\n";
        scope.UserLocation = "/home/example/.local/share/flatpak/app/" + AppRef + "/other-commit";
        await scope.InstallAsync();
        Assert.That(scope.Commands[^1].ArgumentList, Does.Contain("--system"));
        Assert.That(scope.Commands.Count(x => x.ArgumentList.Contains("--show-location")), Is.EqualTo(2));
    }

    [Test]
    public void UnknownInstallation_DoesNotDownloadOrInstall()
    {
        using var scope = new Scope();
        scope.UserLocation = "/different/installation/app/" + AppRef + "/commit";
        Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.InstallAsync());
        Assert.That(scope.Requests, Is.Empty);
        Assert.That(scope.InstalledBytes, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("relative.flatpak")]
    [TestCase("http://example.test/update.flatpak")]
    [TestCase("file:///tmp/update.flatpak")]
    public void MissingOrInvalidRegisteredUrl_DoesNotGuessAnAlternative(string? url)
    {
        using var scope = new Scope();
        var update = Update;
        scope.UpdateResponse = new AppUpdateResponse
        {
            LatestVersion = update.LatestVersion,
            Url = update.Url,
            DownloadUrl = url,
            IsLatest = false,
            MustLatest = false
        };
        Assert.That(async () => await scope.InstallAsync(), Throws.Exception);
        Assert.That(scope.Requests, Is.Empty);
        Assert.That(scope.Commands, Is.Empty);
        Assert.That(scope.InstalledBytes, Is.Null);
    }

    [TestCase("empty")]
    [TestCase("truncated")]
    [TestCase("oversized")]
    [TestCase("http")]
    public void InvalidDownload_IsNotInstalledAndIsCleanedUp(string failure)
    {
        using var scope = new Scope();
        scope.DownloadFailure = failure;
        Assert.That(async () => await scope.InstallAsync(), Throws.Exception);
        Assert.That(scope.InstalledBytes, Is.Null);
        Assert.That(Directory.GetFiles(scope.Root, "*.flatpak", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task RegisteredUrlWithoutContentLength_UsesIndeterminateProgressAndInstalls()
    {
        using var scope = new Scope { UnknownContentLength = true };
        await scope.InstallAsync();
        Assert.Multiple(() =>
        {
            Assert.That(scope.InstalledBytes, Is.EqualTo(s_bundle));
            Assert.That(scope.Requests, Is.EqualTo(new[] { BundleUrl }));
            Assert.That(scope.Progress.Values.Count(value => value.Message == Beutl.Language.MessageStrings.Downloading
                && value.Fraction == null), Is.GreaterThan(1));
        });
    }

    [Test]
    public void InstallFailure_IsReportedAndDownloadIsCleanedUp()
    {
        using var scope = new Scope { InstallFailure = true };
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.InstallAsync());
        Assert.That(error!.Message, Is.EqualTo("Permission denied"));
        Assert.That(Directory.GetFiles(scope.Root, "*.flatpak", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task CancellationDuringDownload_DoesNotInstallAndReleasesTheUpdateGate()
    {
        using var scope = new Scope { BlockDownload = true };
        using var cts = new CancellationTokenSource();
        Task operation = scope.InstallAsync(cts.Token);
        await scope.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await operation);
        Assert.That(scope.InstalledBytes, Is.Null);
        Assert.That(Directory.GetFiles(scope.Root, "*.flatpak", SearchOption.AllDirectories), Is.Empty);
        scope.BlockDownload = false;
        await scope.InstallAsync();
        Assert.That(scope.InstalledBytes, Is.EqualTo(s_bundle));
    }

    [Test]
    public async Task ConcurrentUpdate_IsRejectedBeforeItCanMutateTheInstallation()
    {
        using var scope = new Scope { BlockDownload = true };
        using var other = new Scope();
        using var cts = new CancellationTokenSource();
        Task operation = scope.InstallAsync(cts.Token);
        try
        {
            await scope.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await other.InstallAsync());
            Assert.That(other.Commands, Is.Empty);
            Assert.That(other.Requests, Is.Empty);
        }
        finally
        {
            cts.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await operation);
        }
    }

    [TestCase("Application", "name", "org.example.Other")]
    [TestCase("Instance", "arch", "aarch64")]
    [TestCase("Instance", "branch", "stable")]
    [TestCase("Instance", "instance-path", "relative")]
    [TestCase("Instance", "app-path", "/app")]
    public void UnsupportedSandbox_IsRejected(string section, string key, string value)
    {
        using var scope = new Scope();
        string info = scope.Info;
        string[] lines = info.Split('\n');
        string current = "";
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('[')) current = lines[i].Trim('[', ']');
            else if (current == section && lines[i].StartsWith(key + "=", StringComparison.Ordinal))
                lines[i] = key + "=" + value;
        }
        Assert.Throws<InvalidOperationException>(() => FlatpakUpdateService.ParseInfo(string.Join('\n', lines)));
    }

    [Test]
    public void SandboxInfo_UsesOriginalDeploymentAndDecodesEscapedPaths()
    {
        using var scope = new Scope();
        var info = FlatpakUpdateService.ParseInfo(scope.Info + "\noriginal-app-path=/original/app/commit/files\nextra-args=ignored\\;value\n");
        Assert.That(info.AppPath, Is.EqualTo("/original/app/commit/files"));
        Assert.That(info.InstancePath, Is.EqualTo(scope.Root));
    }

    [Test]
    public async Task ProcessFailure_PropagatesStderrAndSuccessReturnsOutput()
    {
        var psi = TestProcess("printf 'Permission denied' >&2; exit 7");
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await FlatpakUpdateService.RunCommandAsync(psi, CancellationToken.None));
        Assert.That(error!.Message, Is.EqualTo("Permission denied"));
        Assert.That(await FlatpakUpdateService.RunCommandAsync(TestProcess("printf 'ok'"), CancellationToken.None), Is.EqualTo("ok"));
    }

    [Test]
    public async Task CancelingProcess_StopsItBeforeReturning()
    {
        using var scope = new Scope();
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(scope.Root, "started") { EnableRaisingEvents = true };
        watcher.Created += (_, _) => started.TrySetResult();
        var psi = TestProcess("echo $$ > \"$1/pid\"; touch \"$1/started\"; exec sleep 30");
        psi.ArgumentList.Add("test");
        psi.ArgumentList.Add(scope.Root);
        Task operation = FlatpakUpdateService.RunCommandAsync(psi, cts.Token);
        Process? child = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(Path.Combine(scope.Root, "pid"))));
            Assert.That(operation.IsCompleted, Is.False);
            cts.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.That(child.HasExited, Is.True, "Cancellation must stop the installer before cleanup or retry.");
        }
        finally
        {
            cts.Cancel();
            if (child is { HasExited: false }) child.Kill(entireProcessTree: true);
            child?.Dispose();
            Assert.CatchAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private static ProcessStartInfo TestProcess(string script) => new("/bin/sh")
    {
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        ArgumentList = { "-c", script }
    };

    private sealed class ProgressLog : IProgress<FlatpakUpdateProgress>
    {
        internal List<FlatpakUpdateProgress> Values { get; } = [];
        public void Report(FlatpakUpdateProgress value) => Values.Add(value);
    }

    private sealed class Scope : HttpMessageHandler
    {
        private readonly HttpClient _client;
        private readonly FlatpakUpdateService _service;
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "beutl flatpak " + Guid.NewGuid().ToString("N"));
        internal string List { get; set; }
        internal string UserLocation { get; set; }
        internal AppUpdateResponse UpdateResponse { get; set; } = Update;
        internal string? DownloadFailure { get; set; }
        internal bool UnknownContentLength { get; init; }
        internal bool InstallFailure { get; init; }
        internal bool BlockDownload { get; set; }
        internal TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<string> Requests { get; } = [];
        internal List<ProcessStartInfo> Commands { get; } = [];
        internal ProgressLog Progress { get; } = new();
        internal byte[]? InstalledBytes { get; private set; }
        internal string? DownloadPath { get; private set; }
        private string Location => "/installation/app/" + AppRef + "/new-commit";
        internal string Info => $"[Application]\nname=net.beditor.Beutl\n[Instance]\narch=x86_64\nbranch=master\ninstance-path={Root.Replace(" ", "\\s")}\napp-path=/installation/app/{AppRef}/old-commit/files";

        internal Scope(string installation = "user")
        {
            Directory.CreateDirectory(Root);
            List = $"{AppRef}\t{installation}\n";
            UserLocation = Location;
            _client = new HttpClient(this, disposeHandler: false);
            _service = new FlatpakUpdateService(_client, RunAsync);
        }

        internal Task InstallAsync(CancellationToken token = default) => _service.InstallAsync(UpdateResponse, Info, Progress, token);

        private async Task<string> RunAsync(ProcessStartInfo psi, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Commands.Add(psi);
            if (psi.ArgumentList.Contains("list")) return List;
            if (psi.ArgumentList.Contains("info")) return psi.ArgumentList.Contains("--user") ? UserLocation : Location;
            Assert.That(psi.ArgumentList, Does.Contain("install"));
            DownloadPath = psi.ArgumentList[^1];
            InstalledBytes = await File.ReadAllBytesAsync(DownloadPath, token);
            if (InstallFailure) throw new InvalidOperationException("Permission denied");
            return "";
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            Assert.That(request.RequestUri.AbsoluteUri, Is.EqualTo(UpdateResponse.DownloadUrl));
            DownloadStarted.TrySetResult();
            if (BlockDownload) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (DownloadFailure == "http") return new(HttpStatusCode.NotFound);
            byte[] bytes = DownloadFailure switch
            {
                "truncated" => s_bundle[..^1],
                "oversized" => [.. s_bundle, 1],
                "empty" => [],
                _ => s_bundle
            };
            HttpContent content = UnknownContentLength ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes);
            if (!UnknownContentLength) content.Headers.ContentLength = s_bundle.Length;
            return new(HttpStatusCode.OK) { Content = content };
        }

        protected override void Dispose(bool disposing)
        {
            _client.Dispose();
            Directory.Delete(Root, recursive: true);
            base.Dispose(disposing);
        }
    }

    private sealed class UnknownLengthContent(byte[] data) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(data).AsTask();
    }
}
