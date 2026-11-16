using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Language;
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
            await CheckPageLoading(view, webView, vm, timeout.Token);
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

    private static async Task CheckPageLoading(WebBrowserTabView view, NativeWebView webView, WebBrowserTabViewModel vm, CancellationToken token)
    {
        List<bool> states = [];
        using var subscription = vm.IsLoading.Subscribe(states.Add);
        await webView.InvokeScript("""
            const extra = document.createElement('iframe');
            extra.srcdoc = "<script>parent.postMessage('extra', '*')<\/script>";
            document.body.append(extra);
            """);
        while (await webView.InvokeScript("window.loadedFrames.includes('extra')") != "true")
            await Task.Delay(20, token);
        Assert.That(states, Is.EqualTo(new[] { false }));

        // Hold back the response so a page-initiated link must show its loading state before it commits.
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var destination = new Uri($"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/");
        var completed = new TaskCompletionSource();
        webView.NavigationCompleted += (_, e) =>
        {
            if ((e.Request ?? webView.Source) == destination && e.IsSuccess) completed.TrySetResult();
        };
        Task started = vm.IsLoading.FirstAsync(loading => loading).ToTask(token);
        await webView.InvokeScript($"const link = document.createElement('a'); link.href = '{destination}'; link.click();");
        using var client = await server.AcceptTcpClientAsync(token);
        await started;
        Assert.That(vm.IsLoading.Value, Is.True);
        await client.GetStream().WriteAsync("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\nContent-Type: text/html\r\n\r\nOK"u8.ToArray(), token);
        await completed.Task.WaitAsync(token);
        Assert.That(states, Is.EqualTo(new[] { false, true, false }));

        client.Close();
        states.Clear();
        started = vm.IsLoading.FirstAsync(loading => loading).ToTask(token);
        await webView.InvokeScript("location.assign('/slow')");
        using var slow = await server.AcceptTcpClientAsync(token);
        await started;
        Button stop = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), Strings.Stop));
        stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(states, Is.EqualTo(new[] { true, false }));
        Assert.That(vm.ErrorMessage.Value, Is.Null);

        server.Stop();
        states.Clear();
        Task failed = vm.ErrorMessage.FirstAsync(message => message == Strings.WebPageLoadFailed).ToTask(token);
        await webView.InvokeScript("location.assign('/offline')");
        await failed;
        Assert.That(states, Is.EqualTo(new[] { true, false }));
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
