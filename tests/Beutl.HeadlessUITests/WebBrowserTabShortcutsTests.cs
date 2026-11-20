using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Helpers;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class WebBrowserTabShortcutsTests
{
    [AvaloniaTest]
    public void WebView_is_marked_as_text_input_for_context_commands()
    {
        var page = new Uri("https://example.com/");
        NativeWebView? webView = null;
        using var vm = new WebBrowserTabViewModel(TestShell.CreateEditorContextMock().Object, page);
        using var view = new WebBrowserTabView(uri => webView = new NativeWebView { Source = uri }, () => (true, null, false))
        {
            DataContext = vm
        };
        vm.CompleteNavigation(page, true, false, false);
        var window = new Window { Content = view, Width = 640, Height = 480 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();
            Assert.That(webView, Is.Not.Null);
            webView!.Focus();

            KeyEventArgs args = KeyDown(webView, Key.Space, KeyModifiers.None);
            Assert.That(ContextCommandInput.IsFromTextInput(args), Is.True);

            var fallback = new RecordingCommand();
            new FocusedEditCommand(StandardEditAction.Paste, fallback, window).Execute(null);
            Assert.That(fallback.Runs, Is.Zero);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Alt_F4_is_not_swallowed_by_webview()
    {
        var page = new Uri("https://example.com/");
        NativeWebView? webView = null;
        using var vm = new WebBrowserTabViewModel(TestShell.CreateEditorContextMock().Object, page);
        using var view = new WebBrowserTabView(uri => webView = new NativeWebView { Source = uri }, () => (true, null, false))
        {
            DataContext = vm
        };
        vm.CompleteNavigation(page, true, false, false);
        var window = new Window { Content = view, Width = 640, Height = 480 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();
            Assert.That(webView, Is.Not.Null);
            webView!.Focus();

            KeyEventArgs altF4 = KeyDown(webView, Key.F4, KeyModifiers.Alt);
            webView.RaiseEvent(altF4);
            Assert.That(altF4.Handled, Is.False);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Keys_while_webview_is_focused_do_not_trigger_editor_or_main_shortcuts()
    {
        (Window window, MainView mainView, EditViewModel editor) = await ShowEditorAsync("browser-shortcuts-docked");
        var clock = (IEditorClock)editor.GetService(typeof(IEditorClock))!;
        MediaTime start = MediaTime.FromSeconds(1);
        clock.Seek(start);
        editor.ToggleMarkerAtCurrentTime();
        Assert.That(editor.Scene.Markers, Has.Count.EqualTo(1));

        var page = new Uri("https://example.com/");
        NativeWebView? webView = null;
        using var browserVm = new WebBrowserTabViewModel(editor, page);
        using var browserView = new WebBrowserTabView(uri => webView = new NativeWebView { Source = uri }, () => (true, null, false))
        {
            DataContext = browserVm
        };
        browserVm.CompleteNavigation(page, true, false, false);

        Panel host = mainView.GetVisualDescendants().OfType<EditorHostView>().Single()
            .GetVisualDescendants().OfType<Panel>().First();
        var outside = new Border { Focusable = true };
        host.Children.Add(browserView);
        host.Children.Add(outside);
        try
        {
            HeadlessTestHelpers.Render(2);
            Assert.That(webView, Is.Not.Null);
            webView!.Focus();

            KeyGesture toggleMarker = EditorGesture("editor.toggleMarker");
            KeyGesture undo = MainGesture("main.undo");
            KeyGesture closeTab = MainGesture(MainViewExtension.CloseTabCommandId);
            int toolCount = editor.DockHost.Factory.EnumerateTools().Count();

            foreach (KeyEventArgs key in new[]
            {
                KeyDown(webView, toggleMarker.Key, toggleMarker.KeyModifiers),
                KeyDown(webView, Key.Space, KeyModifiers.None),
                KeyDown(webView, Key.Right, KeyModifiers.None),
                KeyDown(webView, Key.Left, KeyModifiers.None),
                KeyDown(webView, undo.Key, undo.KeyModifiers),
                KeyDown(webView, closeTab.Key, closeTab.KeyModifiers)
            })
            {
                webView.RaiseEvent(key);
                HeadlessTestHelpers.Settle();
                Assert.That(key.Handled, Is.True, $"Expected {key.KeyModifiers}+{key.Key} to be handled inside NativeWebView.");
            }

            Assert.Multiple(() =>
            {
                Assert.That(editor.Scene.Markers, Has.Count.EqualTo(1), "Marker must neither be toggled nor undone.");
                Assert.That(clock.CurrentTime.Value, Is.EqualTo(start), "Playhead must not move.");
                Assert.That(editor.DockHost.Factory.EnumerateTools().Count(), Is.EqualTo(toolCount), "No tool tab may close.");
            });

            // Leaving the web view restores Beutl shortcuts normally.
            outside.Focus();
            outside.RaiseEvent(KeyDown(outside, toggleMarker.Key, toggleMarker.KeyModifiers));
            HeadlessTestHelpers.Settle();
            Assert.That(editor.Scene.Markers, Is.Empty);

            outside.RaiseEvent(KeyDown(outside, undo.Key, undo.KeyModifiers));
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Scene.Markers.Count == 1,
                message: "Undo should still work once focus leaves NativeWebView.");
        }
        finally
        {
            host.Children.Remove(browserView);
            host.Children.Remove(outside);
            await CloseAsync(window, mainView);
        }
    }

    [AvaloniaTest]
    public async Task Keys_in_a_floated_browser_tab_do_not_trigger_editor_or_main_shortcuts()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("browser-shortcuts-floating");
        var mainView = new MainView { DataContext = TestShell.MainViewModel };
        var mainWindow = new Window { Content = mainView, DataContext = TestShell.MainViewModel, Width = 1000, Height = 720 };
        AppHelper.OverrideTopLevel = mainWindow;
        mainWindow.Show();
        HeadlessTestHelpers.Render(2);

        BeutlDockFactory factory = editor.DockHost.Factory;
        BeutlToolDockable tool = factory.EnumerateTools().First();
        factory.SplitToWindow((IDock)tool.Owner!, tool, 20, 20, 500, 400, null);
        HeadlessTestHelpers.Render(2);
        var floating = (Window)factory.FindRoot(tool, _ => true)!.Window!.Host!;
        ToolTabContent toolContent = floating.GetVisualDescendants().OfType<ToolTabContent>()
            .Single(content => ReferenceEquals(content.DataContext, tool));

        var page = new Uri("https://example.com/");
        NativeWebView? webView = null;
        using var browserVm = new WebBrowserTabViewModel(editor, page);
        using var browserView = new WebBrowserTabView(uri => webView = new NativeWebView { Source = uri }, () => (true, null, false))
        {
            DataContext = browserVm
        };
        browserVm.CompleteNavigation(page, true, false, false);
        toolContent.Content = browserView;
        floating.Activate();
        HeadlessTestHelpers.Render(2);

        try
        {
            Assert.That(webView, Is.Not.Null);
            webView!.Focus();
            HeadlessTestHelpers.Settle();

            KeyGesture toggleMarker = EditorGesture("editor.toggleMarker");
            KeyGesture undo = MainGesture("main.undo");
            floating.KeyPress(toggleMarker.Key, (RawInputModifiers)toggleMarker.KeyModifiers, PhysicalKey.None, null);
            HeadlessTestHelpers.Settle();
            Assert.That(editor.Scene.Markers, Is.Empty, "Floating browser web view must not trigger editor shortcuts.");

            editor.ToggleMarkerAtCurrentTime();
            Assert.That(editor.Scene.Markers, Has.Count.EqualTo(1));

            floating.KeyPress(undo.Key, (RawInputModifiers)undo.KeyModifiers, PhysicalKey.None, null);
            HeadlessTestHelpers.Settle();
            Assert.That(editor.Scene.Markers, Has.Count.EqualTo(1), "Floating browser web view must not trigger main shortcuts.");
        }
        finally
        {
            AppHelper.OverrideTopLevel = null;
            if (floating.IsVisible) floating.Close();
            await CloseAsync(mainWindow, mainView);
        }
    }

    private static KeyGesture MainGesture(string id)
        => GetCommandGestureExtension.GetKeyGesture(typeof(MainViewExtension), id)!;

    private static KeyGesture EditorGesture(string id)
        => GetCommandGestureExtension.GetKeyGesture(typeof(SceneEditorExtension), id)!;

    private static KeyEventArgs KeyDown(Interactive source, Key key, KeyModifiers modifiers) => new()
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Source = source,
        Key = key,
        KeyModifiers = modifiers,
        PhysicalKey = PhysicalKey.None
    };

    private static async Task<(Window Window, MainView View, EditViewModel Editor)> ShowEditorAsync(string name)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync(name);
        var view = new MainView { DataContext = TestShell.MainViewModel };
        var window = new Window { Content = view, Width = 1000, Height = 720 };
        window.Show();
        HeadlessTestHelpers.Render(2);
        return (window, view, editor);
    }

    private static async Task CloseAsync(Window window, MainView view)
    {
        view.DataContext = null;
        window.Close();
        HeadlessTestHelpers.Settle();
        await TestReset.ResetShellAsync();
    }

    private sealed class RecordingCommand : ICommand
    {
        public int Runs { get; private set; }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Runs++;
    }
}
