using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Pages;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Dock.Model.Core;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.MacNative;

namespace Beutl.HeadlessUITests;

// The real app's menu bar under real AppKit, which the Headless platform does not have: a key equivalent goes to the
// menu before the focused view, and AppKit decides which key matches which entry. The window stays transparent.
internal static partial class NativeTimelineTestHost
{
    private const ulong Shift = 1UL << 17, Control = 1UL << 18, Option = 1UL << 19, Command = 1UL << 20;

    private static int RunMenuShortcuts(string[] args)
    {
        BeutlHomeIsolation.Begin("beutl-native-menu");
        OpenALPreload.EnsureLoaded();
        return Program.BuildAvaloniaApp()
            .AfterSetup(_ => Dispatcher.UIThread.Post(CheckMenuShortcuts, DispatcherPriority.Send))
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    private static async void CheckMenuShortcuts()
    {
        int exitCode = 1;
        try
        {
            var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            Window window = lifetime.MainWindow!;
            var handle = (IMacOSTopLevelPlatformHandle)window.TryGetPlatformHandle()!;
            SetAlpha(handle.NSWindow, Selector("setAlphaValue:"), 0);
            await App.WaitLoadingExtensions();

            var viewModel = (MainViewModel)window.DataContext!;
            string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, "native-menu");
            Directory.CreateDirectory(location);
            Project project = (await viewModel.ProjectService.CreateProject(640, 480, new Rational(30), 44100, "native-menu", location))!;
            viewModel.EditorService.ActivateTabItem(project.Items.OfType<Scene>().First());
            await Task.Delay(2000);
            var editor = (EditViewModel)viewModel.EditorService.SelectedTabItem.Value!.Context.Value;
            Send(handle.NSWindow, Selector("showWindowMenuWithAppMenu"));
            await Task.Delay(200);
            MainView view = window.GetVisualDescendants().OfType<MainView>().First();
            var root = (Grid)view.Content!;

            AssertKeyEquivalents();
            await CheckTextBoxAsync(window, root, editor);
            await CheckSceneUndoAsync(window, view, editor);
            await CheckTimelineAsync(window, view, editor);
            await CheckFloatingWindowAsync(handle, editor);
            await CheckCloseTabAsync(window, editor);
            await CheckSettingsAsync(window, handle, lifetime);
            await CheckTutorialsAsync(window, view);
            await CheckFullScreenKeyAsync(window);
            await CheckWebViewAsync(window, handle, root);

            Press(window, 46, "m", Command);
            await Task.Delay(800);
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Minimized), "Cmd+M");
            window.WindowState = WindowState.Normal;

