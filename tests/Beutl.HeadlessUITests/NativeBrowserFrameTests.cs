using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// WKWebView must run on the process main thread, outside the headless dispatcher.
internal static class NativeBrowserFrameTestHost
{
    internal static int Run(string[] args)
    {
        BeutlHomeIsolation.Begin("beutl-native-browser-frames");
        OpenALPreload.EnsureLoaded();
        try
        {
            return AppBuilder.Configure<TestApp>().UsePlatformDetect()
                .AfterSetup(_ => Dispatcher.UIThread.Post(CheckFrames))
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally { BeutlHomeIsolation.End(); }
    }

    private static async void CheckFrames()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        Window? window = null;
        int exitCode = 1;
        try
        {
            using var temporary = new TemporaryDirectory();
            var webView = new NativeWebView();
            webView.EnvironmentRequested += (_, e) =>
            {
                if (e is AppleWKWebViewEnvironmentRequestedEventArgs apple) apple.NonPersistentDataStore = true;
            };
            var page = new Uri("https://page.example/");
            using var vm = new WebBrowserTabViewModel(new BrowserEditorContext(), page);
            using var view = new WebBrowserTabView(_ => webView, () => (true, null, false)) { DataContext = vm };
            window = new Window { Content = view, Width = 640, Height = 480, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var loaded = new TaskCompletionSource();
            webView.NavigationCompleted += (_, _) => loaded.TrySetResult();
            webView.NavigateToString("""
                <script>
                    window.loadedFrames = [];
                    addEventListener('message', e => loadedFrames.push(e.data));
                </script>
                <iframe srcdoc="<script>parent.postMessage('srcdoc', '*')</script>"></iframe>
                <iframe src="data:text/html,<script>parent.postMessage('data', '*')</script>"></iframe>
                <script>
                    const frame = document.createElement('iframe');
                    frame.src = URL.createObjectURL(new Blob(
                        ["<script>parent.postMessage('blob', '*')<\/script>"], {type:'text/html'}));
                    document.body.append(frame);
                </script>
                """, page);
            await loaded.Task.WaitAsync(timeout.Token);
            while (await webView.InvokeScript("window.loadedFrames?.length === 3") != "true")
                await Task.Delay(50, timeout.Token);

            Assert.That(vm.CurrentUri, Is.EqualTo(page));
            Assert.That(vm.ErrorMessage.Value, Is.Null);

            // The frame fix must not relax the existing policy for actual page navigations.
            string file = Path.Combine(temporary.Path, "local.html");
            Directory.CreateDirectory(temporary.Path);
            File.WriteAllText(file, "Local page");
            var localUri = new Uri(file);
            var canceled = new TaskCompletionSource<bool>();
            webView.NavigationStarted += (_, e) =>
            {
                if (e.Request == localUri) canceled.TrySetResult(e.Cancel);
            };
            webView.Navigate(localUri);
            Assert.That(await canceled.Task.WaitAsync(timeout.Token), Is.True);
            Assert.That(vm.CurrentUri, Is.EqualTo(page));
            Console.WriteLine("Embedded srcdoc, data and blob frames loaded; top-level file navigation stayed blocked.");
            exitCode = 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        finally
        {
            window?.Close();
            lifetime.Shutdown(exitCode);
        }
    }
}

[TestFixture]
public class NativeBrowserFrameTests
{
    [Test]
    [Platform("MacOSX")]
    public async Task EmbeddedDocumentsLoadWithoutBecomingTopLevelNavigations()
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(NativeBrowserFrameTestHost).Assembly.Location);
        start.ArgumentList.Add("--native-browser-frames");
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        Assert.That(process.ExitCode, Is.Zero, await output + "\n" + await error);
    }
}
