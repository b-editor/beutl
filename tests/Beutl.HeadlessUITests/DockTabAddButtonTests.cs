using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Editor.Components.CurvesTab;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.NodeGraphTab;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Beutl.Views.Dock;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DockTabAddButtonTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    [AvaloniaTest]
    public async Task Add_button_is_available_on_every_dock_and_only_visible_while_its_header_is_hovered()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-hover");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            ToolControl[] controls = view.GetVisualDescendants()
                .OfType<ToolControl>()
                .ToArray();
            Assert.That(controls, Is.Not.Empty);
            Assert.That(
                controls.All(control => FindAddButton(control) is not null),
                Is.True,
                "Every rendered dock tab strip should expose an add button.");

            IToolDock playerDock = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Player)!;
            ToolControl playerControl = controls.Single(control => ReferenceEquals(control.DataContext, playerDock));
            ToolTabAddButton button = FindAddButton(playerControl)!;
            ToolTabHeaderPanel header = playerControl.GetVisualDescendants()
                .OfType<ToolTabHeaderPanel>()
                .Single();
            Border addButtonBorder = playerControl.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Name == "PART_AddButtonBorder");
            ToolTabStrip tabStrip = playerControl.GetVisualDescendants()
                .OfType<ToolTabStrip>()
                .Single();

            Assert.Multiple(() =>
            {
                Assert.That(
                    addButtonBorder.BorderBrush,
                    Is.SameAs(addButtonBorder.FindResource("DockBorderSubtleBrush")));
                Assert.That(addButtonBorder.BorderThickness.Bottom, Is.EqualTo(1));
                // The button follows the last tab instead of sitting at the far right of the header.
                Assert.That(addButtonBorder.Bounds.Left, Is.EqualTo(tabStrip.Bounds.Right).Within(0.5));
                Assert.That(addButtonBorder.Bounds.Right, Is.LessThan(header.Bounds.Width - addButtonBorder.Bounds.Width));
                Assert.That(
                    button.CornerRadius,
                    Is.EqualTo((CornerRadius)button.FindResource("DockDocumentTabCreateButtonCornerRadius")!));
                Assert.That(button.Opacity, Is.EqualTo(0));
                Assert.That(button.IsHitTestVisible, Is.True);
                Assert.That(button.IsTabStop, Is.True);
            });

            window.MouseMove(Center(header, window));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(button.Opacity, Is.EqualTo(1));
                Assert.That(button.IsHitTestVisible, Is.True);
            });

            window.MouseMove(Center(button, window));
            HeadlessTestHelpers.Settle();

            Assert.That(
                button.Background,
                Is.SameAs(button.FindResource("DockChromeButtonHoverBackgroundBrush")));

            window.MouseMove(new Point(1, window.Bounds.Height - 1));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(button.Opacity, Is.EqualTo(0));
                Assert.That(button.IsHitTestVisible, Is.True);
            });

            Assert.That(button.Focus(NavigationMethod.Tab), Is.True);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(button.Opacity, Is.EqualTo(1));
                Assert.That(button.IsHitTestVisible, Is.True);
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Add_button_stays_visible_when_tabs_overflow_the_header()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-overflow");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            IToolDock target = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
            ToolControl targetControl = view.GetVisualDescendants()
                .OfType<ToolControl>()
                .Single(control => ReferenceEquals(control.DataContext, target));
            ToolTabAddButton button = FindAddButton(targetControl)!;
            ToolTabHeaderPanel header = targetControl.GetVisualDescendants()
                .OfType<ToolTabHeaderPanel>()
                .Single();
            Border addButtonBorder = targetControl.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Name == "PART_AddButtonBorder");
            ToolTabStrip tabStrip = targetControl.GetVisualDescendants()
                .OfType<ToolTabStrip>()
                .Single();

            var factory = (BeutlDockFactory)editor.DockHost.Factory;
            foreach (ToolTabExtension extension in factory.EnumerateToolTabExtensions())
            {
                if (extension.CanMultiple || !factory.IsToolTabOpen(extension))
                {
                    factory.OpenToolTab(extension, target);
                }
            }

            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(tabStrip.Bounds.Width, Is.EqualTo(header.Bounds.Width - addButtonBorder.Bounds.Width).Within(0.5));
                Assert.That(addButtonBorder.Bounds.Left, Is.EqualTo(tabStrip.Bounds.Right).Within(0.5));
                Assert.That(button.Bounds.Width, Is.EqualTo(28));
                Assert.That(button.IsEffectivelyVisible, Is.True);
            });

            Point? buttonLeft = button.TranslatePoint(new Point(0, 0), header);
            Point? buttonRight = button.TranslatePoint(new Point(button.Bounds.Width, 0), header);
            Assert.Multiple(() =>
            {
                Assert.That(buttonLeft, Is.Not.Null);
                Assert.That(buttonRight, Is.Not.Null);
                Assert.That(buttonLeft!.Value.X, Is.GreaterThanOrEqualTo(tabStrip.Bounds.Right - 0.5));
                Assert.That(buttonRight!.Value.X, Is.LessThanOrEqualTo(header.Bounds.Width + 0.5));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Header_panel_keeps_the_add_button_when_arranged_narrower_than_measured()
    {
        // Explicit widths would win over the arrange rect, so size the slots through their content,
        // the way ToolTabStrip and the add-button Border are sized in the real template.
        var strip = new Border { Child = new Border { Width = 200, Height = 28 } };
        var addButton = new Border { Child = new Border { Width = 36, Height = 28 } };
        var freeSpace = new Border();
        var panel = new ToolTabHeaderPanel { Children = { strip, addButton, freeSpace } };

        panel.Measure(new Size(300, 28));
        panel.Arrange(new Rect(0, 0, 150, 28));

        Assert.Multiple(() =>
        {
            Assert.That(strip.Bounds.Width, Is.EqualTo(114));
            Assert.That(addButton.Bounds.Left, Is.EqualTo(114));
            Assert.That(addButton.Bounds.Width, Is.EqualTo(36));
            Assert.That(freeSpace.Bounds.Width, Is.EqualTo(0));
        });
    }

    [AvaloniaTest]
    public async Task Add_button_opens_an_empty_tab_that_turns_into_the_picked_tool()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-new-tab");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            IToolDock target = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
            ToolControl targetControl = view.GetVisualDescendants()
                .OfType<ToolControl>()
                .Single(control => ReferenceEquals(control.DataContext, target));
            ToolTabAddButton button = FindAddButton(targetControl)!;
            int tabCount = target.VisibleDockables!.Count;

            Point buttonCenter = Center(button, window);
            window.MouseDown(buttonCenter, MouseButton.Left);
            window.MouseUp(buttonCenter, MouseButton.Left);
            HeadlessTestHelpers.Settle();

            var newTab = target.VisibleDockables[^1] as NewToolTabDockable;
            NewToolTabView? page = targetControl.GetVisualDescendants()
                .OfType<NewToolTabView>()
                .SingleOrDefault();

            Assert.Multiple(() =>
            {
                Assert.That(button.ContextFlyout, Is.Null, "The add button no longer opens a menu.");
                Assert.That(target.VisibleDockables, Has.Count.EqualTo(tabCount + 1));
                Assert.That(newTab, Is.Not.Null);
                Assert.That(target.ActiveDockable, Is.SameAs(newTab));
                Assert.That(newTab!.Title, Is.EqualTo(Strings.NewTab));
                Assert.That(page, Is.Not.Null);
                Assert.That(page!.DataContext, Is.SameAs(newTab));
                Assert.That(page.SearchBox.IsFocused, Is.True, "Typing right away should search the tools.");
            });

            Button timelineButton = FindToolButton(page!, TimelineTabExtension.Instance);
            Button historyButton = FindToolButton(page!, HistoryTabExtension.Instance);
            Assert.Multiple(() =>
            {
                Assert.That(timelineButton.IsEnabled, Is.False, "An open single-instance tool cannot be added again.");
                Assert.That(
                    newTab!.OpenItems.Select(item => item.Extension),
                    Does.Contain(TimelineTabExtension.Instance),
                    "Open tools are listed apart from the ones that can be opened.");
                Assert.That(
                    newTab.AvailableItems.Select(item => item.Extension),
                    Does.Not.Contain(TimelineTabExtension.Instance));
                // Disabled tiles keep their tooltip, which shows a name trimmed in a narrow dock.
                Assert.That(ToolTip.GetShowOnDisabled(timelineButton), Is.True);
                Assert.That(ToolTip.GetTip(timelineButton), Is.EqualTo(Strings.Timeline));
                Assert.That(historyButton.IsEnabled, Is.True);
                Assert.That(ToolTip.GetTip(historyButton), Is.EqualTo(Strings.History));
            });

            historyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(target.VisibleDockables, Has.Count.EqualTo(tabCount + 1));
                Assert.That(target.VisibleDockables.OfType<NewToolTabDockable>(), Is.Empty);
                Assert.That(
                    target.VisibleDockables[^1],
                    Is.InstanceOf<BeutlToolDockable>()
                        .With.Property(nameof(BeutlToolDockable.ToolContext))
                        .With.Property(nameof(IToolContext.Extension)).SameAs(HistoryTabExtension.Instance));
                Assert.That(target.ActiveDockable, Is.SameAs(target.VisibleDockables[^1]));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task A_recycled_page_takes_the_focus_its_next_empty_tab_asks_for()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-recycled-page");
        BeutlDockFactory factory = editor.DockHost.Factory;
        IToolDock target = factory.GetAnchoredDock(DockAnchor.Left)!;
        NewToolTabDockable first = factory.OpenNewToolTab(target);
        first.FocusOnShow = false;

        var page = new NewToolTabView { DataContext = first };
        var elsewhere = new TextBox();
        var window = new Window
        {
            Content = new StackPanel { Children = { elsewhere, page } },
            Width = 600,
            Height = 600,
        };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(elsewhere.Focus(), Is.True);

            // The dock's recycling template can hand an attached page to the next empty tab
            // instead of loading a new one.
            NewToolTabDockable second = factory.OpenNewToolTab(target);
            page.DataContext = second;
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(page.SearchBox.IsFocused, Is.True);
                Assert.That(second.FocusOnShow, Is.False, "A served request must not fire again later.");
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Picked_tool_takes_the_empty_tabs_place_in_the_strip()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-in-place");
        BeutlDockFactory factory = editor.DockHost.Factory;
        IToolDock target = factory.GetAnchoredDock(DockAnchor.Left)!;
        IDockable[] before = target.VisibleDockables!.ToArray();
        Assert.That(before, Is.Not.Empty);

        // The user may drag the empty tab before choosing; the tool must land where the tab is now.
        var newTab = new NewToolTabDockable();
        factory.InsertDockable(target, newTab, 0);

        Assert.That(newTab.Open(HistoryTabExtension.Instance), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(target.VisibleDockables, Has.Count.EqualTo(before.Length + 1));
            Assert.That(
                (target.VisibleDockables![0] as BeutlToolDockable)?.ToolContext.Extension,
                Is.SameAs(HistoryTabExtension.Instance));
            Assert.That(target.VisibleDockables.Skip(1), Is.EqualTo(before));
            Assert.That(newTab.Owner is IDock owner && owner.VisibleDockables!.Contains(newTab), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Empty_tab_search_matches_display_and_internal_names_and_enter_opens_the_first_match()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-search");
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;

        // The whole shell, as in the app: MainView is focusable, so a search box that let Enter move
        // focus to an ancestor would never open the match.
        var view = new MainView { DataContext = TestShell.MainViewModel };
        var window = new Window { Content = view, Width = 1000, Height = 720 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            IToolDock target = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
            NewToolTabDockable newTab = editor.DockHost.Factory.OpenNewToolTab(target);
            HeadlessTestHelpers.Settle();
            NewToolTabView page = view.GetVisualDescendants().OfType<NewToolTabView>().Single();
            TextBlock noResults = page.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Text == Strings.NewToolTab_NoResults);

            Assert.That(newTab.AvailableItems, Has.Count.GreaterThan(1));
            Assert.That(noResults.IsVisible, Is.False);

            newTab.SearchText = "no tool is called this";
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(newTab.AvailableItems, Is.Empty);
                Assert.That(newTab.OpenItems, Is.Empty);
                Assert.That(noResults.IsVisible, Is.True);
            });

            // A Japanese UI still finds a tool by its English name.
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            newTab.Refresh();
            newTab.SearchText = "  history ";
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(
                    newTab.AvailableItems.Select(item => item.Extension),
                    Is.EqualTo(new ToolTabExtension[] { HistoryTabExtension.Instance }));
                Assert.That(newTab.OpenItems, Is.Empty);
                Assert.That(noResults.IsVisible, Is.False);
                Assert.That(newTab.AvailableItems[0].Header, Is.EqualTo(Strings.History).And.Not.EqualTo("History"));
            });

            // The English label differs from the internal name (FileBrowser, NodeGraphTab).
            foreach ((string query, ToolTabExtension expected) in new (string, ToolTabExtension)[]
                     {
                         ("Files", FileBrowserTabExtension.Instance),
                         ("node graph", NodeGraphTabExtension.Instance),
                     })
            {
                newTab.SearchText = query;
                Assert.That(
                    newTab.AvailableItems.Select(item => item.Extension),
                    Does.Contain(expected),
                    $"'{query}' should find {expected.Name} in the Japanese UI.");
            }

            newTab.SearchText = "history";
            HeadlessTestHelpers.Settle();
            page.SearchBox.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            HeadlessTestHelpers.Settle();
            Assert.That(
                (TopLevel.GetTopLevel(page)!.FocusManager!.GetFocusedElement() as Control)?.DataContext,
                Is.SameAs(newTab.AvailableItems[0]),
                "Down moves from the search box into the results.");

            page.SearchBox.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(target.VisibleDockables!.OfType<NewToolTabDockable>(), Is.Empty);
                Assert.That(
                    (target.ActiveDockable as BeutlToolDockable)?.ToolContext.Extension,
                    Is.SameAs(HistoryTabExtension.Instance));
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Pinned_tools_lead_every_new_tab_and_stay_pinned()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-pins");
        CoreList<string> pins = GlobalConfiguration.Instance.ViewConfig.PinnedToolTabs;
        pins.Clear();

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 1000, Height = 800 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            BeutlDockFactory factory = editor.DockHost.Factory;
            NewToolTabDockable left = factory.OpenNewToolTab(factory.GetAnchoredDock(DockAnchor.Left)!);
            NewToolTabDockable bottom = factory.OpenNewToolTab(factory.GetAnchoredDock(DockAnchor.Bottom)!);
            HeadlessTestHelpers.Settle();
            NewToolTabView leftPage = view.GetVisualDescendants().OfType<NewToolTabView>()
                .Single(page => ReferenceEquals(page.DataContext, left));

            Button historyPin = FindPinButton(leftPage, HistoryTabExtension.Instance);
            Assert.Multiple(() =>
            {
                Assert.That(left.PinnedItems, Is.Empty);
                Assert.That(left.ShowsToolsHeader, Is.False, "Without pins the list needs no headings.");
                Assert.That(ToolTip.GetTip(historyPin), Is.EqualTo(Strings.NewToolTab_Pin));
            });

            historyPin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Settle();
            FindPinButton(leftPage, CurvesTabExtension.Instance).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Settle();

            ToolTabExtension[] pinnedOrder = [HistoryTabExtension.Instance, CurvesTabExtension.Instance];
            Assert.Multiple(() =>
            {
                Assert.That(left.PinnedItems.Select(item => item.Extension), Is.EqualTo(pinnedOrder));
                Assert.That(left.AvailableItems.Select(item => item.Extension), Has.None.AnyOf(pinnedOrder));
                Assert.That(left.ShowsToolsHeader, Is.True);
                Assert.That(ToolTip.GetTip(FindPinButton(leftPage, HistoryTabExtension.Instance)), Is.EqualTo(Strings.NewToolTab_Unpin));
                // Pins are a user preference, saved with the settings rather than the project.
                Assert.That(
                    pins,
                    Is.EqualTo(new[]
                    {
                        NewToolTabDockable.GetPinKey(HistoryTabExtension.Instance),
                        NewToolTabDockable.GetPinKey(CurvesTabExtension.Instance),
                    }));
                Assert.That(
                    bottom.PinnedItems.Select(item => item.Extension),
                    Is.EqualTo(pinnedOrder),
                    "Another empty tab picks the pins up while it is shown.");
            });

            // A pinned tool that is already open waits with the other open tools.
            Assert.That(left.Open(HistoryTabExtension.Instance), Is.True);
            HeadlessTestHelpers.Settle();
            NewToolTabView bottomPage = view.GetVisualDescendants().OfType<NewToolTabView>()
                .Single(page => ReferenceEquals(page.DataContext, bottom));
            Assert.Multiple(() =>
            {
                Assert.That(bottom.PinnedItems.Select(item => item.Extension), Is.EqualTo(new[] { CurvesTabExtension.Instance }));
                Assert.That(
                    bottom.OpenItems.Single(item => item.Extension == HistoryTabExtension.Instance).IsPinned,
                    Is.True);
            });

            // Enter opens the first pinned tool.
            bottomPage.SearchBox.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            HeadlessTestHelpers.Settle();
            Assert.That(
                (factory.GetAnchoredDock(DockAnchor.Bottom)!.ActiveDockable as BeutlToolDockable)?.ToolContext.Extension,
                Is.SameAs(CurvesTabExtension.Instance));

            NewToolTabDockable third = factory.OpenNewToolTab(factory.GetAnchoredDock(DockAnchor.Left)!);
            HeadlessTestHelpers.Settle();
            NewToolTabView thirdPage = view.GetVisualDescendants().OfType<NewToolTabView>()
                .Single(page => ReferenceEquals(page.DataContext, third));
            FindPinButton(thirdPage, CurvesTabExtension.Instance).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(pins, Is.EqualTo(new[] { NewToolTabDockable.GetPinKey(HistoryTabExtension.Instance) }));
                Assert.That(third.PinnedItems, Is.Empty, "History is pinned but open.");
                Assert.That(
                    third.AvailableItems.Select(item => item.Extension),
                    Does.Contain(CurvesTabExtension.Instance));
            });
        }
        finally
        {
            pins.Clear();
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Empty_tab_follows_single_instance_tools_opened_and_closed_elsewhere()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-live");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            BeutlDockFactory factory = editor.DockHost.Factory;
            NewToolTabDockable newTab = factory.OpenNewToolTab(factory.GetAnchoredDock(DockAnchor.Left)!);
            HeadlessTestHelpers.Settle();
            NewToolTabView page = view.GetVisualDescendants().OfType<NewToolTabView>().Single();

            Assert.That(FindToolButton(page, HistoryTabExtension.Instance).IsEnabled, Is.True);

            Assert.That(
                editor.DockHost.OpenToolTabFromExtension(
                    HistoryTabExtension.Instance, factory.GetAnchoredDock(DockAnchor.Right)),
                Is.True);
            HeadlessTestHelpers.Settle();

            Assert.That(FindToolButton(page, HistoryTabExtension.Instance).IsEnabled, Is.False);

            BeutlToolDockable history = factory.EnumerateTools()
                .Single(tool => tool.ToolContext.Extension == HistoryTabExtension.Instance);
            editor.DockHost.CloseToolTab(history.ToolContext);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(FindToolButton(page, HistoryTabExtension.Instance).IsEnabled, Is.True);
                Assert.That(page.DataContext, Is.SameAs(newTab));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Empty_tab_is_saved_and_restored_in_its_place()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-persist");
        BeutlDockFactory factory = editor.DockHost.Factory;
        IToolDock left = factory.GetAnchoredDock(DockAnchor.Left)!;
        var newTab = new NewToolTabDockable();
        factory.InsertDockable(left, newTab, 0);
        factory.SetActiveDockable(newTab);
        int tabCount = left.VisibleDockables!.Count;

        var json = new JsonObject();
        editor.DockHost.WriteToJson(json);

        var restored = new DockHostViewModel("dock-tab-add-persist", editor);
        try
        {
            restored.ReadFromJson(json);
            HeadlessTestHelpers.Settle();

            IToolDock restoredLeft = restored.Factory.GetAnchoredDock(DockAnchor.Left)!;
            Assert.Multiple(() =>
            {
                Assert.That(restoredLeft.VisibleDockables, Has.Count.EqualTo(tabCount));
                Assert.That(restoredLeft.VisibleDockables![0], Is.InstanceOf<NewToolTabDockable>());
                Assert.That(restoredLeft.ActiveDockable, Is.SameAs(restoredLeft.VisibleDockables[0]));
                Assert.That(
                    ((NewToolTabDockable)restoredLeft.VisibleDockables[0]).FocusOnShow,
                    Is.False,
                    "A restored empty tab must not take the keyboard from the editor.");
            });
        }
        finally
        {
            restored.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task A_layout_holding_only_an_empty_tab_restores_without_the_default_tools()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-only-new-tab");
        BeutlDockFactory factory = editor.DockHost.Factory;
        // The empty tab keeps the left dock alive, so the default tools would have a place to go.
        factory.OpenNewToolTab(factory.GetAnchoredDock(DockAnchor.Left)!);
        foreach (BeutlToolDockable tool in factory.EnumerateTools().ToArray())
        {
            editor.DockHost.CloseToolTab(tool.ToolContext);
        }

        Assert.That(factory.EnumerateTools(), Is.Empty);

        var json = new JsonObject();
        editor.DockHost.WriteToJson(json);
        JsonObject captured = editor.DockHost.CaptureLayout();

        var restored = new DockHostViewModel("dock-tab-add-only-new-tab", editor);
        try
        {
            restored.ReadFromJson(json);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(restored.Factory.EnumerateTools(), Is.Empty, "The empty tab is the user's layout, not a broken one.");
                Assert.That(
                    BeutlDockFactory.Traverse(restored.Layout.Value).OfType<NewToolTabDockable>().Count(),
                    Is.EqualTo(1));
            });
        }
        finally
        {
            restored.Dispose();
        }

        Assert.That(editor.DockHost.ApplyLayout(captured), Is.True);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(factory.EnumerateTools(), Is.Empty, "Applying a saved layout keeps it as saved.");
            Assert.That(
                BeutlDockFactory.Traverse(editor.DockHost.Layout.Value).OfType<NewToolTabDockable>().Count(),
                Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    [TestCase(240, 1)]
    [TestCase(320, 1)]
    [TestCase(420, 2)]
    [TestCase(1100, 3)]
    public async Task Tool_grid_adds_columns_as_the_page_widens(int width, int columns)
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene($"dock-tab-add-columns-{width}");
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        // Japanese names are the longest and need the taller line box.
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        BeutlDockFactory factory = editor.DockHost.Factory;
        NewToolTabDockable newTab = factory.OpenNewToolTab(factory.GetAnchoredDock(DockAnchor.Left)!);
        newTab.Refresh();
        Assert.That(newTab.AvailableItems, Has.Count.GreaterThanOrEqualTo(8));

        var page = new NewToolTabView { DataContext = newTab };
        var window = new Window { Content = page, Width = width, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Control[] tiles = Enumerable.Range(0, 8)
                .Select(index => page.AvailableList.TryGetElement(index))
                .OfType<Control>()
                .ToArray();
            Assert.That(tiles, Has.Length.EqualTo(8));

            Assert.Multiple(() =>
            {
                Assert.That(tiles.Select(tile => Math.Round(tile.Bounds.X)).Distinct().Count(), Is.EqualTo(columns));
                // Long names wrap rather than widen a tile past its column.
                Assert.That(tiles.Max(tile => tile.Bounds.Right), Is.LessThanOrEqualTo(page.AvailableList.Bounds.Width + 0.5));
                Assert.That(tiles.Select(tile => tile.Bounds.Height).Distinct().Count(), Is.EqualTo(1));
                foreach (TextBlock name in tiles.SelectMany(tile => tile.GetVisualDescendants().OfType<TextBlock>()))
                {
                    Assert.That(
                        name.TextLayout.TextLines.Any(line => line.HasCollapsed),
                        Is.False,
                        $"'{name.Text}' should wrap within two lines, not be cut off.");
                }
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Empty_tab_lists_a_tool_installed_while_it_is_shown()
    {
        const int packageId = -42_755;
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-installed");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        var installed = new InstalledToolTabExtension();

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            BeutlDockFactory factory = editor.DockHost.Factory;
            NewToolTabDockable newTab = factory.OpenNewToolTab(factory.GetAnchoredDock(DockAnchor.Left)!);
            HeadlessTestHelpers.Settle();
            Assert.That(newTab.AvailableItems.Select(item => item.Extension), Does.Not.Contain(installed));

            // Package installs register from a worker thread.
            Task.Run(() => TestShell.Extensions.AddExtensions(packageId, [installed])).Wait();
            HeadlessTestHelpers.Settle();
            newTab.SearchText = "installed";
            Assert.Multiple(() =>
            {
                Assert.That(newTab.AvailableItems.Select(item => item.Extension), Does.Contain(installed));
                Assert.That(newTab.References(installed), Is.True);
            });

            _ = TestShell.Extensions.RemoveExtensions(packageId);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(newTab.AvailableItems.Select(item => item.Extension), Does.Not.Contain(installed));
                Assert.That(newTab.References(installed), Is.False, "A removed tool must not stay cached.");
            });
        }
        finally
        {
            _ = TestShell.Extensions.RemoveExtensions(packageId);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task A_hidden_empty_tab_does_not_hold_on_to_a_removed_tool()
    {
        const int packageId = -42_756;
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-hidden-release");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        var installed = new InstalledToolTabExtension();

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            BeutlDockFactory factory = editor.DockHost.Factory;
            IToolDock left = factory.GetAnchoredDock(DockAnchor.Left)!;
            IDockable neighbour = left.VisibleDockables!.OfType<BeutlToolDockable>().First();
            NewToolTabDockable newTab = factory.OpenNewToolTab(left);
            TestShell.Extensions.AddExtensions(packageId, [installed]);
            HeadlessTestHelpers.Settle();
            Assert.That(newTab.References(installed), Is.True);

            // Another tab in the dock takes over, so no page shows the empty tab any more.
            factory.SetActiveDockable(neighbour);
            HeadlessTestHelpers.Settle();
            Assert.That(
                view.GetVisualDescendants().OfType<NewToolTabView>().Any(page => ReferenceEquals(page.DataContext, newTab)),
                Is.False);

            _ = TestShell.Extensions.RemoveExtensions(packageId);
            HeadlessTestHelpers.Settle();
            Assert.That(newTab.References(installed), Is.False, "A hidden tab hears no extension changes.");

            factory.SetActiveDockable(newTab);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(newTab.AvailableItems, Is.Not.Empty, "Showing the tab again lists the tools again.");
                Assert.That(newTab.References(installed), Is.False);
            });
        }
        finally
        {
            _ = TestShell.Extensions.RemoveExtensions(packageId);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [Test]
    public void Pin_keys_name_the_assembly_but_not_its_version()
    {
        Type probe = typeof(PinKeyProbe.PinKeyProbeToolTabExtension);
        string key = NewToolTabDockable.GetPinKey(new PinKeyProbe.PinKeyProbeToolTabExtension());
        Assume.That(probe.FullName, Does.Not.Contain(probe.Assembly.GetName().Name));

        Assert.Multiple(() =>
        {
            // Same-named types from two packages must not share a pin.
            Assert.That(key, Does.Contain(probe.Assembly.GetName().Name));
            // A package update must not drop its pins.
            Assert.That(key, Does.Not.Contain(probe.Assembly.GetName().Version!.ToString()));
        });
    }

    [AvaloniaTest]
    public async Task Extension_returning_success_with_a_null_context_is_rejected()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-null-context");
        IToolDock target = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;

        bool opened = editor.DockHost.OpenToolTabFromExtension(new NullContextToolTabExtension(), target);

        Assert.That(opened, Is.False);
    }

    private static ToolTabAddButton? FindAddButton(Visual root)
    {
        return root.GetVisualDescendants().OfType<ToolTabAddButton>().SingleOrDefault();
    }

    // The lists virtualize, so a tool scrolled out of view has no row until one is requested.
    private static Panel FindToolRow(NewToolTabView page, ToolTabExtension extension)
    {
        var newTab = (NewToolTabDockable)page.DataContext!;
        foreach ((IReadOnlyList<NewToolTabItem> items, ItemsRepeater list) in new[]
                 {
                     (newTab.PinnedItems, page.PinnedList),
                     (newTab.AvailableItems, page.AvailableList),
                     (newTab.OpenItems, page.OpenList),
                 })
        {
            int index = items.ToList().FindIndex(item => ReferenceEquals(item.Extension, extension));
            if (index < 0) continue;

            var row = (Panel)list.GetOrCreateElement(index);
            Assert.That(row.DataContext, Is.SameAs(items[index]));
            return row;
        }

        Assert.Fail($"{extension.Name} is not listed.");
        return null!;
    }

    private static Button FindToolButton(NewToolTabView page, ToolTabExtension extension)
    {
        return NewToolTabView.FindToolButton(FindToolRow(page, extension))!;
    }

    private static Button FindPinButton(NewToolTabView page, ToolTabExtension extension)
    {
        return FindToolRow(page, extension).Children.OfType<Button>().Single(button => button.Classes.Contains("pin"));
    }

    private static Point Center(Control control, Visual relativeTo)
    {
        Point? point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            relativeTo);
        Assert.That(point, Is.Not.Null);
        return point!.Value;
    }

    private sealed class InstalledToolTabExtension : ToolTabExtension
    {
        public override string Name => "Installed test tool";

        public override string DisplayName => "Installed test tool";

        public override string? Header => "Installed test tool";

        public override bool CanMultiple => true;

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = null;
            return false;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = null;
            return false;
        }
    }

    private sealed class NullContextToolTabExtension : ToolTabExtension
    {
        public override bool CanMultiple => true;

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = null;
            return false;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = null!;
            return true;
        }
    }
}
