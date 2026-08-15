using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Beutl.Views.Dock;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;

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
            Grid header = playerControl.GetVisualDescendants()
                .OfType<Grid>()
                .Single(control => control.Name == "PART_TabHeader");
            Border addButtonBorder = playerControl.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Name == "PART_AddButtonBorder");

            Assert.Multiple(() =>
            {
                Assert.That(
                    addButtonBorder.BorderBrush,
                    Is.SameAs(addButtonBorder.FindResource("DockBorderSubtleBrush")));
                Assert.That(addButtonBorder.BorderThickness.Bottom, Is.EqualTo(1));
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
    public async Task Add_menu_disables_open_singletons_and_opens_the_selected_tool_in_its_dock()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-menu");

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
            Point buttonCenter = Center(button, window);
            window.MouseDown(buttonCenter, MouseButton.Left);
            window.MouseUp(buttonCenter, MouseButton.Left);
            HeadlessTestHelpers.Settle();

            ContextMenu? menu = button.ContextMenu;
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu!.IsOpen, Is.True);
            MenuItem[] items = menu.ItemsSource!.Cast<MenuItem>().ToArray();

            MenuItem timelineItem = items.Single(
                item => ReferenceEquals(item.DataContext, TimelineTabExtension.Instance));
            MenuItem historyItem = items.Single(
                item => ReferenceEquals(item.DataContext, HistoryTabExtension.Instance));

            Assert.Multiple(() =>
            {
                Assert.That(timelineItem.IsEnabled, Is.False);
                Assert.That(historyItem.IsEnabled, Is.True);
            });

            historyItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            HeadlessTestHelpers.Settle();

            BeutlToolDockable? added = target.VisibleDockables?
                .OfType<BeutlToolDockable>()
                .SingleOrDefault(dockable => dockable.ToolContext.Extension == HistoryTabExtension.Instance);

            Assert.Multiple(() =>
            {
                Assert.That(added, Is.Not.Null);
                Assert.That(target.ActiveDockable, Is.SameAs(added));
            });

            menu.Close();
            HeadlessTestHelpers.Settle();
            window.MouseDown(buttonCenter, MouseButton.Right);
            window.MouseUp(buttonCenter, MouseButton.Right);
            HeadlessTestHelpers.Settle();

            MenuItem refreshedHistoryItem = menu.ItemsSource!
                .Cast<MenuItem>()
                .Single(item => ReferenceEquals(item.DataContext, HistoryTabExtension.Instance));
            Assert.That(refreshedHistoryItem.IsEnabled, Is.False);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
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
