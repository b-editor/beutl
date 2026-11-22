using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Testing.Headless;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Moq;

using static Beutl.Controls.ObjCRuntime;

namespace Beutl.HeadlessUITests;

// WKWebView must run on the process main thread, outside the headless dispatcher.
internal static partial class NativeBrowserFrameTestHost
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
            if (scenario == "--native-browser-download-methods")
            {
                window = new Window { Width = 480, Height = 320, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                using var downloadTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
                await CheckDownloadMethodsAsync(window, downloadTimeout.Token);
                exitCode = 0;
                return;
            }
            if (scenario == "--native-browser-download-referrers")
            {
                window = new Window { Width = 480, Height = 320, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                using var downloadTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
                await CheckPageLinkDownloadReferrersAsync(window, downloadTimeout.Token);
                exitCode = 0;
                return;
            }
            if (scenario == "--native-browser-referrers")
            {
                window = new Window { Width = 480, Height = 320, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                using var referrerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
                await CheckNewTabReferrersAsync(window, referrerTimeout.Token);
                exitCode = 0;
                return;
            }
            if (scenario == "--native-browser-cookie-clear")
            {
                window = new Window { Width = 480, Height = 320, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                using var cookieTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                exitCode = await CheckLinuxCookieClearAsync(window, cookieTimeout.Token) ? 0 : SkippedExitCode;
                return;
            }
            if (scenario == "--native-browser-linux-focus")
            {
                window = new Window { Width = 480, Height = 320, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                using var focusTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                exitCode = await CheckLinuxFocusAsync(window, focusTimeout.Token) ? 0 : SkippedExitCode;
                return;
            }
            if (scenario == "--native-browser-adblock-reference")
            {
                window = new Window { Width = 480, Height = 320, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                using var adBlockTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await CheckAdBlockReferenceAsync(window, adBlockTimeout.Token);
                exitCode = 0;
                return;
            }
            if (scenario == "--native-browser-superseded-navigation")
            {
                window = new Window { Width = 480, Height = 320, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                using var navigationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await CheckSupersededNavigationAsync(window, navigationTimeout.Token);
                exitCode = 0;
                return;
            }
            using var temporary = new TemporaryDirectory();
            var webView = new NativeWebView();
            webView.EnvironmentRequested += (_, e) =>
            {
                if (e is AppleWKWebViewEnvironmentRequestedEventArgs apple) apple.NonPersistentDataStore = true;
            };
            bool windowsHistory = OperatingSystem.IsWindows() && scenario == "--native-browser-history";
            // A failed startup URL creates an error page that WebView2 can retry during the history checks.
            Uri page = windowsHistory ? WebBrowserTabViewModel.BlankPage : new Uri("https://page.example/");
            using var vm = new WebBrowserTabViewModel(new BrowserEditorContext(), page);
            using var view = new WebBrowserTabView(_ => webView, () => (true, null, false)) { DataContext = vm };
            window = new Window { Content = view, Width = 640, Height = 480, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(windowsHistory ? 30 : 10));
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

    // Tells NativeNavigation that the scenario cannot run on this system, as Assert.Ignore would in-process.
    internal const int SkippedExitCode = 77;
    internal const string InsecureHost = "insecure.test";

    private static async Task<bool> CheckLinuxCookieClearAsync(Window window, CancellationToken token)
    {
        await using var server = WebApplication.CreateSlimBuilder().Build();
        server.Urls.Add("http://127.0.0.1:0");
        server.MapGet("/", (HttpContext context) =>
        {
            context.Response.Headers.SetCookie = new Microsoft.Extensions.Primitives.StringValues(
                ["session=one; Path=/; HttpOnly", "persistent=two; Max-Age=3600; Path=/; HttpOnly", "abc"]);
            return Results.Content("<html><body>Cookies</body></html>", "text/html");
        });
        var before = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var after = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MapGet("/cookies/{phase}", (HttpContext context, string phase) =>
        {
            (phase == "before" ? before : after).TrySetResult(
                context.Request.Headers.TryGetValue("Cookie", out var cookies) ? cookies.ToString() : null);
            return Results.NoContent();
        });
        await server.StartAsync(token);

        var page = new Uri(server.Urls.Single());
        var browser = new NativeWebView();
        // The host sets BEUTL_HOME before starting Avalonia, so persistence uses a throwaway store.
        browser.AdapterCreated += LinuxWebKitCookies.PersistCookies;
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess && e.Request == page) loaded.TrySetResult();
        };
        window.Content = browser;
        browser.Navigate(page);
        await loaded.Task.WaitAsync(token);

        var handle = browser.TryGetPlatformHandle();
        if (!LinuxWebKitCookies.CanListCookies(handle))
        {
            Console.WriteLine("This WebKit cannot list cookies (webkit_cookie_manager_get_all_cookies needs WebKit 2.42).");
            return false;
        }
        var cookies = await LinuxWebKitCookies.GetAllAsync(handle).WaitAsync(token);
        Assert.That(cookies.Select(cookie => (cookie.Name, cookie.Value)),
            Is.EquivalentTo(new[] { ("session", "one"), ("persistent", "two") }));
        await browser.InvokeScript("void fetch('/cookies/before', { cache: 'no-store' });").WaitAsync(token);
        Assert.That(await before.Task.WaitAsync(token), Does.Contain("session=one").And.Contain("persistent=two"));

        var clear = LinuxWebKitCookies.CreateClearAction([handle]);
        Assert.That<Func<Task>?>(clear, Is.Not.Null);
        await clear!().WaitAsync(token);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await LinuxWebKitCookies.GetAllAsync(handle).WaitAsync(token), Is.Empty);
            await browser.InvokeScript("void fetch('/cookies/after', { cache: 'no-store' });").WaitAsync(token);
            Assert.That(await after.Task.WaitAsync(token), Is.Null);
        });
        return true;
    }

    private static async Task<bool> CheckLinuxFocusAsync(Window window, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) return false;

        var page = new Uri("https://page.example/");
        var browser = new NativeWebView();
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess) loaded.TrySetResult();
        };

        using var vm = new WebBrowserTabViewModel(new BrowserEditorContext(), page);
        using var view = new WebBrowserTabView(_ => browser, () => (true, null, false)) { DataContext = vm };
        int leakedToWindow = 0;
        window.AddHandler(InputElement.KeyDownEvent, (_, _) => leakedToWindow++, RoutingStrategies.Bubble);
        window.Content = view;

        browser.NavigateToString("<html><body><input id='box' type='text'></body></html>", page);
        await loaded.Task.WaitAsync(token);

        IPlatformHandle? handle = browser.TryGetPlatformHandle();
        if (handle is not IGtkWebViewPlatformHandle gtk || handle.HandleDescriptor != "XID" || handle.Handle == 0 || gtk.WebKitWebView == 0)
        {
            Console.WriteLine("Linux X11 focus test requires the GTK X11 WebKit adapter.");
            return false;
        }

        const nint keyPressAndReleaseMask = 0x3;
        nint? mask = null;
        while (mask is null || (mask.Value & keyPressAndReleaseMask) != keyPressAndReleaseMask)
        {
            mask = await LinuxWebKitFocus.ReadDoNotPropagateMaskAsync(handle.Handle).WaitAsync(token);
            if (mask is not null && (mask.Value & keyPressAndReleaseMask) == keyPressAndReleaseMask) break;
            await Task.Delay(20, token);
        }

        Assert.That(mask!.Value & keyPressAndReleaseMask, Is.EqualTo(keyPressAndReleaseMask),
            "WebKitGTK's embedded X11 window must block core KeyPress/KeyRelease propagation to Avalonia.");

        // 1. Send a real core X11 KeyPress with propagate=True to WebKitGTK's X11 window.
        // Without do_not_propagate_mask, X11 propagates this event up to Avalonia's host window.
        nint hostXid = window.TryGetPlatformHandle()?.Handle ?? 0;
        Assert.That(hostXid, Is.Not.EqualTo(nint.Zero));
        await SendX11KeyPressAsync(handle.Handle, 0x06d /* XK_m */, state: 0).WaitAsync(token);
        await Task.Delay(100, token);
        Assert.That(leakedToWindow, Is.Zero,
            "Core X11 KeyPress on the embedded WebKitGTK window must not propagate to Avalonia's host window.");

        // 2. Focus the page input and send a native GTK key-press-event for 'm'; it must enter the input without triggering Avalonia shortcuts.
        await browser.InvokeScript("document.getElementById('box').focus()").WaitAsync(token);
        await SendGtkKeyPressAsync(gtk.WebKitWebView, 0x06d /* GDK_KEY_m */, state: 0, text: "m").WaitAsync(token);
        string? typedValue = null;
        for (int attempt = 0; attempt < 25; attempt++)
        {
            typedValue = WebBrowserTabView.NormalizePageTitle(await browser.InvokeScript("document.getElementById('box').value").WaitAsync(token));
            if (typedValue == "m") break;
            await Task.Delay(20, token);
        }
        Assert.Multiple(() =>
        {
            Assert.That(typedValue, Is.EqualTo("m"), "Native GTK key input must type into the focused page input.");
            Assert.That(leakedToWindow, Is.Zero, "Typing into the page input must not trigger Avalonia shortcuts.");
        });

        // 3. Browser-local shortcuts (Ctrl+F, Ctrl++) must still work from GTK key-press-event while the page has focus.
        const uint gdkControlMask = 1u << 2;
        const uint gdkShiftMask = 1u << 0;
        await SendGtkKeyPressAsync(gtk.WebKitWebView, 0x02b /* GDK_KEY_plus */, gdkControlMask | gdkShiftMask).WaitAsync(token);
        var zoomReset = view.FindControl<FluentAvalonia.UI.Controls.FAMenuFlyoutItem>("ZoomResetMenuItem")!;
        for (int attempt = 0; attempt < 25; attempt++)
        {
            if (zoomReset.Text?.Contains("110%", StringComparison.Ordinal) == true) break;
            await Task.Delay(20, token);
        }
        Assert.That(zoomReset.Text, Does.Contain("110%"), "Ctrl++ inside the page must invoke browser zoom.");

        await SendGtkKeyPressAsync(gtk.WebKitWebView, 0x066 /* GDK_KEY_f */, gdkControlMask).WaitAsync(token);
        Grid findPanel = view.FindControl<Grid>("FindPanel")!;
        for (int attempt = 0; attempt < 25; attempt++)
        {
            if (findPanel.IsVisible) break;
            await Task.Delay(20, token);
        }
        Assert.That(findPanel.IsVisible, Is.True, "Ctrl+F inside the page must open the browser find panel.");

        // 4. Once focus leaves NativeWebView (Ctrl+F moved focus to FindTextBox -> move to Window), X11 KeyPress on the host window reaches Avalonia normally.
        window.Focusable = true;
        window.Focus();
        await SendX11KeyPressAsync(hostXid, 0x06d /* XK_m */, state: 0).WaitAsync(token);
        for (int attempt = 0; attempt < 25; attempt++)
        {
            if (leakedToWindow > 0) break;
            await Task.Delay(20, token);
        }
        Assert.That(leakedToWindow, Is.GreaterThan(0),
            "After focus leaves the browser, X11 KeyPress on the host window must reach Avalonia normally.");

        return true;
    }

    private static Task SendX11KeyPressAsync(nint targetWindow, nuint keysym, uint state)
    {
        return LinuxWebKit.OnGlibAsync(() =>
        {
            nint gdkDisplay = GdkDisplayGetDefault();
            nint xDisplay = GdkX11DisplayGetXDisplay(gdkDisplay);
            uint keycode = XKeysymToKeycode(xDisplay, keysym);
            var ev = new XKeyEventBuffer
            {
                Type = 2, // KeyPress
                Display = xDisplay,
                Window = targetWindow,
                Root = XDefaultRootWindow(xDisplay),
                State = state,
                Keycode = keycode,
                SameScreen = 1
            };
            XSendEvent(xDisplay, targetWindow, propagate: 1, eventMask: 1 /* KeyPressMask */, ref ev);
            XSync(xDisplay, 0);
        });
    }

    private static unsafe Task SendGtkKeyPressAsync(nint gtkWidget, uint keyval, uint state, string? text = null)
    {
        return LinuxWebKit.OnGlibAsync(gtkWidget, () =>
        {
            GtkWidgetGrabFocus(gtkWidget);
            nint gdkDisplay = GdkDisplayGetDefault();
            nint xDisplay = GdkX11DisplayGetXDisplay(gdkDisplay);
            ushort keycode = (ushort)XKeysymToKeycode(xDisplay, keyval);
            nint gdkWindow = GtkWidgetGetWindow(gtkWidget);
            nint evPtr = GdkEventNew(8 /* GDK_KEY_PRESS */);
            nint textPtr = text != null ? Marshal.StringToCoTaskMemUTF8(text) : 0;
            try
            {
                var ev = (GdkEventKey*)evPtr;
                ev->Window = gdkWindow != 0 ? LinuxWebKit.ObjectRef(gdkWindow) : 0;
                ev->SendEvent = 1;
                ev->State = state;
                ev->Keyval = keyval;
                ev->Length = text?.Length ?? 0;
                ev->String = textPtr;
                ev->HardwareKeycode = keycode;
                GtkWidgetEvent(gtkWidget, evPtr);
                ev->String = 0;
            }
            finally
            {
                if (textPtr != 0) Marshal.FreeCoTaskMem(textPtr);
                GdkEventFree(evPtr);
            }
        });
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XKeyEventBuffer
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(8)] public nuint Serial;
        [FieldOffset(16)] public int SendEvent;
        [FieldOffset(24)] public nint Display;
        [FieldOffset(32)] public nint Window;
        [FieldOffset(40)] public nint Root;
        [FieldOffset(48)] public nint Subwindow;
        [FieldOffset(56)] public nuint Time;
        [FieldOffset(64)] public int X;
        [FieldOffset(68)] public int Y;
        [FieldOffset(72)] public int XRoot;
        [FieldOffset(76)] public int YRoot;
        [FieldOffset(80)] public uint State;
        [FieldOffset(84)] public uint Keycode;
        [FieldOffset(88)] public int SameScreen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdkEventKey
    {
        public int Type;
        public nint Window;
        public sbyte SendEvent;
        public uint Time;
        public uint State;
        public uint Keyval;
        public int Length;
        public nint String;
        public ushort HardwareKeycode;
        public byte Group;
        public uint IsModifier;
    }

    [LibraryImport("libgdk-3.so.0", EntryPoint = "gdk_display_get_default")]
    private static partial nint GdkDisplayGetDefault();

    [LibraryImport("libgdk-3.so.0", EntryPoint = "gdk_x11_display_get_xdisplay")]
    private static partial nint GdkX11DisplayGetXDisplay(nint display);

    [LibraryImport("libgdk-3.so.0", EntryPoint = "gdk_event_new")]
    private static partial nint GdkEventNew(int type);

    [LibraryImport("libgdk-3.so.0", EntryPoint = "gdk_event_free")]
    private static partial void GdkEventFree(nint ev);

    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_widget_grab_focus")]
    private static partial void GtkWidgetGrabFocus(nint widget);

    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_widget_get_window")]
    private static partial nint GtkWidgetGetWindow(nint widget);

    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_widget_event")]
    private static partial int GtkWidgetEvent(nint widget, nint ev);

    [LibraryImport("libX11.so.6")]
    private static partial nint XDefaultRootWindow(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial uint XKeysymToKeycode(nint display, nuint keysym);

    [LibraryImport("libX11.so.6")]
    private static partial int XSendEvent(nint display, nint window, int propagate, nint eventMask, ref XKeyEventBuffer ev);

    [LibraryImport("libX11.so.6")]
    private static partial int XSync(nint display, int discard);

    private static async Task CheckSupersededNavigationAsync(Window window, CancellationToken token)
    {
        await using var server = WebApplication.CreateSlimBuilder().Build();
        server.Urls.Add("http://127.0.0.1:0");
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hanging = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MapGet("/fast", () => Results.Content("<html><body>fast</body></html>", "text/html"));
        server.MapGet("/stream", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/html";
            // WebKit holds back a streaming document's first few bytes.
            await context.Response.WriteAsync("<html><body><!--" + new string('x', 4096) + "-->start", token);
            await context.Response.Body.FlushAsync(token);
            await finish.Task.WaitAsync(token);
            await context.Response.WriteAsync("</body></html>", token);
        });
        server.MapGet("/slow", async (HttpContext context, int ms) =>
        {
            context.Response.ContentType = "text/html";
            // Starting B's response makes WebView2 complete the superseded document while B is still loading.
            // WebKit cancels the streaming page (-999) before B starts, so B must stay loading after the cancellation.
            await context.Response.WriteAsync("<html><body>", token);
            await context.Response.Body.FlushAsync(token);
            await Task.Delay(ms, token);
            await context.Response.WriteAsync("slow</body></html>", token);
        });
        server.MapGet("/redirect", (HttpContext context) => Results.Redirect(context.Request.Query["to"].ToString()));
        server.MapGet("/hang", async () =>
        {
            hanging.TrySetResult();
            await finish.Task.WaitAsync(token);
            return Results.Content("<html><body>done</body></html>", "text/html");
        });
        await server.StartAsync(token);
        var origin = new Uri(server.Urls.Single());
        var browser = new NativeWebView();
        using var vm = new WebBrowserTabViewModel(new BrowserEditorContext(), WebBrowserTabViewModel.BlankPage);
        using var view = new WebBrowserTabView(_ => browser, () => (true, null, false)) { DataContext = vm };
        window.Content = view;
        Button ToolbarButton(string tip) => view.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(ToolTip.GetTip(button), tip));

        void Navigate(Uri uri)
        {
            vm.Address.Value = uri.AbsoluteUri;
            view.NavigateFromAddress();
        }

        async Task Load(Uri expected, Action navigate, Func<Task>? whileLoading = null)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? sender, WebViewNavigationCompletedEventArgs e)
            {
                if (e.IsSuccess && e.Request == expected) loaded.TrySetResult();
            }
            browser.NavigationCompleted += Completed;
            try
            {
                navigate();
                if (whileLoading != null) await whileLoading();
                await loaded.Task.WaitAsync(token);
                Assert.Multiple(() =>
                {
                    Assert.That(vm.IsLoading.Value, Is.False);
                    Assert.That(vm.CurrentUri, Is.EqualTo(expected));
                    Assert.That(vm.ErrorMessage.Value, Is.Null);
                });
            }
            finally { browser.NavigationCompleted -= Completed; }
        }

        try
        {
            var fast = new Uri(origin, "/fast");
            await Load(fast, () => Navigate(fast));
            Button stop = ToolbarButton(Strings.Stop);
            Button refresh = ToolbarButton(Strings.Refresh);
            Navigate(new Uri(origin, "/stream"));
            // Scripts can see the body once the streaming document has committed, before it finishes loading.
            while ((await browser.InvokeScript("document.body ? document.body.textContent : ''"))?.Contains("start", StringComparison.Ordinal) != true)
                await Task.Delay(20, token);

            var slow = new Uri(origin, "/slow?ms=1500");
            await Load(slow, () => Navigate(slow), async () =>
            {
                await Task.Delay(400, token);
                Assert.Multiple(() =>
                {
                    Assert.That(vm.IsLoading.Value, Is.True, "The next navigation must still be loading at 400 ms.");
                    Assert.That(vm.CurrentUri, Is.EqualTo(slow), "The superseded completion must not restore the streaming page's address.");
                    Assert.That(vm.ErrorMessage.Value, Is.Null);
                    Assert.That(stop.IsVisible, Is.True);
                });
            });

            var redirected = new Uri(origin, "/fast?n=R");
            await Load(redirected, () => Navigate(new Uri(origin, "/redirect?to=/fast?n=R")));

            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Stopped(object? sender, WebViewNavigationCompletedEventArgs e)
            {
                if (!e.IsSuccess) stopped.TrySetResult();
            }
            browser.NavigationCompleted += Stopped;
            try
            {
                Navigate(new Uri(origin, "/hang"));
                await hanging.Task.WaitAsync(token);
                stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // On macOS, Stop clears loading synchronously and Avalonia only reports finished loads.
                if (OperatingSystem.IsWindows()) await stopped.Task.WaitAsync(token);
                Assert.That(vm.IsLoading.Value, Is.False);
                Assert.That(vm.ErrorMessage.Value, Is.Null);
            }
            finally { browser.NavigationCompleted -= Stopped; }

            await Load(redirected, () =>
            {
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.That(vm.IsLoading.Value, Is.True);
            });

            var refused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Refused(object? sender, WebViewNavigationCompletedEventArgs e)
            {
                if (!e.IsSuccess) refused.TrySetResult();
            }
            browser.NavigationCompleted += Refused;
            try
            {
                Navigate(new Uri(origin, "/redirect?to=beutl-test:probe"));
                if (OperatingSystem.IsWindows()) await refused.Task.WaitAsync(token);
                else
                {
                    // The macOS handler reports the policy failure directly to the tab.
                    while (vm.IsLoading.Value) await Task.Delay(20, token);
                }
            }
            finally { browser.NavigationCompleted -= Refused; }
            Assert.That(vm.IsLoading.Value, Is.False);

            var optionsRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.DownloadOptionsSelector = (_, _) =>
            {
                optionsRequested.TrySetResult();
                return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(null);
            };
            await browser.InvokeScript("""
                window.invokeCSharpAction(JSON.stringify({kind: 'beutl-download', url: location.origin + '/file.mp3'}))
                """);
            await Task.WhenAny(optionsRequested.Task, Task.Delay(TimeSpan.FromSeconds(3), token));
            Assert.That(optionsRequested.Task.IsCompleted, Is.True,
                "DownloadOptionsSelector must be called after the refused redirect.");
        }
        finally { finish.TrySetResult(); }
    }

    private static async Task CheckAdBlockReferenceAsync(Window window, CancellationToken token)
    {
        var webView = new NativeWebView();
        webView.EnvironmentRequested += BrowserWebViewEnvironment.Configure;
        var loaded = new TaskCompletionSource();
        webView.NavigationCompleted += (_, _) => loaded.TrySetResult();
        window.Content = webView;
        webView.NavigateToString("<p>ready</p>");
        await loaded.Task.WaitAsync(token);

        var handle = (IWindowsWebView2PlatformHandle)webView.TryGetPlatformHandle()!;
        nint pointer = handle.CoreWebView2;
        Assert.That(pointer, Is.Not.EqualTo(nint.Zero));
        var page = new Uri("https://page.example/");
        const int Cycles = 3;
        // Guard references keep an over-release from destroying the adapter's object.
        for (int i = 0; i < Cycles; i++) Marshal.AddRef(pointer);
        int before = ReferenceCount(pointer);
        int after;
        try
        {
            for (int i = 0; i < Cycles; i++)
            {
                using var backend = new WindowsAdBlockBackend(handle, BrowserAdBlockTestRules.Parse("||ads.example^"), () => page);
                await backend.EnableAsync();
            }
        }
        finally
        {
            after = ReferenceCount(pointer);
            // Restore lost references before releasing the guards so a failure can be reported safely.
            for (int i = after; i < before; i++) Marshal.AddRef(pointer);
            for (int i = 0; i < Cycles; i++) Marshal.Release(pointer);
        }
        Assert.That(after, Is.EqualTo(before), "Creating and disposing the ad-block backend must not change the adapter's ICoreWebView2 reference count.");
        Assert.That(await webView.InvokeScript("1 + 1"), Is.EqualTo("2"));

        static int ReferenceCount(nint unknown)
        {
            Marshal.AddRef(unknown);
            return Marshal.Release(unknown);
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

    private static async Task CheckPageLinkDownloadReferrersAsync(Window window, CancellationToken token)
    {
        await using var server = WebApplication.CreateSlimBuilder().Build();
        server.Urls.Add("http://127.0.0.1:0");
        var requests = System.Threading.Channels.Channel.CreateUnbounded<string>();
        int sourceRequests = 0;
        server.MapGet("/media.mp3", (HttpContext context) =>
        {
            requests.Writer.TryWrite(context.Request.Headers.Referer.ToString());
            return Results.Bytes("ID3sample"u8.ToArray(), "audio/mpeg");
        });
        server.MapGet("/source", (HttpContext context) =>
        {
            Interlocked.Increment(ref sourceRequests);
            var query = context.Request.Query;
            if (query.TryGetValue("header", out var header)) context.Response.Headers["Referrer-Policy"] = header;
            string meta = query.TryGetValue("meta", out var policy) ? $"<meta name='referrer' content='{policy}'>" : "";
            string linkPolicy = query.TryGetValue("link", out policy) ? $" referrerpolicy='{policy}'" : "";
            string destination = query.ContainsKey("cross") ? $"http://localhost:{context.Connection.LocalPort}/media.mp3" : "/media.mp3";
            return Results.Content($"{meta}<a href='{destination}'{linkPolicy}>download</a>", "text/html");
        });
        server.MapGet("/plain", () => Results.Content("<title>Plain</title>", "text/html"));
        await server.StartAsync(token);
        var origin = new Uri(server.Urls.Single());
        var browser = new NativeWebView();
        var editor = TestShell.CreateEditorContextMock();
        editor.Setup(x => x.GetService(typeof(Beutl.Configuration.GlobalConfiguration))).Returns(TestShell.Configuration);
        editor.Setup(x => x.GetService(typeof(Beutl.Media.FontManager))).Returns(TestShell.FontManager);
        editor.Setup(x => x.GetService(typeof(Beutl.Media.Decoding.DecoderRegistry))).Returns(TestShell.Decoders);
        editor.Setup(x => x.GetService(typeof(Beutl.Services.INotificationService))).Returns(TestShell.Notifications);
        using var vm = new WebBrowserTabViewModel(editor.Object, WebBrowserTabViewModel.BlankPage);
        using var downloads = new TemporaryDirectory();
        using var view = new WebBrowserTabView(_ => browser, () => (true, null, false)) { DataContext = vm };
        view.DownloadOptionsSelector = (_, _) =>
            Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(downloads.Path, false));
        window.Content = view;

        async Task Load(Uri page)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded(object? sender, WebViewNavigationCompletedEventArgs e)
            {
                if (e.IsSuccess && e.Request == page) loaded.TrySetResult();
            }
            browser.NavigationCompleted += Loaded;
            try
            {
                vm.Address.Value = page.AbsoluteUri;
                view.NavigateFromAddress();
                await loaded.Task.WaitAsync(token);
            }
            finally { browser.NavigationCompleted -= Loaded; }
            while (await browser.InvokeScript("window.__beutlDownloadsInstalled === true") != "true")
                await Task.Delay(20, token);
        }

        async Task CheckDownload(Uri page, string expected, string description)
        {
            int before = vm.Profile.Downloads.Count;
            await ClickDownloadLinkAsync(browser, token);
            string referrer = await requests.Reader.ReadAsync(token);
            while (vm.Profile.Downloads.Count == before) await Task.Delay(20, token);
            Assert.That(referrer, Is.EqualTo(expected), description);
            Assert.That(vm.Profile.Downloads.Count, Is.EqualTo(before + 1));
            // The link script took the click, so Beutl downloaded the file while the page stayed.
            Assert.That(vm.CurrentUri, Is.EqualTo(page), description);
        }

        async Task Check(string query, string expected, bool removeMeta = false)
        {
            Uri page = new(origin, "/source?" + query);
            await Load(page);
            if (removeMeta)
            {
                await browser.InvokeScript("""
                    const meta = document.createElement('meta');
                    meta.name = 'referrer';
                    meta.content = 'origin';
                    document.head.append(meta);
                    """);
                // Deliver the insertion before removing the element whose policy Chromium keeps.
                await browser.InvokeScript("document.querySelector('meta[name=referrer]').remove()");
            }
            await CheckDownload(page, expected, query + (removeMeta ? "; removed meta origin" : ""));
        }

        await Check("header=no-referrer", "");
        await Check("header=same-origin&cross", "");
        await Check("header=same-origin", origin.AbsoluteUri);
        await Check("header=no-referrer&link=origin", origin.AbsoluteUri);
        await Check("header=origin&meta=no-referrer", "");
        if (OperatingSystem.IsWindows())
        {
            // The macOS link script reads metas present at the click; tracking removed metas is a follow-up.
            await Check("header=no-referrer", origin.AbsoluteUri, removeMeta: true);
        }
        await Check("", origin.AbsoluteUri);

        if (OperatingSystem.IsMacOS())
        {
            await Check("header=no-referrer&header=same-origin", origin.AbsoluteUri);
            await Check("header=no-referrer&header=same-origin&cross", "");

            Uri page = new(origin, "/source?header=no-referrer");
            Uri pushed = new(origin, "/source?header=no-referrer&pushed");
            await Load(page);
            await browser.InvokeScript($"history.pushState(null, '', {JsonSerializer.Serialize(pushed.AbsoluteUri)})");
            int before = Volatile.Read(ref sourceRequests);
            await Load(new Uri(origin, "/plain"));
            Button back = view.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(ToolTip.GetTip(button), Strings.Back));
            Assert.That(back.IsEnabled, Is.True);
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (vm.CurrentUri != pushed || await browser.InvokeScript(
                $"location.href === {JsonSerializer.Serialize(pushed.AbsoluteUri)} && window.__beutlDownloadsInstalled === true") != "true")
                await Task.Delay(20, token);
            Assert.That(Volatile.Read(ref sourceRequests), Is.EqualTo(before), "The source document must be restored from the back-forward cache.");
            await CheckDownload(pushed, "", "back-forward cache restore after pushState");
        }
    }

    private static async Task ClickDownloadLinkAsync(NativeWebView browser, CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            // WebView2 can finish navigation before its new document is ready for input hit testing.
            await browser.InvokeScript("requestAnimationFrame(() => requestAnimationFrame(() => window.downloadLinkReady = true))");
            while (await browser.InvokeScript("window.downloadLinkReady === true") != "true")
                await Task.Delay(20, token);
        }
        using JsonDocument point = JsonDocument.Parse((await browser.InvokeScript("""
            (() => {
                const rect = document.querySelector('a').getBoundingClientRect();
                return {x: rect.x + rect.width / 2, y: rect.y + rect.height / 2};
            })()
            """))!);
        double x = point.RootElement.GetProperty("x").GetDouble();
        double y = point.RootElement.GetProperty("y").GetDouble();
        if (OperatingSystem.IsMacOS())
        {
            var apple = (IAppleWKWebViewPlatformHandle)browser.TryGetPlatformHandle()!;
            nint wkWebView = apple.GetWKWebViewRetained();
            Assert.That(wkWebView, Is.Not.EqualTo(nint.Zero));
            try
            {
                if (!SendBool(wkWebView, Selector("isFlipped")))
                {
                    // x64 returns CGRect through objc_msgSend_stret; arm64 uses objc_msgSend.
                    CGRect bounds;
                    if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                        bounds = SendRect(wkWebView, Selector("bounds"));
                    else
                        SendRectInMemory(out bounds, wkWebView, Selector("bounds"));
                    y = bounds.Height - y;
                }
                CGPoint location = ConvertPoint(wkWebView, Selector("convertPoint:toView:"), new CGPoint(x, y), 0);
                nint number = Send(Send(wkWebView, Selector("window")), Selector("windowNumber"));
                double timestamp = SendDouble(Send(GetClass("NSProcessInfo"), Selector("processInfo")), Selector("systemUptime"));
                nint CreateEvent(ulong type, float pressure) => MouseEvent(GetClass("NSEvent"),
                    Selector("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
                    type, location, 0, timestamp, number, 0, 0, 1, pressure);
                // Sending NSEvents directly produces trusted clicks even in this invisible window.
                SendVoid(wkWebView, Selector("mouseDown:"), CreateEvent(1, 1));
                SendVoid(wkWebView, Selector("mouseUp:"), CreateEvent(2, 0));
            }
            finally { SendVoid(wkWebView, Selector("release")); }
            return;
        }
        var handle = (IWindowsWebView2PlatformHandle)browser.TryGetPlatformHandle()!;
        nint webView = handle.CoreWebView2;
        Assert.That(webView, Is.Not.EqualTo(nint.Zero));
        Marshal.AddRef(webView);
        try
        {
            await DispatchMouseEvent(webView, "mousePressed", x, y).WaitAsync(token);
            await DispatchMouseEvent(webView, "mouseReleased", x, y).WaitAsync(token);
        }
        finally { Marshal.Release(webView); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CGPoint(double X, double Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CGRect(double X, double Y, double Width, double Height);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial CGRect SendRect(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend_stret")]
    private static partial void SendRectInMemory(out CGRect result, nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial CGPoint ConvertPoint(nint receiver, nint selector, CGPoint point, nint view);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint MouseEvent(nint receiver, nint selector, ulong type, CGPoint location, ulong flags,
        double timestamp, nint windowNumber, nint context, nint eventNumber, nint clickCount, float pressure);

    private static unsafe Task DispatchMouseEvent(nint webView, string type, double x, double y)
    {
        var handler = new MouseEventCompletedHandler();
        void* callback = ComInterfaceMarshaller<IDevToolsMethodCompletedHandler>.ConvertToUnmanaged(handler);
        try
        {
            string parameters = JsonSerializer.Serialize(new { type, x, y, button = "left", clickCount = 1 });
            fixed (char* method = "Input.dispatchMouseEvent")
            fixed (char* json = parameters)
            {
                // ICoreWebView2::CallDevToolsProtocolMethod
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, char*, void*, int>)WebView2Vtable.Method(webView, 36))(
                    webView, method, json, callback));
            }
            return handler.Completed.Task;
        }
        finally { ComInterfaceMarshaller<IDevToolsMethodCompletedHandler>.Free(callback); }
    }

    [GeneratedComClass]
    private sealed partial class MouseEventCompletedHandler : IDevToolsMethodCompletedHandler
    {
        internal readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Invoke(int errorCode, nint returnObjectAsJson)
        {
            if (errorCode < 0) Completed.TrySetException(Marshal.GetExceptionForHR(errorCode)!);
            else Completed.TrySetResult();
        }
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
        var editor = TestShell.CreateEditorContextMock();
        editor.Setup(x => x.GetService(typeof(Beutl.Configuration.GlobalConfiguration))).Returns(TestShell.Configuration);
        editor.Setup(x => x.GetService(typeof(Beutl.Media.FontManager))).Returns(TestShell.FontManager);
        editor.Setup(x => x.GetService(typeof(Beutl.Media.Decoding.DecoderRegistry))).Returns(TestShell.Decoders);
        editor.Setup(x => x.GetService(typeof(Beutl.Services.INotificationService))).Returns(TestShell.Notifications);
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
                await Check("window.open(document.querySelector('a').href, 'preview')", origin.AbsoluteUri);
                await Check("window.open(document.querySelector('a').href, 'preview', 'noreferrer')", "");
                await Check("document.querySelector('a').target='preview'; document.querySelector('a').click()", origin.AbsoluteUri);
                await Check("document.querySelector('a').referrerPolicy='no-referrer'; document.querySelector('a').click()", "");
                int beforeFrame = count;
                await browser.InvokeScript("""
                    const frame = document.createElement('iframe');
                    frame.name = 'preview';
                    const frameUrl = new URL('/source', location.href);
                    frameUrl.hostname = 'localhost';
                    frame.src = frameUrl;
                    document.body.append(frame);
                    const link = document.querySelector('a');
                    link.href = new URL('/opened', frameUrl);
                    link.referrerPolicy = 'origin';
                    """);
                while (await browser.InvokeScript("document.querySelector('iframe').contentDocument === null") != "true")
                    await Task.Delay(20, token);
                await browser.InvokeScript("document.querySelector('a').click()");
                Assert.That(await requests.Reader.ReadAsync(token), Is.EqualTo(origin.AbsoluteUri));
                Assert.That(count, Is.EqualTo(beforeFrame), "The named link should navigate the existing iframe");
                await Check("window.open(document.querySelector('a').href, 'otherPreview')", origin.AbsoluteUri);
                await Check("document.querySelector('a').target='otherPreview'; document.querySelector('a').click()", origin.AbsoluteUri);
                await browser.InvokeScript("document.querySelector('a').target='preview'");
                beforeFrame = count;
                await browser.InvokeScript("window.open(document.querySelector('a').href, 'preview')");
                Assert.That(await requests.Reader.ReadAsync(token), Is.EqualTo(origin.AbsoluteUri));
                Assert.That(count, Is.EqualTo(beforeFrame), "The named window.open should navigate the existing iframe");
                await Check("window.open(document.querySelector('a').href, 'otherPreview')", origin.AbsoluteUri);
                await Check("window.open(document.querySelector('a').href, 'preview', 'noopener')", origin.AbsoluteUri);
                await Check("document.querySelector('a').rel='noopener'; document.querySelector('a').click()", origin.AbsoluteUri);
                // A stale iframe decision must not override a new tab's no-referrer policy.
                beforeFrame = count;
                await browser.InvokeScript("document.querySelector('a').rel=''; document.querySelector('a').click()");
                Assert.That(await requests.Reader.ReadAsync(token), Is.EqualTo(origin.AbsoluteUri));
                Assert.That(count, Is.EqualTo(beforeFrame), "The named link should navigate the existing iframe");
                await Check("""
                    document.querySelector('iframe').remove();
                    document.querySelector('a').referrerPolicy = 'no-referrer';
                    document.querySelector('a').click()
                    """, "");
                await browser.InvokeScript("""
                    document.querySelector('a').href = '/opened';
                    document.querySelector('a').target = '_blank';
                    document.querySelector('a').rel = '';
                    document.querySelector('a').removeAttribute('referrerpolicy');
                    """);
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

        await LoadDocument(first, () =>
        {
            if (OperatingSystem.IsWindows())
            {
                // The address path retains this request while Windows starts the adapter on about:blank.
                vm.Address.Value = first.AbsoluteUri;
                view.NavigateFromAddress();
            }
            else webView.Navigate(first);
        });
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

    private static async Task CheckDownloadMethodsAsync(Window window, CancellationToken token)
    {
        await using var server = WebApplication.CreateSlimBuilder().Build();
        server.Urls.Add("http://127.0.0.1:0");
        server.MapGet("/source", () => Results.Content("""
            <form method="post" action="/post"><input name="value" value="sample"><button id="post">POST</button></form>
            <form method="post" action="/redirect-303"><button id="redirect-303">303</button></form>
            <form method="post" action="/redirect-307"><button id="redirect-307">307</button></form>
            <a id="get" href="/get">GET</a>
            <iframe name="sink" hidden></iframe>
            <form method="post" action="/frame-post" target="sink"><button id="frame-post">Frame POST</button></form>
            """, "text/html"));
        server.MapPost("/post", () => Results.Bytes("post"u8.ToArray(), "application/octet-stream", "post.txt"));
        server.MapPost("/redirect-303", (HttpContext context) =>
        {
            context.Response.Headers.Location = "/after-303";
            return Results.StatusCode(StatusCodes.Status303SeeOther);
        });
        server.MapGet("/after-303", () => Results.Bytes("303"u8.ToArray(), "application/octet-stream", "303.txt"));
        server.MapPost("/redirect-307", (HttpContext context) =>
        {
            context.Response.Headers.Location = "/after-307";
            return Results.StatusCode(StatusCodes.Status307TemporaryRedirect);
        });
        server.MapPost("/after-307", () => Results.Bytes("307"u8.ToArray(), "application/octet-stream", "307.txt"));
        server.MapGet("/get", () => Results.Bytes("get"u8.ToArray(), "application/octet-stream", "get.txt"));
        server.MapPost("/frame-post", () => Results.Bytes("frame post"u8.ToArray(), "application/octet-stream", "frame-post.txt"));
        server.MapGet("/policy", (HttpContext context) =>
        {
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Results.Content("<title>Policy</title>", "text/html");
        });
        await server.StartAsync(token);
        int port = new Uri(server.Urls.Single()).Port;
        var browser = new NativeWebView();
        var editor = TestShell.CreateEditorContextMock();
        editor.Setup(x => x.GetService(typeof(Beutl.Configuration.GlobalConfiguration))).Returns(TestShell.Configuration);
        editor.Setup(x => x.GetService(typeof(Beutl.Media.FontManager))).Returns(TestShell.FontManager);
        editor.Setup(x => x.GetService(typeof(Beutl.Services.INotificationService))).Returns(TestShell.Notifications);
        using var vm = new WebBrowserTabViewModel(editor.Object, WebBrowserTabViewModel.BlankPage);
        using var downloads = new TemporaryDirectory();
        using var view = new WebBrowserTabView(_ => browser, () => (true, null, false)) { DataContext = vm };
        view.DownloadOptionsSelector = (_, _) =>
            Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(downloads.Path, false));
        window.Content = view;

        async Task Load(Uri page)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completions = new List<string>();
            void Loaded(object? sender, WebViewNavigationCompletedEventArgs e)
            {
                completions.Add($"{e.Request} ({(e.IsSuccess ? "loaded" : "failed")})");
                if (e.IsSuccess && e.Request == page) loaded.TrySetResult();
            }
            browser.NavigationCompleted += Loaded;
            try
            {
                vm.Address.Value = page.AbsoluteUri;
                view.NavigateFromAddress();
                await loaded.Task.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"{page} did not load. Completed navigations: [{string.Join(", ", completions)}]");
            }
            finally { browser.NavigationCompleted -= Loaded; }
        }

        async Task Check(string host, string id, string path, string? method)
        {
            string label = $"{id} on {host}";
            // Reload so each scripted download is allowed without a multiple-download permission prompt.
            await Load(new Uri($"http://{host}:{port}/source"));
            int before = vm.Profile.Downloads.Count;
            await browser.InvokeScript($"document.getElementById('{id}').click()");
            while (vm.Profile.Downloads.Count == before) await Task.Delay(20, token);
            var record = vm.Profile.Downloads[0];
            Assert.That(new Uri(record.Url).AbsolutePath, Is.EqualTo(path), label);
            Assert.That(record.Method, Is.EqualTo(method), label);
            Assert.That(record.CanRequestAgain, Is.EqualTo(method == null), label);
        }

        await Assert.MultipleAsync(async () =>
        {
            foreach (string host in new[] { "127.0.0.1", InsecureHost })
            {
                await Check(host, "post", "/post", "POST");
                await Check(host, "redirect-303", "/after-303", null);
                await Check(host, "redirect-307", "/after-307", "POST");
                await Check(host, "get", "/get", null);
                await Check(host, "frame-post", "/frame-post", "POST");

                // The same document check gates the Referrer-Policy header that page link downloads follow.
                await Load(new Uri($"http://{host}:{port}/policy"));
                var handler = (WindowsBrowserDownloadHandler)typeof(WebBrowserTabView)
                    .GetField("_nativeBrowserHandler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
                Assert.That(handler.DocumentReferrerPolicy, Is.EqualTo(BrowserReferrerPolicy.NoReferrer), $"policy on {host}");
            }
        });
    }
}