            Console.WriteLine("Native menu shortcuts passed.");
            exitCode = 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            // The real app's shutdown waits on services that have nothing to finish here.
            Console.Out.Flush();
            BeutlHomeIsolation.End();
            Environment.Exit(exitCode);
        }
    }

    private static void AssertKeyEquivalents()
    {
        nint menuBar = Send(Send(GetClass("NSApplication"), Selector("sharedApplication")), Selector("mainMenu"));
        Send(menuBar, Selector("update"));
        Dictionary<string, string> keys = [];
        CollectKeyEquivalents(menuBar, keys);

        Assert.Multiple(() =>
        {
            foreach ((string title, string key) in new[]
            {
                (Strings.Settings, "⌘,"), ("Hide Others", "⌥⌘h"), (Strings.CloseTab, "⌘w"), (Strings.CloseProject, "⇧⌘w"),
                (Strings.Undo, "⌘z"), (Strings.Redo, "⇧⌘z"), (Strings.Cut, "⌘x"), (Strings.Copy, "⌘c"), (Strings.Paste, "⌘v"),
                (Strings.SelectAll, "⌘a"), (Strings.ToggleFullScreen, "⌃⌘f"), (Strings.MinimizeWindow, "⌘m"), (Strings.Tutorials, "⇧⌘/"),
            })
            {
                Assert.That(keys.GetValueOrDefault(title), Is.EqualTo(key), title);
            }
        });
    }

    private static void CollectKeyEquivalents(nint menu, Dictionary<string, string> keys)
    {
        nint count = Send(menu, Selector("numberOfItems"));
        for (nint i = 0; i < count; i++)
        {
            nint item = Send(menu, Selector("itemAtIndex:"), i);
            if (ToManaged(Send(item, Selector("keyEquivalent"))) is { Length: > 0 } key)
            {
                long mask = Send(item, Selector("keyEquivalentModifierMask"));
                keys[ToManaged(Send(item, Selector("title")))] = ((mask & (long)Control) != 0 ? "⌃" : "") + ((mask & (long)Option) != 0 ? "⌥" : "")
                    + ((mask & (long)Shift) != 0 ? "⇧" : "") + ((mask & (long)Command) != 0 ? "⌘" : "") + key;
            }

            nint submenu = Send(item, Selector("submenu"));
            if (submenu != 0)
                CollectKeyEquivalents(submenu, keys);
        }
    }

    private static async Task CheckTextBoxAsync(Window window, Grid root, EditViewModel editor)
    {
        var box = new TextBox { Width = 300, Height = 80, AcceptsReturn = true, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ZIndex = 1000 };
        Grid.SetRowSpan(box, 3);
        root.Children.Add(box);
        await Task.Delay(200);
        box.Focus();
        foreach (string text in new[] { "hello", " ", "world" })
            box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = box });
        editor.ToggleMarkerAtCurrentTime();
        int markers = editor.Scene.Markers.Count;

        Assert.That(Press(window, 6, "z", Command), Is.True, "The menu takes Cmd+Z.");
        await Task.Delay(100);
        Assert.That(box.Text, Is.EqualTo("hello "), "Cmd+Z undoes the text box.");
        Assert.That(editor.Scene.Markers.Count, Is.EqualTo(markers), "Cmd+Z leaves the scene alone.");
        Press(window, 6, "Z", Command | Shift);
        await Task.Delay(100);
        Assert.That(box.Text, Is.EqualTo("hello world"), "Shift+Cmd+Z redoes the text box.");
        Press(window, 0, "a", Command);
        await Task.Delay(50);
        Assert.That(box.SelectedText, Is.EqualTo("hello world"), "Cmd+A");

        async Task<int> Caret(ushort keyCode, string chars, ulong flags)
        {
            Press(window, keyCode, chars, flags);
            await Task.Delay(50);
            return box.CaretIndex;
        }

        box.Text = "line one\nline two";
        box.CaretIndex = box.Text.Length;
        Assert.That(await Caret(126, "", Command), Is.Zero, "Cmd+Up");
        Assert.That(await Caret(125, "", Command), Is.EqualTo(17), "Cmd+Down");
        box.CaretIndex = 12;
        Assert.That(await Caret(0, "a", Control), Is.EqualTo(9), "Ctrl+A");
        Assert.That(await Caret(14, "e", Control), Is.EqualTo(17), "Ctrl+E");
        box.CaretIndex = 12;
        await Caret(51, "\u007F", Command);
        Assert.That(box.Text, Is.EqualTo("line one\ne two"), "Cmd+Back");
        box.Text = "line one\nline two";
        box.CaretIndex = 12;
        await Caret(40, "k", Control);
        Assert.That(box.Text, Is.EqualTo("line one\nlin"), "Ctrl+K");
        root.Children.Remove(box);
    }

    private static async Task CheckSceneUndoAsync(Window window, MainView view, EditViewModel editor)
    {
        view.Focus();
        await Task.Delay(50);
        int markers = editor.Scene.Markers.Count;
        Press(window, 6, "z", Command);
        await Task.Delay(300);
        Assert.That(editor.Scene.Markers.Count, Is.EqualTo(markers - 1), "Cmd+Z away from a text box undoes the scene.");
        Press(window, 6, "Z", Command | Shift);
        await Task.Delay(300);
        Assert.That(editor.Scene.Markers.Count, Is.EqualTo(markers), "Shift+Cmd+Z away from a text box redoes the scene.");
    }

    private static async Task CheckTimelineAsync(Window window, MainView view, EditViewModel editor)
    {
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(MediaTime.Zero, MediaTime.FromSeconds(1), 0, new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        await Task.Delay(300);
        Element clip = editor.Scene.Children.First();
        var selection = (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;
        selection.SelectedElements.Clear();
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        window.GetVisualDescendants().OfType<TimelineTabView>().First().Focus();
        await Task.Delay(100);

        Press(window, 0, "a", Command);
        await Task.Delay(100);
        Assert.That(selection.SelectedElements, Has.Count.EqualTo(1), "Cmd+A in the timeline");
        Press(window, 0, "A", Command | Shift);
        await Task.Delay(100);
        Assert.That(selection.SelectedElements, Is.Empty, "Shift+Cmd+A in the timeline");
        Press(window, 24, "=", Command);
        await Task.Delay(100);
        Assert.That(timeline.Options.Value.Scale, Is.EqualTo(TimelineTabViewModel.ZoomStep).Within(1e-4), "Cmd+=");
        Press(window, 27, "-", Command);
        await Task.Delay(100);
        Assert.That(timeline.Options.Value.Scale, Is.EqualTo(1).Within(1e-4), "Cmd+-");
        Press(window, 24, "=", Command);
        await Task.Delay(50);
        Press(window, 29, "0", Command);
        await Task.Delay(100);
        Assert.That(timeline.Options.Value.Scale, Is.EqualTo(1), "Cmd+0");

        // The key of Cut stays with the panel that has the focus.
        selection.SelectedElements.Add(clip);
        view.Focus();
        await Task.Delay(50);
        Press(window, 7, "x", Command);
        await Task.Delay(200);
        Assert.That(editor.Scene.Children, Does.Contain(clip), "Cmd+X outside a panel leaves the clip.");
    }

    // A floated tool's window shows the main window's menu bar, and a key that its tool leaves reaches the editor.
    private static async Task CheckFloatingWindowAsync(IMacOSTopLevelPlatformHandle mainHandle, EditViewModel editor)
    {
        BeutlDockFactory factory = editor.DockHost.Factory;
        BeutlToolDockable tool = factory.EnumerateTools().First(t => t.ToolContext is not TimelineTabViewModel);
        factory.SplitToWindow((IDock)tool.Owner!, tool, 20, 20, 500, 400, null);
        await Task.Delay(500);
        var floating = (Window)factory.FindRoot(tool, _ => true)!.Window!.Host!;
        var handle = (IMacOSTopLevelPlatformHandle)floating.TryGetPlatformHandle()!;
        SetAlpha(handle.NSWindow, Selector("setAlphaValue:"), 0);
        try
        {
            Send(handle.NSWindow, Selector("showWindowMenuWithAppMenu"));
            await Task.Delay(200);
            AssertKeyEquivalents();

            ((Control)floating.GetVisualDescendants().OfType<ToolTabContent>()
                .Single(content => ReferenceEquals(content.DataContext, tool)).Content!).Focus();
            int markers = editor.Scene.Markers.Count;
            Press(floating, 46, "m", 0);
            await Task.Delay(200);
            Assert.That(editor.Scene.Markers.Count, Is.Not.EqualTo(markers), "M in a floating window toggles a marker.");
            Assert.That(Press(floating, 6, "z", Command), Is.True, "The floating window's menu takes Cmd+Z.");
            await Task.Delay(300);
            Assert.That(editor.Scene.Markers.Count, Is.EqualTo(markers), "Cmd+Z in a floating window undoes the scene.");
        }
        finally
        {
            floating.Close();
            Send(mainHandle.NSWindow, Selector("showWindowMenuWithAppMenu"));
            await Task.Delay(200);
        }
    }

    private static async Task CheckCloseTabAsync(Window window, EditViewModel editor)
    {
        BeutlDockFactory factory = editor.DockHost.Factory;
        BeutlToolDockable tool = factory.EnumerateTools().First(t => t.ToolContext is not TimelineTabViewModel);
        factory.SetActiveDockable(tool);
        factory.SetFocusedDockable((IDock)tool.Owner!, tool);
        await Task.Delay(100);
        Press(window, 13, "w", Command);
        await Task.Delay(200);
        Assert.That(factory.EnumerateTools(), Does.Not.Contain(tool), "Cmd+W closes the focused tool tab.");

        IDockable player = BeutlDockFactory.Traverse(editor.DockHost.Layout.Value).First(d => d is PlayerToolDockable);
        factory.SetFocusedDockable((IDock)player.Owner!, player);
        await Task.Delay(100);
        int tools = factory.EnumerateTools().Count();
        Press(window, 13, "w", Command);
        await Task.Delay(200);
        Assert.That((factory.EnumerateTools().Count(), window.IsVisible), Is.EqualTo((tools, true)), "Cmd+W leaves the preview.");
    }

    private static async Task CheckSettingsAsync(Window window, IMacOSTopLevelPlatformHandle handle, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        Assert.That(Press(window, 43, ",", Command), Is.True, "The application menu takes Cmd+,.");
        await Task.Delay(1200);
        SettingsDialog dialog = SettingsDialog.Current!;
        Assert.That(dialog, Is.Not.Null, "Cmd+, opens the settings.");
        var dialogHandle = (IMacOSTopLevelPlatformHandle)dialog.TryGetPlatformHandle()!;
        SetAlpha(dialogHandle.NSWindow, Selector("setAlphaValue:"), 0);
        Press(window, 43, ",", Command);
        await Task.Delay(300);
        Assert.That(lifetime.Windows.OfType<SettingsDialog>().Count(), Is.EqualTo(1), "A second Cmd+, opens no other dialog.");

        // Its window has no File menu, so the key goes to the window as a key.
        Send(dialogHandle.NSWindow, Selector("showWindowMenuWithAppMenu"));
        await Task.Delay(100);
        Assert.That(Press(dialog, 13, "w", Command), Is.False, "No menu takes Cmd+W in the settings.");
        await Task.Delay(500);
        Assert.That(SettingsDialog.Current, Is.Null, "Cmd+W closes the settings.");
        Send(handle.NSWindow, Selector("showWindowMenuWithAppMenu"));
        await Task.Delay(200);
    }

    private static async Task CheckTutorialsAsync(Window window, MainView view)
    {
        view.Focus();
        await Task.Delay(50);
        // AppKit does not match ? to the entry's Shift+/, so this key arrives as a key.
        Press(window, 44, "?", Command | Shift);
        await Task.Delay(500);
        FAContentDialog[] Dialogs() => [.. OverlayLayer.GetOverlayLayer(window)!.GetVisualDescendants().OfType<FAContentDialog>()];
        Assert.That(Dialogs(), Has.Length.EqualTo(1), "Cmd+? opens the tutorials.");
        Press(window, 44, "?", Command | Shift);
        await Task.Delay(300);
        Assert.That(Dialogs(), Has.Length.EqualTo(1), "A second Cmd+? opens no other dialog.");
        Dialogs()[0].Hide();
        await Task.Delay(400);
    }

    // The entry's own command would take the screen for a Space transition.
    private static async Task CheckFullScreenKeyAsync(Window window)
    {
        NativeMenuItem entry = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.View)
            .Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.ToggleFullScreen);
        int runs = 0;
        entry.Command = new ReactiveCommandSlim().WithSubscribe(() => runs++);
        await Task.Delay(100);
        Assert.That(Press(window, 3, "\u0006", "f", Command | Control), Is.True);
        Assert.That(runs, Is.EqualTo(1), "Ctrl+Cmd+F");
    }

    private static async Task CheckWebViewAsync(Window window, IMacOSTopLevelPlatformHandle handle, Grid root)
    {
        // AppKit only sends an action to a responder of the key window.
        if (!SendBoolean(handle.NSWindow, Selector("isKeyWindow")))
        {
            Console.WriteLine("Skipped the web view: another app has the keyboard.");
            return;
        }

        var web = new NativeWebView { Width = 300, Height = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ZIndex = 1000 };
        web.EnvironmentRequested += (_, e) =>
        {
            if (e is AppleWKWebViewEnvironmentRequestedEventArgs apple) apple.NonPersistentDataStore = true;
        };
        Grid.SetRowSpan(web, 3);
        root.Children.Add(web);
        var loaded = new TaskCompletionSource();
        web.NavigationCompleted += (_, _) => loaded.TrySetResult();
        web.NavigateToString("<p>hello web</p><textarea id=t>abc</textarea>", new Uri("https://page.example/"));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Task.Delay(300);
        nint webView = ((IAppleWKWebViewPlatformHandle)web.TryGetPlatformHandle()!).GetWKWebViewRetained();
        SendBoolean(handle.NSWindow, Selector("makeFirstResponder:"), webView);
        await Task.Delay(100);

        Press(window, 0, "a", Command);
        await Task.Delay(500);
        Assert.That(await web.InvokeScript("window.getSelection().toString()"), Does.Contain("hello web"), "Select All in the page");

        // WKWebView has no undo: of its own, which a send to it directly would crash on.
        await web.InvokeScript("document.getElementById('t').focus(); document.execCommand('insertText', false, 'X');");
        await Task.Delay(200);
        SendBoolean(handle.NSWindow, Selector("makeFirstResponder:"), webView);
        await Task.Delay(100);
        Press(window, 6, "z", Command);
        await Task.Delay(500);
        Assert.That(await web.InvokeScript("document.getElementById('t').value"), Does.Contain("abc").And.Not.Contain("X"), "Undo in the page");

        SendBoolean(handle.NSWindow, Selector("makeFirstResponder:"), handle.NSView);
        root.Children.Remove(web);
    }

    private static bool Press(Window window, ushort keyCode, string chars, ulong flags) => Press(window, keyCode, chars, chars, flags);

    // The way NSApplication sends a key: the menu first and then the view. Returns whether the menu took it.
    private static bool Press(Window window, ushort keyCode, string chars, string charsIgnoringModifiers, ulong flags)
    {
        var handle = (IMacOSTopLevelPlatformHandle)window.TryGetPlatformHandle()!;
        nint app = Send(GetClass("NSApplication"), Selector("sharedApplication"));
        nint pool = Send(GetClass("NSAutoreleasePool"), Selector("new"));
        try
        {
            nint keyEvent = KeyEvent(GetClass("NSEvent"),
                Selector("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:"),
                10, default, flags, 0, Send(handle.NSWindow, Selector("windowNumber")), 0, CreateString(chars), CreateString(charsIgnoringModifiers), 0, keyCode);
            // Through the queue, so that it is NSApplication's current event.
            PostEvent(app, Selector("postEvent:atStart:"), keyEvent, 1);
            nint current = NextEvent(app, Selector("nextEventMatchingMask:untilDate:inMode:dequeue:"),
                1UL << 10, Send(GetClass("NSDate"), Selector("distantPast")), CreateString("kCFRunLoopDefaultMode"), 1);
            if (SendBoolean(Send(app, Selector("mainMenu")), Selector("performKeyEquivalent:"), current))
                return true;

            Send(handle.NSView, Selector("keyDown:"), current);
            return false;
        }
        finally
        {
            Send(pool, Selector("drain"));
        }
    }

    private static string ToManaged(nint value) =>
        value == 0 ? "" : Marshal.PtrToStringUTF8(Send(value, Selector("UTF8String"))) ?? "";

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint KeyEvent(
        nint receiver, nint selector, ulong type, NativePoint location, ulong flags, double timestamp, nint windowNumber, nint context,
        nint characters, nint charactersIgnoringModifiers, byte isRepeat, ushort keyCode);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SetAlpha(nint receiver, nint selector, double value);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SendBoolean(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SendBoolean(nint receiver, nint selector, nint argument);
}

[TestFixture]
public class NativeMenuShortcutsTests
{
    [Test]
    [Explicit("Starts the real app, which can take the keyboard from whatever is being typed into for half a minute.")]
    [Platform("MacOSX")]
    public async Task AppKit_gives_the_standard_keys_to_the_menu_bar_and_the_focused_control()
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(NativeTimelineTestHost).Assembly.Location);
        start.ArgumentList.Add("--native-menu-shortcuts");
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        Assert.That(process.ExitCode, Is.Zero, await output + "\n" + await error);
        Assert.That(await output, Does.Contain("Native menu shortcuts passed."));
    }
}
