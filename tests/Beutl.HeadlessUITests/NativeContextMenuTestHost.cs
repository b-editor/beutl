using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls.NativeContextMenu;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Graphics.Shapes;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Dock.Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using static Beutl.HeadlessUITests.MacNative;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.HeadlessUITests;

// Runs native menus for real. AppKit tracks them on the process's main thread against a real window, so this runs
// outside the headless platform and the menus appear on the screen. The checks choose items through AppKit's
// performActionForItemAtIndex: and leave the real pointer alone.
[SupportedOSPlatform("macos")]
internal static partial class NativeContextMenuTestHost
{
    private static string s_output = "";
    private static int s_failures;

    public static int Run(string[] args)
    {
        s_output = args[1];
        Directory.CreateDirectory(s_output);
        BeutlHomeIsolation.Begin("beutl-native-context-menu");
        OpenALPreload.EnsureLoaded();
        try
        {
            return AppBuilder.Configure<TestApp>()
                .UsePlatformDetect()
                .AfterSetup(_ => Dispatcher.UIThread.Post(RunAll, DispatcherPriority.Background))
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            BeutlHomeIsolation.End();
        }
    }

    private static async void RunAll()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        int exitCode = 1;
        try
        {
            NativeContextMenus.Install();
            await CheckBasicMenusAsync();
            await CheckEditorMenusAsync();
            Console.WriteLine($"Native context menus: {s_failures} failed.");
            exitCode = s_failures == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            lifetime.Shutdown(exitCode);
        }
    }

    private sealed class Model
    {
        public string Name { get; set; } = "From the data context";
    }

    private sealed class Command(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }

    private static async Task CheckBasicMenusAsync()
    {
        var log = new List<string>();
        int commands = 0;
        var host = new Border { Background = Brushes.SlateGray, Margin = new Thickness(16), Height = 90, DataContext = new Model() };
        var inner = new Border { Background = Brushes.DarkSlateGray, Margin = new Thickness(40, 16), Height = 40 };
        var liveHost = new Border { Background = Brushes.DimGray, Margin = new Thickness(16), Height = 40 };
        var contextMenuHost = new Border { Background = Brushes.Gray, Margin = new Thickness(16), Height = 40, DataContext = new Model() };
        var customHost = new Border { Background = Brushes.DarkGray, Margin = new Thickness(16), Height = 40 };
        var button = new Button { Content = "Button", Margin = new Thickness(16) };
        var textBox = new TextBox { Text = "Some selected text", Margin = new Thickness(16) };
        host.Child = inner;
        var window = new Window
        {
            Title = "Native context menu check",
            Width = 520,
            Height = 480,
            ShowActivated = false,
            Content = new StackPanel { Children = { host, liveHost, contextMenuHost, customHost, button, textBox } }
        };
        window.Position = PlaceOnLastScreen(window);
        window.Show();
        await WaitAsync(500);

        // Every kind of item, in the order an Avalonia menu runs its events.
        var plain = new FAMenuFlyoutItem
        {
            Text = "Copy",
            InputGesture = new KeyGesture(Key.C, KeyModifiers.Meta),
            IconSource = new FluentIconSource { Icon = Icon.Copy },
            Command = new Command(() => commands++)
        };
        plain.Click += (_, _) => log.Add("Click");
        var boundChild = new FAMenuFlyoutItem { InputGesture = new KeyGesture(Key.Delete) };
        boundChild.Bind(FAMenuFlyoutItem.TextProperty, new Binding(nameof(Model.Name)));
        var toggle = new FAToggleMenuFlyoutItem { Text = "Toggle", IsChecked = true };
        var flyout = new FAMenuFlyout
        {
            Items =
            {
                plain,
                new FAMenuFlyoutItem { Text = "Disabled", IsEnabled = false, IconSource = new FluentIconSource { Icon = Icon.Delete } },
                new FAMenuFlyoutItem { Text = "Hidden", IsVisible = false },
                new FAMenuFlyoutSeparator(),
                toggle,
                new FARadioMenuFlyoutItem { Text = "Radio A", GroupName = "NativeCheck", IsChecked = true },
                new FARadioMenuFlyoutItem { Text = "Radio B", GroupName = "NativeCheck" },
                new FAMenuFlyoutSeparator(),
                new FAMenuFlyoutSubItem
                {
                    Text = "Sub menu",
                    IconSource = new FluentIconSource { Icon = Icon.Add },
                    Items =
                    {
                        boundChild,
                        new FAMenuFlyoutSubItem { Text = "Nested", Items = { new FAMenuFlyoutItem { Text = "Deep" } } }
                    }
                },
                new FAMenuFlyoutSubItem { Text = "Empty sub menu" }
            }
        };
        flyout.Opening += (_, _) => log.Add("Opening");
        flyout.Opened += (_, _) => log.Add("Opened");
        flyout.Closed += (_, _) => log.Add("Closed");
        host.ContextFlyout = flyout;

        MacOSContextMenu menu = await RightClickAsync(window, host, new Point(12, 12));
        await CaptureAsync(window, "01-every-kind");
        string dump = Dump(Open(menu));
        Console.WriteLine(dump);
        Check(flyout.IsOpen, "The flyout counts as open while the native menu is up.");
        Check(((Control)flyout.Popup.Child!).Opacity == 0, "The Avalonia presenter is unseen.");
        Check(flyout.Popup.IsUsingOverlayLayer, "The unseen popup has no window of its own.");
        Check(dump.Contains("From the data context"), "An item in a sub menu has its data context.");
        Check(log.SequenceEqual(["Opening", "Opened"]), $"Opening and Opened ran: {string.Join(", ", log)}");
        await ChooseAsync(menu, 0);
        Check(log.SequenceEqual(["Opening", "Opened", "Click", "Closed"]), $"The events ran in order: {string.Join(", ", log)}");
        Check(commands == 1, "The command ran once.");
        Check(!flyout.IsOpen, "The flyout closed with the native menu.");
        Check(flyout.Popup.Child is { Opacity: 1 }, "The presenter is restored.");

        await CheckPointerAsync(window, host, boundChild);

        // A toggle.
        menu = await RightClickAsync(window, host, new Point(12, 12));
        await ChooseAsync(menu, 4);
        Check(!toggle.IsChecked, "Choosing the toggle unchecks it.");

        // Dismissing.
        log.Clear();
        menu = await RightClickAsync(window, host, new Point(12, 12));
        await DismissAsync(menu);
        Check(log.SequenceEqual(["Opening", "Opened", "Closed"]), $"Dismissing raises no click: {string.Join(", ", log)}");

        // The setting takes effect on the next menu.
        ViewConfig view = GlobalConfiguration.Instance.ViewConfig;
        view.UseNativeContextMenus.Value = false;
        await WaitForHitAsync(window, host, new Point(12, 12));
        RightClick(window, host, new Point(12, 12));
        await WaitAsync(300);
        Check(MacOSContextMenu.Current is null && flyout.IsOpen, "With the setting off, the Avalonia menu opens instead.");
        Check(flyout.Popup.Child is { Opacity: 1 } && !flyout.Popup.IsUsingOverlayLayer, "It is visible, in a window of its own.");
        await CaptureAsync(window, "00-setting-off");
        flyout.Hide();
        view.UseNativeContextMenus.Value = true;
        await WaitAsync(100);
        menu = await RightClickAsync(window, host, new Point(12, 12));
        Check(flyout.Popup.Child is { Opacity: 0 }, "With the setting on again, the native menu is back.");
        await DismissAsync(menu);

        // The deepest flyout wins, as the event stops at the first that shows.
        var innerFlyout = new FAMenuFlyout { Items = { new FAMenuFlyoutItem { Text = "Inner" } } };
        inner.ContextFlyout = innerFlyout;
        log.Clear();
        menu = await RightClickAsync(window, inner, new Point(8, 8));
        Check(Dump(Open(menu)).Contains("Inner") && log.Count == 0, "The inner flyout shows and the outer does not.");
        await DismissAsync(menu);
        inner.ContextFlyout = null;

        // A handler that cancels the opening.
        var canceling = new FAMenuFlyout { Items = { new FAMenuFlyoutItem { Text = "Never" } } };
        canceling.Opening += (_, e) => ((System.ComponentModel.CancelEventArgs)e).Cancel = true;
        liveHost.ContextFlyout = canceling;
        await WaitForHitAsync(window, liveHost, new Point(8, 8));
        RightClick(window, liveHost, new Point(8, 8));
        await WaitAsync(300);
        Check(MacOSContextMenu.Current is null, "A canceled opening shows no native menu.");

        // Changes reach the native menu while it is open, and the dispatcher, timers and async handlers keep running.
        var asyncItem = new FAMenuFlyoutItem { Text = "Before", IsEnabled = false };
        bool? onUiThreadAfterAwait = null;
        asyncItem.Click += async (_, _) =>
        {
            await Task.Delay(10);
            onUiThreadAfterAwait = Dispatcher.UIThread.CheckAccess();
        };
        var live = new FAMenuFlyout { Items = { asyncItem } };
        liveHost.ContextFlyout = live;
        int ticks = 0;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background, (_, _) => ticks++);
        timer.Start();
        menu = await RightClickAsync(window, liveHost, new Point(8, 8));
        nint nativeItem = Send(Open(menu), Selector("itemAtIndex:"), 0);
        Check(!SendBool(nativeItem, Selector("isEnabled")), "The item starts disabled.");
        asyncItem.IsEnabled = true;
        asyncItem.Text = "After";
        Check(SendBool(nativeItem, Selector("isEnabled")), "Enabling the item reaches the open native menu.");
        Check(ToManagedString(Send(nativeItem, Selector("title"))) == "After", "Changing the text reaches the open native menu.");
        int before = ticks;
        await WaitAsync(200);
        Check(ticks > before + 2, $"A background timer keeps ticking while the menu tracks ({ticks - before} in 200 ms).");
        timer.Stop();
        int frames = 0;
        bool counting = true;
        void CountFrame(TimeSpan _)
        {
            frames++;
            if (counting) window.RequestAnimationFrame(CountFrame);
        }
        window.RequestAnimationFrame(CountFrame);
        await WaitAsync(200);
        counting = false;
        Check(frames > 2, $"Avalonia keeps rendering while the menu tracks ({frames} frames in 200 ms).");
        await ChooseAsync(menu, 0);
        await WaitAsync(100);
        Check(onUiThreadAfterAwait == true, "An async click handler continues on the UI thread.");

        // Closing the flyout from code closes the native menu.
        menu = await RightClickAsync(window, liveHost, new Point(8, 8));
        live.Hide();
        await WaitUntilAsync(() => MacOSContextMenu.Current is null, "The native menu closes with the flyout.");
        Check(!live.IsOpen, "Hiding the flyout from code closes the native menu.");

        // A handler that opens its own flyout again keeps the new menu.
        var reopening = new FAMenuFlyout();
        var again = new FAMenuFlyoutItem { Text = "Again" };
        again.Click += (_, _) =>
        {
            reopening.Hide();
            reopening.ShowAt(liveHost, true);
        };
        reopening.Items.Add(again);
        liveHost.ContextFlyout = reopening;
        menu = await RightClickAsync(window, liveHost, new Point(8, 8));
        await ChooseAndAwaitNextAsync(menu, 0);
        Check(reopening.IsOpen && reopening.Popup.Child is { Opacity: 0 }, "A handler can open its own flyout again.");
        await WaitAsync(PostClickWaitMs);
        await DismissAsync(MacOSContextMenu.Current!);
        Check(!reopening.IsOpen, "The menu it opened closes as usual.");

        // ContextMenu, with an item that toggles through a binding.
        var checkItem = new MenuItem { ToggleType = MenuItemToggleType.CheckBox, Header = "_Check me", IsChecked = true };
        var boundItem = new MenuItem();
        boundItem.Bind(HeaderedSelectingItemsControl.HeaderProperty, new Binding(nameof(Model.Name)));
        var contextMenu = new ContextMenu
        {
            Items =
            {
                checkItem,
                new Separator(),
                boundItem,
                new MenuItem { Header = "Sub", Items = { new MenuItem { Header = "Child" } } }
            }
        };
        contextMenuHost.ContextMenu = contextMenu;
        menu = await RightClickAsync(window, contextMenuHost, new Point(8, 8));
        await CaptureAsync(window, "02-context-menu");
        dump = Dump(Open(menu));
        Console.WriteLine(dump);
        Check(contextMenu.IsOpen, "The context menu counts as open.");
        Check(dump.Contains("From the data context"), "A context menu's item has its data context.");
        Check(dump.Contains("Check me"), "The access key marker is gone.");
        await ChooseAsync(menu, 0);
        Check(!checkItem.IsChecked, "Choosing the item toggles it.");
        Check(!contextMenu.IsOpen, "The context menu closed with the native menu.");

        // A menu a native menu cannot show stays an Avalonia one.
        var custom = new MenuFlyout { Items = { new MenuItem { Header = new StackPanel() } } };
        customHost.ContextFlyout = custom;
        await WaitForHitAsync(window, customHost, new Point(8, 8));
        RightClick(window, customHost, new Point(8, 8));
        await WaitAsync(300);
        Check(MacOSContextMenu.Current is null && custom.IsOpen, "The Avalonia menu stays.");
        Check(custom.Popup.Child is { Opacity: 1 }, "It is visible.");
        custom.Hide();

        // Placement, from a flyout shown at a button.
        var buttonFlyout = new FAMenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Items = { new FAMenuFlyoutItem { Text = "Under the button" }, new FAMenuFlyoutItem { Text = "A second item" } }
        };
        button.ContextFlyout = buttonFlyout;
        buttonFlyout.ShowAt(button);
        menu = await WaitForMenuAsync();
        await CaptureAsync(window, "03-placement");
        await DismissAsync(menu);

        // The text box menu, a command bar that fills itself as it opens.
        textBox.Focus();
        textBox.SelectAll();
        await WaitAsync(50);
        string selected = textBox.SelectedText;
        menu = await RightClickAsync(window, textBox, new Point(8, 8));
        await CaptureAsync(window, "04-text-box");
        dump = Dump(Open(menu));
        Console.WriteLine(dump);
        Check(ToManagedString(Send(Send(Open(menu), Selector("itemAtIndex:"), 1), Selector("title"))) == "Copy", "Copy is the second item.");
        Check(SendBool(Send(Open(menu), Selector("itemAtIndex:"), 1), Selector("isEnabled")), "Copy is enabled for a selection.");
        Check(textBox.SelectedText == selected, "The selection is still there while the menu is open.");
        await ChooseAsync(menu, 1);
        await WaitAsync(100);
        string? copied = await TopLevel.GetTopLevel(textBox)!.Clipboard!.TryGetTextAsync();
        Check(copied == selected, $"Copy put the selection on the clipboard: '{copied}'");
        Check(textBox.IsFocused, "The text box has the focus back.");
        Check(textBox.SelectedText == selected, "The selection survived the menu.");

        // A text box in a flyout, whose menu's top level is the flyout's own window.
        var nestedBox = new TextBox { Text = "In a flyout", Width = 200 };
        var parentFlyout = new Flyout { Content = nestedBox };
        parentFlyout.ShowAt(button);
        await WaitAsync(300);
        nestedBox.Focus();
        nestedBox.SelectAll();
        await WaitAsync(50);
        TopLevel popupTop = TopLevel.GetTopLevel(nestedBox)!;
        Console.WriteLine($"The nested text box is in a {popupTop.GetType().Name}.");
        menu = await RightClickAsync(popupTop, nestedBox, new Point(8, 8));
        await CaptureAsync(window, "05-nested");
        Console.WriteLine(Dump(Open(menu)));
        Check(parentFlyout.IsOpen, "The flyout holding the text box stays open under the menu.");
        await ChooseAsync(menu, 1);
        await WaitAsync(100);
        Check(parentFlyout.IsOpen, "The flyout holding the text box stays open after the menu.");
        copied = await TopLevel.GetTopLevel(textBox)!.Clipboard!.TryGetTextAsync();
        Check(copied == "In a flyout", $"Copy works from a flyout: '{copied}'");
        parentFlyout.Hide();
        await WaitAsync(100);

        // The light theme.
        ThemeVariant? theme = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        await WaitAsync(200);
        menu = await RightClickAsync(window, host, new Point(12, 12));
        await CaptureAsync(window, "06-light");
        await DismissAsync(menu);
        Application.Current.RequestedThemeVariant = theme;
        await WaitAsync(100);

        window.Close();
        await WaitAsync(100);
    }

    // Mouse events posted to this process alone, so that the real cursor stays where it is.
    private static async Task CheckPointerAsync(Window window, Control host, FAMenuFlyoutItem subItemChild)
    {
        int clicks = 0;
        subItemChild.Click += (_, _) => clicks++;
        MacOSContextMenu menu = await RightClickAsync(window, host, new Point(12, 12));
        nint app = Send(GetClass("NSApplication"), Selector("sharedApplication"));

        (nint Window, NativeRect Frame) FindMenuWindow(double minX)
        {
            nint windows = Send(app, Selector("windows"));
            nint count = Send(windows, Selector("count"));
            for (nint i = count - 1; i >= 0; i--)
            {
                nint each = Send(windows, Selector("objectAtIndex:"), i);
                NativeRect rect = GetFrame(each);
                if (ToManagedString(Send(Send(each, Selector("className")), Selector("description"))) == "NSPopupMenuWindow" && rect.X > minX)
                    return (each, rect);
            }

            return default;
        }

        void Post(nint target, ulong type, NativePoint location)
        {
            nint mouse = MouseEvent(
                GetClass("NSEvent"),
                Selector("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
                type, location, 0, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, Send(target, Selector("windowNumber")), 0, 0,
                type == 5 ? 0 : 1, type == 1 ? 1 : 0);
            PostEvent(app, Selector("postEvent:atStart:"), mouse, 0);
        }

        (nint first, NativeRect frame) = FindMenuWindow(double.MinValue);
        Console.WriteLine($"The menu window is {frame}");
        string? highlighted = null;
        NativePoint at = default;
        for (double y = frame.Height - 4; y > 0 && highlighted != "Sub menu"; y -= 6)
        {
            at = new NativePoint(40, y);
            Post(first, 5, at);
            await WaitAsync(60);
            highlighted = ToManagedString(Send(Send(Open(menu), Selector("highlightedItem")), Selector("title")));
        }

        Check(highlighted == "Sub menu", $"The pointer highlights an item: {highlighted}");
        await WaitAsync(500);
        await CaptureAsync(window, "07-sub-menu");
        nint subMenu = Send(Send(Open(menu), Selector("highlightedItem")), Selector("submenu"));
        (nint second, frame) = FindMenuWindow(frame.X);
        Check(second != 0, "The sub menu opens when the pointer rests on its item.");
        highlighted = null;
        for (double y = frame.Height - 4; y > 0 && highlighted != "From the data context"; y -= 6)
        {
            at = new NativePoint(40, y);
            Post(second, 5, at);
            await WaitAsync(60);
            highlighted = ToManagedString(Send(Send(subMenu, Selector("highlightedItem")), Selector("title")));
        }

        Check(highlighted == "From the data context", $"The pointer reaches the sub menu's item: {highlighted}");
        Post(second, 1, at);
        Post(second, 2, at);
        await WaitUntilAsync(() => MacOSContextMenu.Current is null, "The menu should close on the click.");
        Check(clicks == 1, "A click on the sub menu's item reaches the item.");
    }

    private static async Task CheckEditorMenusAsync()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("native-context-menus", duration: MediaTime.FromSeconds(30));
        await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.Zero, MediaTime.FromSeconds(4));
        var window = new Window
        {
            Title = "Native context menu check",
            Width = 1100,
            Height = 720,
            ShowActivated = false,
            ExtendClientAreaToDecorationsHint = true,
            Content = new EditView { DataContext = editor }
        };
        window.Position = PlaceOnLastScreen(window);
        window.Show();

        // A clip can run far past the window's edge.
        ElementView clip = null!;
        await WaitUntilAsync(() => (clip = window.GetVisualDescendants().OfType<ElementView>().FirstOrDefault()!) != null, "The clip should inflate.");
        var clipPoint = new Point(60, clip.Bounds.Height / 2);
        await WaitForHitAsync(window, clip, clipPoint);
        for (int i = 0; i < 4; i++)
        {
            long opened = Stopwatch.GetTimestamp();
            RightClick(window, clip, clipPoint);
            await WaitUntilAsync(() => MacOSContextMenu.Current is { IsTracking: true }, "The native menu should open.");
            Console.WriteLine($"The clip menu, right button up to tracking: {Stopwatch.GetElapsedTime(opened).TotalMilliseconds:F0} ms");
            await WaitAsync(300);
            await DismissAsync(MacOSContextMenu.Current!);
        }

        await ReviewMenuAsync(window, clip, clipPoint, "10-clip");

        // Items of the real menu that act on the clip.
        var border = clip.FindControl<Border>("border")!;
        var clipFlyout = (FAMenuFlyout)border.ContextFlyout!;
        var clipModel = (Beutl.Editor.Components.TimelineTab.ViewModels.ElementViewModel)clip.DataContext!;
        bool wasEnabled = clipModel.Model.IsEnabled.Value;
        MacOSContextMenu menu = await RightClickAsync(window, clip, clipPoint);
        await ChooseAsync(menu, clipFlyout.Items.IndexOf(clip.FindControl<FAToggleMenuFlyoutItem>("enableElement")));
        await WaitAsync(100);
        Check(clipModel.Model.IsEnabled.Value != wasEnabled, "The clip's toggle turned the clip off.");
        menu = await RightClickAsync(window, clip, clipPoint);
        await ChooseAsync(menu, clipFlyout.Items.IndexOf(clip.FindControl<FAToggleMenuFlyoutItem>("enableElement")));
        await WaitAsync(100);
        Check(clipModel.Model.IsEnabled.Value == wasEnabled, "The clip's toggle turned the clip back on.");
        menu = await RightClickAsync(window, clip, clipPoint);
        await ChooseAsync(menu, clipFlyout.Items.IndexOf(clip.FindControl<FAMenuFlyoutItem>("rename")));
        await WaitAsync(200);
        TextBox renameBox = clip.FindControl<TextBox>("textBox")!;
        Check(renameBox.IsFocused, "Rename puts the focus in the clip's name box.");
        await CaptureAsync(window, "10-clip-rename");
        border.Focus();
        await WaitAsync(100);

        TimelineTabView timeline = window.GetVisualDescendants().OfType<TimelineTabView>().First();
        Control panel = timeline.FindControl<Panel>("TimelinePanel")!;
        await ReviewMenuAsync(window, panel, new Point(Math.Min(panel.Bounds.Width - 20, 700), Math.Min(panel.Bounds.Height - 8, 120)), "11-timeline");

        ToolTabStripItem tab = window.GetVisualDescendants().OfType<ToolTabStripItem>().First();
        await ReviewMenuAsync(window, tab, new Point(8, tab.Bounds.Height / 2), "12-tab");

        window.Content = null;
        window.Close();
        await WaitAsync(100);
    }

    // Far from where people tend to work, on a screen with room for the menus.
    private static PixelPoint PlaceOnLastScreen(Window window)
    {
        Screen screen = window.Screens.All[^1];
        PixelRect area = screen.WorkingArea;
        int pad = (int)(40 * screen.Scaling);
        int width = (int)(window.Width * screen.Scaling);
        int height = (int)(window.Height * screen.Scaling);
        return new PixelPoint(Math.Max(area.X, area.Right - width - pad), Math.Max(area.Y, area.Bottom - height - pad));
    }

    private const int PostClickWaitMs = 200;

    private static async Task<MacOSContextMenu> RightClickAsync(TopLevel window, Control target, Point position)
    {
        // A click anywhere on the desktop closes a menu, and people go on working during this run. Whether the menu
        // closed for that shows in the log.
        for (int attempt = 0; ; attempt++)
        {
            await WaitForHitAsync(window, target, position);
            RightClick(window, target, position);
            await WaitUntilAsync(() => MacOSContextMenu.Current is { IsTracking: true }, "The native menu should open.");
            MacOSContextMenu menu = MacOSContextMenu.Current!;
            await WaitAsync(PostClickWaitMs);
            if (ReferenceEquals(MacOSContextMenu.Current, menu))
                return menu;

            nint app = Send(GetClass("NSApplication"), Selector("sharedApplication"));
            Console.WriteLine($"The menu closed by itself: active {SendBool(app, Selector("isActive"))}, pressed buttons {Send(GetClass("NSEvent"), Selector("pressedMouseButtons"))}");
            if (attempt == 2)
                throw new InvalidOperationException("The native menu keeps closing by itself.");
        }
    }

    // Captures and dumps a menu, again if something on the desktop closes it meanwhile.
    private static async Task ReviewMenuAsync(TopLevel window, Control target, Point position, string name)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            MacOSContextMenu menu = await RightClickAsync(window, target, position);
            await CaptureAsync((Window)window, name);
            if (ReferenceEquals(MacOSContextMenu.Current, menu))
            {
                await WriteDumpAsync(menu, name);
                await DismissAsync(menu);
                return;
            }

            Console.WriteLine($"The {name} menu closed during its capture.");
        }

        throw new InvalidOperationException($"The {name} menu keeps closing by itself.");
    }

    // A click needs a rendered frame to hit anything.
    private static Task WaitForHitAsync(TopLevel window, Control target, Point position) => WaitUntilAsync(() =>
        target.TranslatePoint(position, window) is { } at
        && window.InputHitTest(at) is Visual hit
        && (ReferenceEquals(hit, target) || target.IsVisualAncestorOf(hit)), "The control should be on the screen.");

    private static async Task<MacOSContextMenu> WaitForMenuAsync()
    {
        await WaitUntilAsync(() => MacOSContextMenu.Current is { IsTracking: true }, "The native menu should open.");
        await WaitAsync(PostClickWaitMs);
        return MacOSContextMenu.Current!;
    }

    // A menu that closed has been released, and reading it would crash.
    private static nint Open(MacOSContextMenu menu) => ReferenceEquals(MacOSContextMenu.Current, menu)
        ? menu.Handle
        : throw new InvalidOperationException("The native menu closed by itself.");

    private static async Task ChooseAsync(MacOSContextMenu menu, int index)
    {
        SendVoid(Open(menu), Selector("performActionForItemAtIndex:"), index);
        await DismissAsync(menu);
    }

    // For an item whose handler opens another menu, where choosing ends with that one tracking.
    private static async Task ChooseAndAwaitNextAsync(MacOSContextMenu menu, int index)
    {
        SendVoid(Open(menu), Selector("performActionForItemAtIndex:"), index);
        SendVoid(Open(menu), Selector("cancelTracking"));
        await WaitUntilAsync(
            () => MacOSContextMenu.Current is { IsTracking: true } current && !ReferenceEquals(current, menu),
            "The next native menu should open.");
    }

    private static async Task DismissAsync(MacOSContextMenu menu)
    {
        SendVoid(Open(menu), Selector("cancelTracking"));
        await WaitUntilAsync(() => MacOSContextMenu.Current is null, "The native menu should close.");
    }

    // The menu opens at the position Avalonia saw, wherever the real pointer is.
    private static void RightClick(TopLevel window, Control target, Point position)
    {
        var handle = (IMacOSTopLevelPlatformHandle)window.TryGetPlatformHandle()!;
        Point client = target.TranslatePoint(position, window)!.Value;
        nint pool = Send(GetClass("NSAutoreleasePool"), Selector("new"));
        NativePoint inWindow = ConvertPoint(handle.NSView, Selector("convertPoint:toView:"), new NativePoint(client.X, client.Y), 0);
        nint number = Send(handle.NSWindow, Selector("windowNumber"));
        double timestamp = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        nint CreateEvent(ulong type, float pressure) => MouseEvent(
            GetClass("NSEvent"),
            Selector("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
            type, inWindow, 0, timestamp, number, 0, 0, 1, pressure);
        Send(handle.NSView, Selector("rightMouseDown:"), CreateEvent(3, 1));
        Send(handle.NSView, Selector("rightMouseUp:"), CreateEvent(4, 0));
        Send(pool, Selector("drain"));
    }

    private static async Task CaptureAsync(Window window, string name)
    {
        PixelPoint from = window.PointToScreen(new Point(-16, -16));
        PixelPoint to = window.PointToScreen(new Point(window.ClientSize.Width + 16, window.ClientSize.Height + 16));
        string path = Path.Combine(s_output, name + ".png");
        using Process process = Process.Start("screencapture", ["-x", $"-R{from.X},{from.Y},{to.X - from.X},{to.Y - from.Y}", path]);
        await process.WaitForExitAsync();
        Console.WriteLine($"{name}: {(File.Exists(path) ? "captured" : "NOT captured")}");
    }

    private static async Task WriteDumpAsync(MacOSContextMenu menu, string name)
    {
        string dump = Dump(Open(menu));
        Console.WriteLine($"--- {name}{Environment.NewLine}{dump}");
        await File.WriteAllTextAsync(Path.Combine(s_output, name + ".txt"), dump);
    }

    private static string Dump(nint menu)
    {
        var builder = new StringBuilder();
        Append(menu, 0);
        return builder.ToString();

        void Append(nint current, int depth)
        {
            nint count = Send(current, Selector("numberOfItems"));
            for (nint i = 0; i < count; i++)
            {
                nint item = Send(current, Selector("itemAtIndex:"), i);
                builder.Append(' ', depth * 2);
                if (SendBool(item, Selector("isSeparatorItem")))
                {
                    builder.Append("---");
                }
                else
                {
                    builder.Append(ToManagedString(Send(item, Selector("title"))));
                    string key = ToManagedString(Send(item, Selector("keyEquivalent"))) ?? "";
                    if (key.Length > 0)
                        builder.Append($"  [key {string.Join("+", key.Select(c => ((int)c).ToString("X")))} mask {Send(item, Selector("keyEquivalentModifierMask")):X}]");
                    if (!SendBool(item, Selector("isEnabled"))) builder.Append("  (disabled)");
                    if (Send(item, Selector("state")) != 0) builder.Append("  (checked)");
                    if (Send(item, Selector("image")) != 0) builder.Append("  (icon)");
                }

                if (SendBool(item, Selector("isHidden"))) builder.Append("  (hidden)");
                builder.AppendLine();
                if (SendBool(item, Selector("hasSubmenu")))
                    Append(Send(item, Selector("submenu")), depth + 1);
            }
        }
    }

    private static void Check(bool condition, string message)
    {
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {message}");
        if (!condition) s_failures++;
    }

    private static Task WaitAsync(int milliseconds) => Task.Delay(milliseconds);

    private static async Task WaitUntilAsync(Func<bool> condition, string message)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException(message);
            await Task.Delay(1);
        }
    }

    // A struct this large comes back in memory on x64, through another entry point.
    private static NativeRect GetFrame(nint window)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return SendRect(window, Selector("frame"));

        SendRectInMemory(out NativeRect frame, window, Selector("frame"));
        return frame;
    }

    private static string? ToManagedString(nint value) =>
        value == 0 ? null : Marshal.PtrToStringUTF8(Send(value, Selector("UTF8String")));

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(double X, double Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRect(double X, double Y, double Width, double Height);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial NativeRect SendRect(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend_stret")]
    private static partial void SendRectInMemory(out NativeRect result, nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void PostEvent(nint receiver, nint selector, nint nativeEvent, byte atStart);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial NativePoint ConvertPoint(nint receiver, nint selector, NativePoint point, nint view);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint MouseEvent(
        nint receiver, nint selector, ulong type, NativePoint location, ulong flags, double timestamp, nint windowNumber,
        nint context, nint eventNumber, nint clickCount, float pressure);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SendBool(nint receiver, nint selector);
}

[TestFixture]
public class NativeContextMenuIntegrationTests
{
    [Test]
    [Explicit("Opens real menus on the screen for several seconds.")]
    [Platform("MacOSX")]
    public async Task Native_menus_stand_in_for_avalonia_menus()
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(NativeTimelineTestHost).Assembly.Location);
        start.ArgumentList.Add("--native-context-menu");
        start.ArgumentList.Add(TestContext.CurrentContext.WorkDirectory);
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
    }
}
