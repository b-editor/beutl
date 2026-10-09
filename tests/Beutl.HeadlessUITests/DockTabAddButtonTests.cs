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
                Assert.That(ToolTip.GetTip(timelineButton), Is.EqualTo(Strings.NewToolTab_AlreadyOpen));
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
                (target.VisibleDockables[0] as BeutlToolDockable)?.ToolContext.Extension,
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

            Assert.That(newTab.VisibleItems, Has.Count.GreaterThan(1));
            Assert.That(noResults.IsVisible, Is.False);

            newTab.SearchText = "no tool is called this";
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(newTab.VisibleItems, Is.Empty);
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
                    newTab.VisibleItems.Select(item => item.Extension),
                    Is.EqualTo(new ToolTabExtension[] { HistoryTabExtension.Instance }));
                Assert.That(newTab.VisibleItems[0].Header, Is.EqualTo(Strings.History).And.Not.EqualTo("History"));
            });

            page.SearchBox.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            HeadlessTestHelpers.Settle();
            Assert.That(
                (TopLevel.GetTopLevel(page)!.FocusManager!.GetFocusedElement() as Control)?.DataContext,
                Is.SameAs(newTab.VisibleItems[0]),
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

    // The list virtualizes, so a tool scrolled out of view has no button until one is requested.
    private static Button FindToolButton(NewToolTabView page, ToolTabExtension extension)
    {
        var newTab = (NewToolTabDockable)page.DataContext!;
        int index = newTab.VisibleItems.ToList().FindIndex(item => ReferenceEquals(item.Extension, extension));
        Assert.That(index, Is.GreaterThanOrEqualTo(0), $"{extension.Name} is not listed.");
        var button = (Button)page.ToolsList.GetOrCreateElement(index);
        Assert.That(button.DataContext, Is.SameAs(newTab.VisibleItems[index]));
        return button;
    }

    private static Point Center(Control control, Visual relativeTo)
    {
        Point? point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            relativeTo);
        Assert.That(point, Is.Not.Null);
        return point!.Value;
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