[TestFixture]
public class NativeBrowserFrameTests
{
    [TestCase("--native-browser-download-methods", TestName = "WindowsDownloadHistoryKeepsTheRequestMethod", IncludePlatform = "Win")]
    [TestCase("--native-browser-frames", TestName = "EmbeddedDocumentsLoadWithoutBecomingTopLevelNavigations", IncludePlatform = "MacOSX")]
    [TestCase("--native-browser-history", TestName = "HistoryButtonsSettleAfterSameDocumentNavigation", IncludePlatform = "MacOSX,Win")]
    [TestCase("--native-browser-superseded-navigation", TestName = "ACommittedPageFinishingAfterTheNextNavigationStartsLeavesItLoading", IncludePlatform = "Win,MacOSX")]
    [TestCase("--native-browser-referrers", TestName = "NewTabRequestsHonorLinkAndDocumentReferrerPolicies", IncludePlatform = "Win,Linux")]
    [TestCase("--native-browser-cookie-clear", TestName = "LinuxCookieClearRemovesSessionAndPersistentCookies", IncludePlatform = "Linux")]
    [TestCase("--native-browser-linux-focus", TestName = "LinuxWebKitBlocksCoreKeyPropagationFromEmbeddedX11Window", IncludePlatform = "Linux")]
    [TestCase("--native-browser-download-referrers", TestName = "PageLinkDownloadsHonorTheDocumentReferrerPolicy", IncludePlatform = "Win,MacOSX")]
    [TestCase("--native-browser-adblock-reference", TestName = "WindowsAdBlockBackendKeepsTheAdaptersWebViewReference", IncludePlatform = "Win")]
    public async Task NativeNavigation(string scenario)
    {
        if (OperatingSystem.IsLinux() && (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
            || !WebBrowserTabView.GetWebViewAvailability().IsAvailable))
            Assert.Ignore("Native browser navigation requires an X display and a WebKit runtime.");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        using var webKitHome = scenario == "--native-browser-cookie-clear" ? new TemporaryDirectory() : null;
        if (webKitHome != null)
        {
            start.Environment["XDG_CACHE_HOME"] = Path.Combine(webKitHome.Path, "cache");
            start.Environment["XDG_DATA_HOME"] = Path.Combine(webKitHome.Path, "data");
        }
        // Chromium omits Sec-Fetch-* for http hosts other than localhost, so this scenario also loads pages from one.
        // Either rule alone can miss: a proxy resolves names itself, and a runner may resolve before the resolver rule applies.
        if (OperatingSystem.IsWindows() && scenario == "--native-browser-download-methods")
        {
            string map = $"MAP {NativeBrowserFrameTestHost.InsecureHost} 127.0.0.1";
            start.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] =
                $"--no-proxy-server --host-rules=\"{map}\" --host-resolver-rules=\"{map}\"";
        }
        start.ArgumentList.Add(typeof(NativeBrowserFrameTestHost).Assembly.Location);
        start.ArgumentList.Add(scenario);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(scenario is "--native-browser-referrers" or "--native-browser-download-referrers" or "--native-browser-download-methods" ? 90 : 60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode == NativeBrowserFrameTestHost.SkippedExitCode) Assert.Ignore(await output);
        Assert.That(process.ExitCode, Is.Zero, await output + "\n" + await error);
    }
}
