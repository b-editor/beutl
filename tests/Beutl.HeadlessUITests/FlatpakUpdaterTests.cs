using System.Net;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Api.Clients;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class FlatpakUpdaterTests
{
    private const string Ref = "net.beditor.Beutl/x86_64/master";
    private const string Url = "https://downloads.example.test/registered?token=opaque";
    private string _root = null!;
    [SetUp] public void SetUp() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "beutl update " + Guid.NewGuid().ToString("N")));
    [TearDown] public void TearDown() => Directory.Delete(_root, true);
    private static UpdateDialogViewModel ViewModel(FlatpakUpdater? updater = null, HttpClient? client = null) => new(new AppUpdateResponse
    { LatestVersion = "2.0.0", Url = null, DownloadUrl = Url, IsLatest = false, MustLatest = false }, true, updater, client);

    private FlatpakUpdater Updater(string scope = "user", Func<string[], CancellationToken, Task<string>>? run = null,
        Func<string, string?>? read = null) => new($"""
        [Application]
        name=net.beditor.Beutl
        [Instance]
        arch=x86_64
        branch=master
        instance-path={_root.Replace("\\", "\\\\").Replace(" ", "\\s")}
        app-path=/install/{scope}/app/{Ref}/old/files
        """, run, read);

    [TestCase("user", "--user")]
    [TestCase("system", "--system")]
    [TestCase("external", "--installation=external")]
    public async Task InstallTargetsTheRunningCopy(string scope, string option)
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Flatpak uses Unix paths.");
        string[]? install = null;
        var updater = Updater(scope, (args, _) =>
        {
            if (args[0] == "install") install = args;
            return Task.FromResult(args[0] switch
            {
                "list" => $"{Ref}\tuser\n{Ref}\tsystem\n{Ref}\tsystem (external)\n",
                "info" => $"/install/{(args[1] == "--user" ? "user" : args[1] == "--system" ? "system" : "external")}/app/{Ref}/new",
                _ => ""
            });
        }, _ => "app/" + Ref);
        await updater.InstallAsync(default);
        Assert.That(install, Is.EqualTo(new[] { "install", option, "--bundle", "--or-update", "--noninteractive", "--assumeyes", updater.DownloadPath }));
        Assert.That(updater.DownloadPath, Does.StartWith(Path.Combine(_root, "cache")));
    }

    [TestCase("app/org.example.Other/x86_64/master")]
    [TestCase("app/net.beditor.Beutl/aarch64/master")]
    [TestCase("app/net.beditor.Beutl/x86_64/stable")]
    [TestCase("runtime/net.beditor.Beutl/x86_64/master")]
    [TestCase(null)]
    public void WrongBundleNeverReachesTheHostInstaller(string? reference)
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Flatpak uses Unix paths.");
        Assert.ThrowsAsync<InvalidDataException>(() => Updater(run:
            (_, _) => throw new AssertionException("The host must not be invoked."), read: _ => reference).InstallAsync(default));
    }

    [AvaloniaTest]
    [TestCase("success")]
    [TestCase("unknown-length")]
    [TestCase("http-error")]
    [TestCase("truncated")]
    [TestCase("large-header")]
    [TestCase("large-stream")]
    [TestCase("cancel")]
    public async Task UsesTheExistingDownloaderAndRejectsInvalidTransfers(string state)
    {
        var vm = ViewModel();
        using var response = new HttpResponseMessage(state == "http-error" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
        { Content = state is "unknown-length" or "large-stream" ? new UnknownLengthContent() : new ByteArrayContent([1, 2, 3, 4]) };
        if (state == "truncated") response.Content.Headers.ContentLength = 5;
        if (state == "large-header") response.Content.Headers.ContentLength = FlatpakUpdater.MaximumBundleBytes + 1;
        using var client = new HttpClient(new Handler(response));
        string path = Path.Combine(_root, "update.flatpak");
        if (state == "cancel") vm.Cancel();
        string? result = await vm.DownloadFile(path, client, state == "large-stream" ? 3 : null);
        bool success = state is "success" or "unknown-length";
        Assert.That(result, success ? Is.EqualTo(path) : Is.Null);
        if (success)
        {
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(vm.ProgressValue.Value, Is.EqualTo(1));
        }
        if (state == "large-stream") Assert.That(new FileInfo(path).Length, Is.LessThanOrEqualTo(3));
    }

    [Test]
    public void NativeBundleMetadataReadsIdentityAndRejectsCorruption()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("Uses the GLib library provided by Flatpak.");
        using var source = GetType().Assembly.GetManifestResourceStream("Beutl.HeadlessUITests.Assets.Flatpak.beutl.flatpak")!;
        string path = Path.Combine(_root, "test.flatpak");
        using (var destination = File.Create(path)) source.CopyTo(destination);
        Assert.That(FlatpakBundleMetadata.ReadRef(path), Is.EqualTo("app/" + Ref));
        File.WriteAllText(path, "app/" + Ref);
        Assert.That(FlatpakBundleMetadata.ReadRef(path), Is.Null);
    }

    [AvaloniaTest]
    [TestCase("complete", false)]
    [TestCase("cancel", true)]
    [TestCase("failure", false)]
    public async Task ActiveUpdateInstallsOrCancelsAndAlwaysRemovesTheBundle(string outcome, bool light)
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Flatpak uses Unix paths.");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updater = Updater(run: async (args, token) =>
        {
            if (args[0] == "list") return $"{Ref}\tuser\n";
            if (args[0] == "info") return $"/install/user/app/{Ref}/new";
            Assert.That(await File.ReadAllBytesAsync(args[^1], token), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            started.SetResult();
            await finish.Task.WaitAsync(token);
            if (outcome == "failure") throw new IOException("install failed");
            return "";
        }, read: _ => "app/" + Ref);
        using var client = new HttpClient(new Handler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) }));
        var vm = ViewModel(updater, client);
        var dialog = new UpdateDialog { DataContext = vm };
        var window = new Window { Width = 400, Height = 400, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        Task closed = dialog.ShowAsync(window);
        try
        {
            vm.Start();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(vm.UpdateTask!.IsCompleted, Is.False);
            Assert.That(File.Exists(updater.DownloadPath), Is.True);
            if (outcome == "cancel") dialog.Hide();
            else finish.SetResult();
            await vm.UpdateTask.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Render();
            Assert.That(File.Exists(updater.DownloadPath), Is.False);
            Assert.That(vm.ProgressText.Value, Is.EqualTo(outcome switch
            { "complete" => MessageStrings.FlatpakUpdateCompleted, "cancel" => MessageStrings.Canceled, _ => "install failed" }));
            Assert.That(dialog.PrimaryButtonText, Is.Empty);
            Assert.That(window.IsVisible, Is.True);
            if (outcome != "cancel" && Environment.GetEnvironmentVariable("BEUTL_FLATPAK_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                await Task.Delay(400); // Wait for the entrance animation only when taking a manual capture.
                HeadlessTestHelpers.Render();
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image!.Save(Path.Combine(directory, $"flatpak-{outcome}-{light}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { vm.Cancel(); finish.TrySetResult(); if (vm.UpdateTask != null) await vm.UpdateTask; dialog.Hide(); await closed; window.Close(); }
    }

    [Test]
    public void EnvironmentWithoutMetadataRequiresManualUpdate()
    {
        if (File.Exists("/.flatpak-info")) Assert.Ignore("Requires a host outside the Flatpak sandbox.");
        string? previous = Environment.GetEnvironmentVariable("FLATPAK_ID");
        try
        {
            Environment.SetEnvironmentVariable("FLATPAK_ID", "net.beditor.Beutl");
            Assert.That(FlatpakUpdater.IsRunning, Is.False);
            Assert.That(FlatpakUpdater.RequiresManualUpdate, Is.True);
            Environment.SetEnvironmentVariable("FLATPAK_ID", null);
            Assert.That(FlatpakUpdater.RequiresManualUpdate, Is.False);
        }
        finally { Environment.SetEnvironmentVariable("FLATPAK_ID", previous); }
    }

    [TestCase("ok")]
    [TestCase("fail")]
    [TestCase("wait")]
    public async Task HostProcessReportsErrorsAndStopsOnCancellation(string mode)
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Uses a POSIX test process.");
        string script = Path.Combine(_root, "host");
        string pidFile = Path.Combine(_root, "pid");
        File.WriteAllText(script, "#!/bin/sh\ncase \"$6\" in\nok) printf '%s' \"$7\";;\nfail) echo denied >&2; exit 3;;\nwait) echo $$ > \"$7\"; touch \"$7.ready\"; exec sleep 30;;\nesac\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var cancellation = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(_root, "pid.ready") { EnableRaisingEvents = true };
        watcher.Created += (_, _) => ready.TrySetResult();
        Task<string> operation = FlatpakUpdater.RunHostAsync([mode, pidFile], cancellation.Token, script);
        try
        {
            if (mode == "ok") Assert.That(await operation, Is.EqualTo(pidFile));
            else if (mode == "fail") Assert.That(Assert.ThrowsAsync<IOException>(async () => await operation)!.Message, Is.EqualTo("denied"));
            else
            {
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
                using var process = System.Diagnostics.Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidFile)));
                cancellation.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.That(process.HasExited, Is.True);
            }
        }
        finally { cancellation.Cancel(); try { await operation; } catch (Exception) when (mode != "ok") { } }
    }

    private sealed class Handler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Assert.That(request.RequestUri!.AbsoluteUri, Is.EqualTo(Url)); return Task.FromResult(response); }
    }
    private sealed class UnknownLengthContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(new byte[] { 1, 2, 3, 4 }).AsTask();
    }
}
