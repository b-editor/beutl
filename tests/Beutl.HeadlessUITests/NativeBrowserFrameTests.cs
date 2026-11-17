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
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Testing.Headless;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Moq;

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
                .AfterSetup(_ => Dispatcher.UIThread.Post(() => CheckNavigation(args[0])))
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally { BeutlHomeIsolation.End(); }
    }

    private static async void CheckNavigation(string scenario)
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        Window? window = null;
        int exitCode = 1;
        try
        {
            if (scenario == "--native-browser-referrers")
            {
                window = new Window { Width = 480, Height = 320, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                using var referrerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
                await CheckNewTabReferrersAsync(window, referrerTimeout.Token);
                exitCode = 0;
                return;
            }
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
            if (scenario == "--native-browser-history")
            {
                await CheckHistoryAsync(view, webView, vm, timeout.Token);
                exitCode = 0;
                return;
            }
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

    private static async Task CheckNewTabReferrersAsync(Window window, CancellationToken token)
    {
        await using var server = WebApplication.CreateSlimBuilder().Build();
        server.Urls.Add("http://127.0.0.1:0");
        var requests = System.Threading.Channels.Channel.CreateUnbounded<string>();
        var probeReferrer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MapGet("/referrer-probe", (HttpContext context) =>
        {
            probeReferrer.TrySetResult(context.Request.Headers.Referer.ToString());
            return Results.NoContent();
        });
        var loading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MapGet("/defer", async () =>
        {
            loading.TrySetResult();
            await finishLoad.Task.WaitAsync(token);
            return Results.NoContent();
        });
        server.MapGet("/opened", (HttpContext context) =>
        {
            requests.Writer.TryWrite(context.Request.Headers.Referer.ToString());
            return Results.Content("<title>Opened</title>", "text/html");
        });
        server.MapGet("/media.mp3", (HttpContext context) =>
        {
            requests.Writer.TryWrite(context.Request.Headers.Referer.ToString());
            return Results.Redirect($"http://localhost:{context.Connection.LocalPort}/track.mp3");
        });
        server.MapGet("/track.mp3", (HttpContext context) =>
        {
            requests.Writer.TryWrite(context.Request.Headers.Referer.ToString());
            return Results.Bytes("ID3sample"u8.ToArray(), "audio/mpeg");
        });
        server.MapGet("/attachment", (HttpContext context) =>
        {
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Results.Bytes("file"u8.ToArray(), "application/octet-stream", "sample.zip");
        });
        server.MapGet("/source", (HttpContext context) =>
        {
            if (context.Request.Query.ContainsKey("header")) context.Response.Headers["Referrer-Policy"] = "origin, no-referrer";
            string meta = context.Request.Query.ContainsKey("meta") ? "<meta name='referrer' content='no-referrer'>" : "";
            string delay = context.Request.Query.ContainsKey("loading") ? "<img src='/defer'>" : "";
            string destination = context.Request.Query.ContainsKey("same") ? "/opened" : $"http://localhost:{context.Connection.LocalPort}/opened";
            return Results.Content($"{meta}<a href='{destination}' target='_blank'>open</a>{delay}", "text/html");
        });
        await server.StartAsync(token);
        var origin = new Uri(server.Urls.Single());
        var browser = new NativeWebView();
        var editor = new Mock<IEditorContext>();
        using var vm = new WebBrowserTabViewModel(editor.Object, WebBrowserTabViewModel.BlankPage);
        using var opener = new WebBrowserTabView(_ => browser, () => (true, null, false)) { DataContext = vm };
        using var downloads = new TemporaryDirectory();
        var attachmentOffered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        opener.DownloadOptionsSelector = (uri, _) =>
        {
            if (uri.AbsolutePath != "/attachment")
                return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(downloads.Path, false));
            attachmentOffered.TrySetResult();
            return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(null);
        };
        var openedHost = new ContentControl();
        window.Content = new Grid { Children = { opener, openedHost } };
        WebBrowserTabViewModel? opened = null;
        WebBrowserTabView? openedView = null;
        int count = 0;
        editor.Setup(x => x.OpenToolTab(It.IsAny<IToolContext>())).Returns<IToolContext>(tab =>
        {
            count++;
            openedView?.Dispose();
            opened?.Dispose();
            opened = (WebBrowserTabViewModel)tab;
            openedHost.Content = openedView = new WebBrowserTabView { DataContext = opened };
            return true;
        });

        async Task Load(string query, Func<Task>? whileLoading = null)
        {
            Uri uri = new(origin, "/source?" + query);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded(object? sender, WebViewNavigationCompletedEventArgs e)
            {
                if (e.IsSuccess && e.Request == uri) done.TrySetResult();
            }
            browser.NavigationCompleted += Loaded;
            try
            {
                vm.Address.Value = uri.AbsoluteUri;
                opener.NavigateFromAddress();
                if (whileLoading != null)
                {
                    await loading.Task.WaitAsync(token);
                    try { await whileLoading(); }
                    finally { finishLoad.TrySetResult(); }
                }
                await done.Task.WaitAsync(token);
            }
            finally { browser.NavigationCompleted -= Loaded; }
        }

        async Task Check(string script, string expected)
        {
            int before = count;
            await browser.InvokeScript(script);
            string header = await requests.Reader.ReadAsync(token);
            Assert.That(header, Is.EqualTo(expected), script);
            Assert.That(count, Is.EqualTo(before + 1), script);
            await opened!.IsLoading.FirstAsync(loading => !loading).ToTask(token);
        }

        async Task CheckMediaRedirect(string policy, string expectedRedirect)
        {
            int before = vm.Profile.Downloads.Count;
            await browser.InvokeScript($$"""
                (() => {
                    const a = document.createElement('a');
                    a.href = '/media.mp3?{{policy}}';
                    a.target = '_blank';
                    a.referrerPolicy = '{{policy}}';
                    document.body.append(a);
                    a.click();
                    a.remove();
                })()
                """);
            Assert.That(await requests.Reader.ReadAsync(token), Is.EqualTo(origin.AbsoluteUri), policy);
            Assert.That(await requests.Reader.ReadAsync(token), Is.EqualTo(expectedRedirect), policy);
            while (vm.Profile.Downloads.Count == before) await Task.Delay(20, token);
        }

        try
        {
            await Load("normal&loading", async () =>
            {
                await Check("window.open(document.querySelector('a').href)", origin.AbsoluteUri);
            });
            await Check("document.querySelector('a').click()", origin.AbsoluteUri);
            await Check("history.pushState({}, '', '/items?view=list'); document.querySelector('a').click()", origin.AbsoluteUri);
            await Check("history.replaceState({}, '', '/items?view=grid'); window.open(document.querySelector('a').href)", origin.AbsoluteUri);
            await Check("document.querySelector('a').rel='noreferrer'; document.querySelector('a').click()", "");
            await Check("document.querySelector('a').rel=''; document.querySelector('a').referrerPolicy='same-origin'; document.querySelector('a').click()", "");
            await Check("document.querySelector('a').referrerPolicy='no-referrer'; document.querySelector('a').click()", "");
            await Check("window.open(document.querySelector('a').href, '_blank')", origin.AbsoluteUri);
            await Check("window.open(document.querySelector('a').href, '_blank', 'noreferrer')", "");
            await Check("window.open(document.querySelector('a').href, '_blank', 'noopener,NoReferrer')", "");
            await Load("same");
            await CheckMediaRedirect("same-origin", "");
            if (OperatingSystem.IsWindows())
            {
                await Check("""
                    document.querySelector('a').addEventListener('click', e => e.stopPropagation(), {once:true});
                    document.querySelector('a').click()
                    """, origin.AbsoluteUri);
                await Check("""
                    document.body.addEventListener('click', e => e.stopPropagation(), {once:true});
                    document.body.addEventListener('click', e => e.target.referrerPolicy='no-referrer', {once:true});
                    document.querySelector('a').click()
                    """, "");
                await Check("""
                    document.body.addEventListener('click', e => { e.stopPropagation(); e.target.referrerPolicy='origin' }, {once:true, capture:true});
                    document.querySelector('a').click()
                    """, origin.AbsoluteUri);
                await browser.InvokeScript("document.querySelector('a').removeAttribute('referrerpolicy')");
                await CheckMediaRedirect("origin", origin.AbsoluteUri);
                await browser.InvokeScript("location.assign('/attachment')");
                await attachmentOffered.Task.WaitAsync(token);
                await Check("window.open(document.querySelector('a').href)", origin.AbsoluteUri);
            }
            await Check("document.querySelector('a').click()", origin.AbsoluteUri);
            await Check("window.addEventListener('click', e => e.target.rel='noreferrer', {once:true}); document.querySelector('a').click()", "");
            await Load("meta");
            await Check("document.querySelector('a').click()", "");
            await Check("window.open(document.querySelector('a').href)", "");
            await Check("document.querySelector('meta').remove(); window.open(document.querySelector('a').href)", "");
            await Check("const m=document.createElement('meta'); m.name='referrer'; m.content='origin'; document.head.append(m); window.open(document.querySelector('a').href)", origin.AbsoluteUri);
            await Check("document.querySelector('meta').content='invalid'; window.open(document.querySelector('a').href)", origin.AbsoluteUri);
            await Check("document.querySelector('meta').content='no-referrer'; document.querySelector('meta').content='invalid'; window.open(document.querySelector('a').href)", "");
            if (OperatingSystem.IsWindows())
            {
                await Load("normal");
                // Let the observer deliver the removal before reusing the detached subtree.
                await browser.InvokeScript("""
                    window.referrerWrapper = document.createElement('div');
                    document.head.append(window.referrerWrapper);
                    window.referrerWrapper.remove();
                    window.referrerWrapper.append(document.createElement('meta'));
                    """);
                Task popup = Check("""
                    (() => {
                        const wrapper = window.referrerWrapper;
                        const meta = wrapper.firstElementChild;
                        meta.name = 'referrer';
                        meta.content = 'no-referrer';
                        document.head.append(wrapper);
                        meta.remove();
                        fetch('/referrer-probe');
                        window.open(document.querySelector('a').href);
                    })()
                    """, "");
                Assert.That(await probeReferrer.Task.WaitAsync(token), Is.Empty, "Chromium's own request after removing the nested meta");
                await popup;
                await Check("""
                    const permissive = document.createElement('meta');
                    permissive.name = 'referrer';
                    permissive.content = 'origin';
                    document.head.append(permissive);
                    window.open(document.querySelector('a').href)
                    """, origin.AbsoluteUri);
                await Check("document.querySelector('meta[name=referrer]').remove(); window.open(document.querySelector('a').href)", origin.AbsoluteUri);
            }
            await Load("header");
            await Check("document.querySelector('a').click()", "");
            await Check("history.pushState({}, '', '/items?view=grid'); document.querySelector('a').click()", "");
            await Check("window.open(document.querySelector('a').href)", "");
            await Check("document.querySelector('a').referrerPolicy='origin'; document.querySelector('a').click()", origin.AbsoluteUri);
        }
        finally
        {
            finishLoad.TrySetResult();
            openedView?.Dispose();
            opened?.Dispose();
        }
    }

    private static async Task CheckHistoryAsync(WebBrowserTabView view, NativeWebView webView,
        WebBrowserTabViewModel vm, CancellationToken token)
    {
        await using var server = WebApplication.CreateSlimBuilder().Build();
        server.Urls.Add("http://127.0.0.1:0");
        server.MapGet("/{**path}", () => Results.Content("<title>History</title>", "text/html"));
        await server.StartAsync(token);
        var first = new Uri(server.Urls.Single() + "/first");
        var page = new Uri(first, "/second");
        Button ToolbarButton(string tip) => view.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(ToolTip.GetTip(button), tip));
        Button back = ToolbarButton(Strings.Back);
        Button forward = ToolbarButton(Strings.Forward);
        Button refresh = ToolbarButton(Strings.Refresh);
        Button stop = ToolbarButton(Strings.Stop);

        async Task LoadDocument(Uri uri, Action navigate)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? _, WebViewNavigationCompletedEventArgs e)
            {
                if (e.Request == uri) loaded.TrySetResult();
            }
            webView.NavigationCompleted += Completed;
            try
            {
                navigate();
                await loaded.Task.WaitAsync(token);
            }
            finally { webView.NavigationCompleted -= Completed; }
        }

        await LoadDocument(first, () => webView.Navigate(first));
        await LoadDocument(page, () =>
        {
            vm.Address.Value = page.AbsoluteUri;
            view.NavigateFromAddress();
            Assert.That(stop.IsVisible, Is.True);
        });
        Assert.That(back.IsEnabled, Is.True);
        await webView.InvokeScript("history.pushState(1, '', '?step=2')");

        async Task Traverse(Button button, string relativeAddress)
        {
            string address = new Uri(page, relativeAddress).AbsoluteUri;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (WebBrowserTabView.NormalizePageTitle(await webView.InvokeScript("location.href")) != address)
                await Task.Delay(20, token);
            Assert.Multiple(() =>
            {
                Assert.That(vm.IsLoading.Value, Is.False);
                Assert.That(refresh.IsVisible, Is.True);
                Assert.That(stop.IsVisible, Is.False);
                Assert.That(vm.Address.Value, Is.EqualTo(address));
                Assert.That(vm.CurrentUri.AbsoluteUri, Is.EqualTo(address));
            });
        }

        await Traverse(back, "");
        Assert.That(forward.IsEnabled, Is.True);
        await Traverse(forward, "?step=2");
        Assert.That(back.IsEnabled, Is.True);
        await webView.InvokeScript("history.pushState(2, '', '#section')");
        await Traverse(back, "?step=2");
        await Traverse(forward, "?step=2#section");
        await Traverse(back, "?step=2");
        await Traverse(back, "");
        await LoadDocument(first, () => back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert.That(vm.CurrentUri, Is.EqualTo(first));
        Assert.That(refresh.IsVisible, Is.True);
    }
}

[TestFixture]
public class NativeBrowserFrameTests
{
    [TestCase("--native-browser-frames", TestName = "EmbeddedDocumentsLoadWithoutBecomingTopLevelNavigations", IncludePlatform = "MacOSX")]
    [TestCase("--native-browser-history", TestName = "HistoryButtonsSettleAfterSameDocumentNavigation", IncludePlatform = "MacOSX")]
    [TestCase("--native-browser-referrers", TestName = "NewTabRequestsHonorLinkAndDocumentReferrerPolicies", IncludePlatform = "Win,Linux")]
    public async Task NativeNavigation(string scenario)
    {
        if (OperatingSystem.IsLinux() && (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
            || !WebBrowserTabView.GetWebViewAvailability().IsAvailable))
            Assert.Ignore("Native browser navigation requires an X display and a WebKit runtime.");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(NativeBrowserFrameTestHost).Assembly.Location);
        start.ArgumentList.Add(scenario);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(scenario == "--native-browser-referrers" ? 90 : 60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        Assert.That(process.ExitCode, Is.Zero, await output + "\n" + await error);
    }
}
