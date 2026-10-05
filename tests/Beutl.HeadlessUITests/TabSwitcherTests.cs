using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TabSwitcherTests
{
    [AvaloniaTest]
    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(3, false)]
    [TestCase(3, true)]
    public async Task Ctrl_tab_initially_selects_and_focuses_a_tool_tab(int documentCount, bool reverse)
    {
        await using var session = await Session.CreateAsync($"initial-tool-{documentCount}-{reverse}", documentCount: documentCount);
        EditorTabItem document = TestShell.Editor.SelectedTabItem.Value!;
        session.Press(Key.Tab, RawInputModifiers.Control | (reverse ? RawInputModifiers.Shift : RawInputModifiers.None));
        HeadlessTestHelpers.Render(2);
        ListBox tools = session.Overlay.FindControl<ListBox>("ToolsList")!;
        ListBox files = session.Overlay.FindControl<ListBox>("DocumentsList")!;
        TabSwitcherItem selected = session.Switcher.SelectedItem!;
        var container = (ListBoxItem)tools.ContainerFromItem(selected)!;
        Assert.Multiple(() =>
        {
            Assert.That(session.Switcher.SelectedGroup.Value, Is.EqualTo(TabSwitcherGroup.Tools));
            Assert.That(selected.Tool, Is.Not.Null);
            Assert.That(selected, Is.SameAs(reverse ? session.Switcher.Tools[^1] : session.Switcher.Tools[1]));
            Assert.That(container.IsSelected, Is.True);
            Assert.That(tools.IsKeyboardFocusWithin, Is.True);
            Assert.That(files.SelectedItem, Is.Null);
            Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(document));
        });
    }

    [AvaloniaTest]
    public async Task File_list_keeps_a_stable_MRU_snapshot_and_commits_only_on_control_release()
    {
        await using var session = await Session.CreateAsync("mru");
        EditorTabItem current = TestShell.Editor.SelectedTabItem.Value!;
        session.Press(Key.Tab, RawInputModifiers.Control);
        session.Press(Key.Right, RawInputModifiers.Control);
        session.Press(Key.Tab, RawInputModifiers.Control);
        Assert.Multiple(() =>
        {
            Assert.That(session.Switcher.IsOpen.Value, Is.True);
            Assert.That(session.Switcher.SelectedItem!.Document, Is.SameAs(session.Documents[1]));
            Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(current));
        });

        var snapshot = session.Switcher.Documents.ToArray();
        session.Press(Key.Tab, RawInputModifiers.Control);
        Assert.That(session.Switcher.SelectedItem!.Document, Is.SameAs(session.Documents[0]));
        session.Press(Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.That(session.Switcher.Documents, Is.EqualTo(snapshot));
        session.Release(Key.LeftCtrl);

        Assert.Multiple(() =>
        {
            Assert.That(session.Switcher.IsOpen.Value, Is.False);
            Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(session.Documents[1]));
        });

        session.Press(Key.Tab, RawInputModifiers.Control);
        session.Press(Key.Right, RawInputModifiers.Control);
        session.Press(Key.Tab, RawInputModifiers.Control);
        session.Release(Key.RightCtrl);
        Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(current));
    }

    [AvaloniaTest]
    public async Task Reverse_navigation_wraps_and_releasing_shift_does_not_commit()
    {
        await using var session = await Session.CreateAsync("reverse");
        session.Press(Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.That(session.Switcher.SelectedItem, Is.SameAs(session.Switcher.Tools[^1]));
        session.Press(Key.Tab, RawInputModifiers.Control);
        Assert.That(session.Switcher.SelectedItem, Is.SameAs(session.Switcher.Tools[0]));
        session.Release(Key.LeftShift, RawInputModifiers.Control);
        Assert.That(session.Switcher.IsOpen.Value, Is.True);
        IDockable selected = session.Switcher.SelectedItem!.Tool!;
        session.Release(Key.LeftCtrl);
        Assert.That(((IDock)selected.Owner!).ActiveDockable, Is.SameAs(selected));
    }

    [AvaloniaTest]
    [TestCase(Key.Tab)]
    [TestCase(Key.F7)]
    public async Task Alt_navigation_switches_tool_tabs_and_preserves_the_active_file(Key shortcut)
    {
        await using var session = await Session.CreateAsync("tools-" + shortcut);
        EditorTabItem current = TestShell.Editor.SelectedTabItem.Value!;
        var factory = session.Editor.DockHost.Factory;
        BeutlToolDockable timeline = factory.EnumerateTools().Single(tool => tool.ToolContext.Extension == TimelineTabExtension.Instance);
        factory.SetActiveDockable(timeline);
        factory.SetFocusedDockable(session.Editor.DockHost.Layout.Value, timeline);
        session.Input.Focus();

        session.Press(shortcut, RawInputModifiers.Alt);
        TabSwitcherItem selected = session.Switcher.SelectedItem!;
        Assert.Multiple(() =>
        {
            Assert.That(session.Switcher.SelectedGroup.Value, Is.EqualTo(TabSwitcherGroup.Tools));
            Assert.That(selected.Tool, Is.Not.Null.And.Not.SameAs(timeline));
            Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(current));
        });
        session.Release(Key.LeftAlt);
        Assert.Multiple(() =>
        {
            Assert.That(((IDock)selected.Tool!.Owner!).ActiveDockable, Is.SameAs(selected.Tool));
            Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(current));
            Assert.That(session.Switcher.IsOpen.Value, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Escape_restores_text_input_focus_and_leaves_tabs_unchanged()
    {
        await using var session = await Session.CreateAsync("cancel");
        EditorTabItem current = TestShell.Editor.SelectedTabItem.Value!;
        session.Press(Key.Tab, RawInputModifiers.Control);
        session.Press(Key.Escape, RawInputModifiers.Control);
        session.Release(Key.LeftCtrl);
        Assert.Multiple(() =>
        {
            Assert.That(session.Switcher.IsOpen.Value, Is.False);
            Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(current));
            Assert.That(session.Window.FocusManager!.GetFocusedElement(), Is.SameAs(session.Input));
        });
        session.Window.KeyTextInput("still editing");
        Assert.That(session.Input.Text, Is.EqualTo("still editing"));
    }

    [AvaloniaTest]
    public async Task Plain_tab_retains_focus_traversal_and_ctrl_tab_runs_before_child_handlers()
    {
        await using var session = await Session.CreateAsync("input");
        session.Press(Key.Tab);
        Assert.That(session.Switcher.IsOpen.Value, Is.False);
        Assert.That(session.Window.FocusManager!.GetFocusedElement(), Is.Not.SameAs(session.Input));
        session.Input.Focus();
        bool childHandledTab = false;
        session.Input.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Tab)
            {
                childHandledTab = true;
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        session.Press(Key.Tab, RawInputModifiers.Control);
        Assert.Multiple(() =>
        {
            Assert.That(session.Switcher.IsOpen.Value, Is.True);
            Assert.That(childHandledTab, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Create_picker_opens_a_tool_in_the_original_dock_without_duplicating_singletons()
    {
        await using var session = await Session.CreateAsync("create");
        var factory = session.Editor.DockHost.Factory;
        BeutlToolDockable timeline = factory.EnumerateTools().Single(tool => tool.ToolContext.Extension == TimelineTabExtension.Instance);
        factory.SetFocusedDockable(session.Editor.DockHost.Layout.Value, timeline);
        IDock target = (IDock)timeline.Owner!;
        RawInputModifiers modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        session.Press(Key.T, modifier);
        session.Release(OperatingSystem.IsMacOS() ? Key.LWin : Key.LeftCtrl);
        Assert.Multiple(() =>
        {
            Assert.That(session.Switcher.IsOpen.Value, Is.True, "Ctrl+T is a picker that stays open until confirmed.");
            Assert.That(session.Switcher.SelectedGroup.Value, Is.EqualTo(TabSwitcherGroup.NewTools));
            Assert.That(session.Switcher.NewTools.Any(item => item.Extension == TimelineTabExtension.Instance), Is.False);
        });
        int historyIndex = session.Switcher.NewTools.ToList().FindIndex(item => item.Extension == HistoryTabExtension.Instance);
        Assert.That(historyIndex, Is.GreaterThanOrEqualTo(0));
        session.Switcher.Select(TabSwitcherGroup.NewTools, historyIndex);
        session.Press(Key.Enter);
        var added = factory.EnumerateTools().Single(tool => tool.ToolContext.Extension == HistoryTabExtension.Instance);
        Assert.Multiple(() =>
        {
            Assert.That(added.Owner, Is.SameAs(target));
            Assert.That(target.ActiveDockable, Is.SameAs(added));
            Assert.That(session.Switcher.IsOpen.Value, Is.False);
        });

        session.Input.Focus();
        session.Press(Key.T, modifier);
        Assert.That(session.Switcher.NewTools.Any(item => item.Extension == HistoryTabExtension.Instance), Is.False);
    }

    [AvaloniaTest]
    public async Task Arrow_keys_switch_between_tool_and_file_lists_and_keep_the_previous_selection()
    {
        await using var session = await Session.CreateAsync("two-lists");
        session.Press(Key.Tab, RawInputModifiers.Control);
        session.Press(Key.Right, RawInputModifiers.Control);
        TabSwitcherItem document = session.Switcher.SelectedItem!;
        session.Press(Key.Left, RawInputModifiers.Control);
        Assert.That(session.Switcher.SelectedGroup.Value, Is.EqualTo(TabSwitcherGroup.Tools));
        TabSwitcherItem tool = session.Switcher.SelectedItem!;
        session.Press(Key.Left, RawInputModifiers.Control);
        Assert.That(session.Switcher.SelectedItem, Is.SameAs(tool), "Left at the left list must not wrap to files.");
        session.Press(Key.Right, RawInputModifiers.Control);
        Assert.That(session.Switcher.SelectedItem, Is.SameAs(document));
        session.Press(Key.Right, RawInputModifiers.Control);
        Assert.Multiple(() =>
        {
            Assert.That(session.Switcher.SelectedItem, Is.SameAs(document));
            Assert.That(session.Switcher.SelectedGroup.Value, Is.EqualTo(TabSwitcherGroup.Documents));
            Assert.That(session.Switcher.IsCreating.Value, Is.False);
            Assert.That(session.Overlay.FindControl<ListBox>("NewToolsList")!.IsEffectivelyVisible, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Create_button_opens_a_separate_picker_that_waits_for_confirmation()
    {
        await using var session = await Session.CreateAsync("create-button");
        session.Press(Key.Tab, RawInputModifiers.Control);
        int count = session.Editor.DockHost.Factory.EnumerateTools().Count();
        session.Overlay.FindControl<Button>("NewTabButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Settle();
        session.Release(Key.LeftCtrl);
        Assert.Multiple(() =>
        {
            Assert.That(session.Switcher.IsOpen.Value, Is.True);
            Assert.That(session.Switcher.IsCreating.Value, Is.True);
            Assert.That(session.Editor.DockHost.Factory.EnumerateTools().Count(), Is.EqualTo(count));
        });
        int index = session.Switcher.NewTools.ToList().FindIndex(item => item.Extension == HistoryTabExtension.Instance);
        session.Switcher.Select(TabSwitcherGroup.NewTools, index);
        session.Press(Key.Enter);
        Assert.That(session.Editor.DockHost.Factory.EnumerateTools().Count(), Is.EqualTo(count + 1));
        Assert.That(session.Editor.DockHost.Factory.IsToolTabOpen(HistoryTabExtension.Instance), Is.True);
    }

    [AvaloniaTest]
    public async Task A_single_file_remains_available_and_closed_tools_are_not_reactivated()
    {
        await using var session = await Session.CreateAsync("single", documentCount: 1);
        session.Press(Key.Tab, RawInputModifiers.Control);
        Assert.That(session.Switcher.SelectedGroup.Value, Is.EqualTo(TabSwitcherGroup.Tools));
        session.Press(Key.Right, RawInputModifiers.Control);
        Assert.Multiple(() =>
        {
            Assert.That(session.Switcher.SelectedGroup.Value, Is.EqualTo(TabSwitcherGroup.Documents));
            Assert.That(session.Switcher.SelectedItem!.Document, Is.SameAs(session.Documents[0]));
        });
        session.Press(Key.Left, RawInputModifiers.Control);
        Assert.That(session.Switcher.SelectedGroup.Value, Is.EqualTo(TabSwitcherGroup.Tools));
        BeutlToolDockable? selected = session.Switcher.SelectedItem?.Tool as BeutlToolDockable;
        if (selected is null)
        {
            session.Press(Key.Tab, RawInputModifiers.Control);
            selected = (BeutlToolDockable)session.Switcher.SelectedItem!.Tool!;
        }
        var factory = session.Editor.DockHost.Factory;
        factory.CloseDockable(selected);
        session.Release(Key.LeftCtrl);
        Assert.Multiple(() =>
        {
            Assert.That(factory.EnumerateTools(), Does.Not.Contain(selected));
            Assert.That(session.Switcher.IsOpen.Value, Is.False);
            Assert.That(session.Window.FocusManager!.GetFocusedElement(), Is.SameAs(session.Input));
        });
    }

    [AvaloniaTest]
    public async Task Project_transitions_and_command_palette_prevent_tab_navigation()
    {
        await using var session = await Session.CreateAsync("lifecycle");
        session.Press(Key.Tab, RawInputModifiers.Control);
        using (TestShell.Editor.BeginLifecycleActivity(ProjectLifecycleActivity.ClosingProject))
        {
            Assert.That(session.Switcher.IsOpen.Value, Is.False);
            session.Input.Focus();
            session.Press(Key.Tab, RawInputModifiers.Control);
            Assert.That(session.Switcher.IsOpen.Value, Is.False);
        }
        TestShell.MainViewModel.CommandPalette.Toggle();
        HeadlessTestHelpers.Settle();
        session.Press(Key.Tab, RawInputModifiers.Control);
        Assert.That(session.Switcher.IsOpen.Value, Is.False);
        TestShell.MainViewModel.CommandPalette.Close();
    }

    [AvaloniaTest]
    public async Task Remapped_shortcuts_replace_the_defaults()
    {
        await using var session = await Session.CreateAsync("remap");
        var manager = TestShell.MainViewModel.ContextCommandManager!;
        var entry = manager.GetDefinitions<MainViewExtension>().Single(command => command.Definition.Name == MainViewExtension.NextTabCommandName);
        OSPlatform platform = OperatingSystem.IsMacOS() ? OSPlatform.OSX : OperatingSystem.IsWindows() ? OSPlatform.Windows : OSPlatform.Linux;
        KeyGesture? original = entry.KeyGestures.First(gesture => gesture.Platform == platform).KeyGesture;
        try
        {
            manager.ChangeKeyGesture(entry, new(Key.J, KeyModifiers.Control), platform);
            session.Press(Key.Tab, RawInputModifiers.Control);
            Assert.That(session.Switcher.IsOpen.Value, Is.False);
            session.Input.Focus();
            session.Press(Key.J, RawInputModifiers.Control);
            Assert.That(session.Switcher.IsOpen.Value, Is.True);
            session.Press(Key.J, RawInputModifiers.Control);
            IDockable selected = session.Switcher.SelectedItem!.Tool!;
            Assert.That(session.Switcher.SelectedGroup.Value, Is.EqualTo(TabSwitcherGroup.Tools));
            session.Release(Key.LeftCtrl);
            Assert.That(((IDock)selected.Owner!).ActiveDockable, Is.SameAs(selected));
            Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(session.Documents[^1]));
        }
        finally
        {
            manager.ChangeKeyGesture(entry, original, platform);
        }
    }

    [AvaloniaTest]
    [TestCase(1000, false)]
    [TestCase(1000, true)]
    [TestCase(420, false)]
    [TestCase(420, true)]
    public async Task Switcher_renders_in_both_themes_and_narrow_windows(int width, bool light)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        try
        {
            await using var session = await Session.CreateAsync($"render-{width}-{light}", width: width, light: light);
            session.Press(Key.Tab, RawInputModifiers.Control);
            HeadlessTestHelpers.Render(2);
            Border container = session.Overlay.FindControl<Border>("SwitcherContainer")!;
            Assert.That(container.Bounds.Width, Is.LessThanOrEqualTo(width - 32));
            Assert.That(container.Bounds.Height, Is.LessThanOrEqualTo(340));
            ListBox documents = session.Overlay.FindControl<ListBox>("DocumentsList")!;
            ListBox tools = session.Overlay.FindControl<ListBox>("ToolsList")!;
            Assert.Multiple(() =>
            {
                Assert.That(documents.IsEffectivelyVisible, Is.True);
                Assert.That(tools.IsEffectivelyVisible, Is.True);
                Assert.That(documents.Bounds.Width, Is.GreaterThan(150));
                Assert.That(tools.TranslatePoint(default, session.Overlay)!.Value.X,
                    Is.LessThan(documents.TranslatePoint(default, session.Overlay)!.Value.X));
            });
            session.Capture($"switcher-{width}-{light}");
            session.Overlay.FindControl<Button>("NewTabButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(2);
            Assert.That(session.Overlay.FindControl<ListBox>("NewToolsList")!.IsEffectivelyVisible, Is.True);
            Assert.That(documents.IsEffectivelyVisible, Is.False);
            Assert.That(tools.IsEffectivelyVisible, Is.False);
            Assert.That(container.Bounds.Height, Is.LessThanOrEqualTo(390));
            session.Capture($"create-{width}-{light}");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private sealed class Session : IAsyncDisposable
    {
        private Session(Window window, MainView view, TextBox input, EditorTabItem[] documents)
        {
            Window = window;
            View = view;
            Input = input;
            Documents = documents;
        }

        public Window Window { get; }
        public MainView View { get; }
        public TextBox Input { get; }
        public EditorTabItem[] Documents { get; }
        public TabSwitcherViewModel Switcher => TestShell.MainViewModel.TabSwitcher;
        public TabSwitcherView Overlay => View.FindControl<TabSwitcherView>("TabSwitcherOverlay")!;
        public EditViewModel Editor => (EditViewModel)Documents[^1].Context.Value;

        public static async Task<Session> CreateAsync(string name, int documentCount = 3, int width = 1000, bool light = false)
        {
            await TestReset.ResetShellAsync();
            string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, "tab-switcher-" + name);
            Directory.CreateDirectory(directory);
            for (int index = 0; index < documentCount; index++)
            {
                var scene = new Scene(640, 480, "Scene " + index)
                {
                    Uri = new Uri(Path.Combine(directory, $"Scene {index}.scene")),
                };
                CoreSerializer.StoreToUri(scene, scene.Uri);
                TestShell.Editor.ActivateTabItem(scene);
            }
            HeadlessTestHelpers.Settle();
            var view = new MainView { DataContext = TestShell.MainViewModel };
            var input = new TextBox
            {
                Width = 140,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom
            };
            ((Grid)view.Content!).Children.Add(input);
            var window = new Window
            {
                Content = view,
                Width = width,
                Height = 720,
                RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
            };
            try
            {
                window.Show();
                HeadlessTestHelpers.Render(2);
                input.Focus();
                return new(window, view, input, TestShell.Editor.TabItems.ToArray());
            }
            catch
            {
                window.Close();
                await TestReset.ResetShellAsync();
                throw;
            }
        }

        public void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPress(key, modifiers, PhysicalKey.None, null);
            HeadlessTestHelpers.Settle();
        }

        public void Release(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
            HeadlessTestHelpers.Settle();
        }

        public void Capture(string name)
        {
            string directory = Path.Combine(Path.GetTempPath(), "beutl-tab-switcher-captures");
            Directory.CreateDirectory(directory);
            using var frame = Window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            frame!.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
        }

        public async ValueTask DisposeAsync()
        {
            Switcher.Close();
            TestShell.MainViewModel.CommandPalette.Close();
            Window.Close();
            HeadlessTestHelpers.Settle();
            await TestReset.ResetShellAsync();
        }
    }
}
