using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Editor.Components.ColorGradingTab.ViewModels;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Extensibility;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Dock.Model.Controls;
using Dock.Model.Core;
using Moq;
using PinKeyProbe;
using Reactive.Bindings;
using PaneStack = Dock.Controls.ProportionalStackPanel.ProportionalStackPanel;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ToolTabPlacementTests
{
    private static void CloseAll<T>(EditViewModel editor) where T : class, IToolContext
    {
        while (editor.FindToolTab<T>() is { } tab)
        {
            editor.CloseToolTab(tab);
        }
    }

    private static BeutlToolDockable DockableOf(EditViewModel editor, IToolContext context)
    {
        return editor.DockHost.Factory.EnumerateTools().Single(tool => tool.ToolContext == context);
    }

    private static void CloseTabsIn(EditViewModel editor, IToolDock dock)
    {
        foreach (BeutlToolDockable tool in dock.VisibleDockables!.OfType<BeutlToolDockable>().ToArray())
        {
            editor.CloseToolTab(tool.ToolContext);
        }

        Assert.That(dock.VisibleDockables, Is.Empty);
    }

    // The room a dock gets from the split holding it.
    private static Rect PaneBounds(Window window, IDock dock)
    {
        ContentPresenter pane = window.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .Single(presenter => ReferenceEquals(presenter.Content, dock) && presenter.GetVisualParent() is PaneStack);
        return new Rect(pane.TranslatePoint(default, window)!.Value, pane.Bounds.Size);
    }

    private static void AssertSameBounds(Rect actual, Rect expected, string message)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(1), message);
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1), message);
            Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(1), message);
            Assert.That(actual.Height, Is.EqualTo(expected.Height).Within(1), message);
        });
    }

    // The layout's panes as nested splits, such as "V[H[Dock.Left,Dock.Player,Dock.Right],Dock.Bottom]".
    private static string Shape(IDockable node)
    {
        return node switch
        {
            IRootDock root => Shape(root.VisibleDockables!.Single()),
            IProportionalDock split =>
                $"{(split.Orientation == Orientation.Horizontal ? 'H' : 'V')}[{string.Join(",", split.VisibleDockables!.Where(child => child is not IProportionalDockSplitter).Select(Shape))}]",
            _ => node.Id,
        };
    }

    private static JsonObject Split(Orientation orientation, params JsonObject[] panes)
    {
        var children = new JsonArray();
        foreach (JsonObject pane in panes)
        {
            if (children.Count > 0)
            {
                children.Add(new JsonObject { ["$type"] = "splitter" });
            }

            children.Add(pane);
        }

        return new JsonObject
        {
            ["$type"] = "proportional",
            ["orientation"] = orientation == Orientation.Horizontal ? "horizontal" : "vertical",
            ["children"] = children,
        };
    }

    // A dock saved with no tab, except the preview's.
    private static JsonObject Pane(string id)
    {
        var pane = new JsonObject { ["$type"] = "tool_dock", ["id"] = id };
        if (id == DockIds.Player)
        {
            pane["tools"] = new JsonArray(new JsonObject { ["$type"] = "player" });
        }

        return pane;
    }

    // Layouts an earlier version saved after every tab of some default docks was closed, which
    // removed those docks.
    private static IEnumerable<TestCaseData> LayoutsSavedWithoutDefaultDocks()
    {
        yield return new TestCaseData("landscape-preview", 640, 480,
                Split(Orientation.Vertical, Pane(DockIds.Player)))
            .SetArgDisplayNames("landscape, preview only");
        yield return new TestCaseData("landscape-no-left", 640, 480,
                Split(Orientation.Vertical,
                    Split(Orientation.Horizontal, Pane(DockIds.Player), Pane(DockIds.Right)),
                    Pane(DockIds.Bottom)))
            .SetArgDisplayNames("landscape, no left dock");
        yield return new TestCaseData("portrait-preview", 480, 640,
                Split(Orientation.Horizontal, Pane(DockIds.Player)))
            .SetArgDisplayNames("portrait, preview only");
        yield return new TestCaseData("portrait-no-left", 480, 640,
                Split(Orientation.Horizontal,
                    Pane(DockIds.Player),
                    Split(Orientation.Vertical, Pane(DockIds.Right), Pane(DockIds.Bottom))))
            .SetArgDisplayNames("portrait, no left dock");
        yield return new TestCaseData("portrait-no-bottom", 480, 640,
                Split(Orientation.Horizontal,
                    Pane(DockIds.Player),
                    Split(Orientation.Horizontal, Pane(DockIds.Left), Pane(DockIds.Right))))
            .SetArgDisplayNames("portrait, no bottom dock");
    }

    [AvaloniaTest]
    public async Task Tab_opened_from_code_goes_to_its_tools_default_dock()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-default-anchor");
        CloseAll<GraphEditorTabViewModel>(editor);
        CloseAll<SceneSettingsTabViewModel>(editor);

        var graphEditor = new GraphEditorTabViewModel(editor);
        var sceneSettings = new SceneSettingsTabViewModel(editor);
        Assert.That(editor.OpenToolTab(graphEditor), Is.True);
        Assert.That(editor.OpenToolTab(sceneSettings), Is.True);

        IToolDock bottom = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)!;
        IToolDock right = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Right)!;
        Assert.Multiple(() =>
        {
            Assert.That(DockableOf(editor, graphEditor).Owner, Is.SameAs(bottom));
            Assert.That(DockableOf(editor, sceneSettings).Owner, Is.SameAs(right));
        });
    }

    [AvaloniaTest]
    public async Task Editing_tools_open_in_their_default_dock()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-editor-tools");
        (IToolContext Tab, DockAnchor Anchor)[] tools =
        [
            (new CurvesTabViewModel(editor), DockAnchor.Left),
            (new ColorScopesTabViewModel(editor), DockAnchor.Left),
            (new NodeGraphTabViewModel(editor), DockAnchor.Bottom),
            (new ColorGradingTabViewModel(editor), DockAnchor.Bottom),
            (new PathEditorTabViewModel(editor), DockAnchor.Bottom),
        ];

        foreach ((IToolContext tab, DockAnchor anchor) in tools)
        {
            Assert.That(editor.OpenToolTab(tab), Is.True);
            Assert.That(
                DockableOf(editor, tab).Owner,
                Is.SameAs(editor.DockHost.Factory.GetAnchoredDock(anchor)),
                tab.Extension.Name);
        }
    }

    [AvaloniaTest]
    public async Task Tab_without_a_default_dock_keeps_the_fallback()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-no-anchor");
        var context = new Mock<IToolContext>();
        context.SetupGet(c => c.Extension).Returns(new PinKeyProbeToolTabExtension());
        context.SetupGet(c => c.Header).Returns(new ReactivePropertySlim<string>("Probe"));
        context.SetupGet(c => c.IsSelected).Returns(new ReactivePropertySlim<bool>());

        Assert.That(editor.OpenToolTab(context.Object), Is.True);

        Assert.That(
            DockableOf(editor, context.Object).Owner,
            Is.SameAs(editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)));
    }

    [AvaloniaTest]
    public async Task Tab_goes_to_its_tools_default_dock_after_every_tab_there_was_closed()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-emptied-dock");
        IToolDock bottom = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)!;
        CloseTabsIn(editor, bottom);

        var graphEditor = new GraphEditorTabViewModel(editor);
        Assert.That(editor.OpenToolTab(graphEditor), Is.True);

        Assert.That(DockableOf(editor, graphEditor).Owner, Is.SameAs(bottom));
    }

    [AvaloniaTest]
    public async Task An_emptied_default_dock_takes_no_room_and_comes_back_at_its_size()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-emptied-dock-view");
        var window = new Window { Content = new EditView { DataContext = editor }, Width = 1200, Height = 700 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            IToolDock bottom = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)!;
            IToolDock player = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Player)!;
            Rect before = PaneBounds(window, bottom);
            HeadlessCapture.SaveIfRequested(window, nameof(ToolTabPlacementTests), "default");

            CloseTabsIn(editor, bottom);
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(window, nameof(ToolTabPlacementTests), "emptied");

            Rect emptied = PaneBounds(window, bottom);
            Assert.Multiple(() =>
            {
                Assert.That(emptied.Width * emptied.Height, Is.Zero, "an empty dock shows no pane");
                Assert.That(
                    PaneBounds(window, player).Bottom,
                    Is.EqualTo(before.Bottom).Within(1),
                    "the panes above take its room");
            });

            Assert.That(editor.OpenToolTab(new GraphEditorTabViewModel(editor)), Is.True);
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(window, nameof(ToolTabPlacementTests), "reopened");

            AssertSameBounds(PaneBounds(window, bottom), before, "it comes back where it was, at its size");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task An_emptied_default_dock_keeps_its_size_in_a_saved_layout()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-emptied-dock-saved");
        var window = new Window { Content = new EditView { DataContext = editor }, Width = 1200, Height = 700 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            IToolDock bottom = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)!;
            Rect before = PaneBounds(window, bottom);
            CloseTabsIn(editor, bottom);
            HeadlessTestHelpers.Render(3);

            Assert.That(editor.DockHost.ApplyLayout(editor.DockHost.CaptureLayout()), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.That(editor.OpenToolTab(new GraphEditorTabViewModel(editor)), Is.True);
            HeadlessTestHelpers.Render(3);

            AssertSameBounds(
                PaneBounds(window, editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)!),
                before,
                "the saved layout keeps the room it had before it emptied");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Tab_joins_the_dock_holding_tools_that_share_its_default_dock()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-shared-anchor");
        BeutlDockFactory factory = editor.DockHost.Factory;
        IToolDock bottom = factory.GetAnchoredDock(DockAnchor.Bottom)!;
        IToolDock right = factory.GetAnchoredDock(DockAnchor.Right)!;
        // The timeline, the bottom dock's only tab, is dragged over to the right dock.
        factory.MoveDockable(bottom, right, bottom.VisibleDockables!.OfType<BeutlToolDockable>().Single(), null);
        Assert.That(bottom.VisibleDockables, Is.Empty);

        var graphEditor = new GraphEditorTabViewModel(editor);
        Assert.That(editor.OpenToolTab(graphEditor), Is.True);

        Assert.That(DockableOf(editor, graphEditor).Owner, Is.SameAs(right), "it joins the timeline");
    }

    [AvaloniaTest]
    public async Task A_default_dock_holding_one_of_its_tools_comes_before_a_dock_used_since()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-own-dock-first");
        BeutlDockFactory factory = editor.DockHost.Factory;
        // The node graph shares the timeline's default dock, and was opened on the left after it.
        Assert.That(
            editor.DockHost.OpenToolTab(new NodeGraphTabViewModel(editor), factory.GetAnchoredDock(DockAnchor.Left)),
            Is.True);

        var graphEditor = new GraphEditorTabViewModel(editor);
        Assert.That(editor.OpenToolTab(graphEditor), Is.True);

        Assert.That(DockableOf(editor, graphEditor).Owner, Is.SameAs(factory.GetAnchoredDock(DockAnchor.Bottom)));
    }

    [AvaloniaTest]
    public async Task Among_other_docks_the_one_used_last_takes_the_tab()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-last-used");
        BeutlDockFactory factory = editor.DockHost.Factory;
        IToolDock bottom = factory.GetAnchoredDock(DockAnchor.Bottom)!;
        IToolDock left = factory.GetAnchoredDock(DockAnchor.Left)!;
        IToolDock right = factory.GetAnchoredDock(DockAnchor.Right)!;
        BeutlToolDockable timeline = bottom.VisibleDockables!.OfType<BeutlToolDockable>().Single();
        factory.MoveDockable(bottom, right, timeline, null);
        Assert.That(editor.DockHost.OpenToolTab(new NodeGraphTabViewModel(editor), left), Is.True);

        var graphEditor = new GraphEditorTabViewModel(editor);
        Assert.That(editor.OpenToolTab(graphEditor), Is.True);
        Assert.That(DockableOf(editor, graphEditor).Owner, Is.SameAs(left), "the node graph was used last");

        factory.SetFocusedDockable(right, timeline);
        var pathEditor = new PathEditorTabViewModel(editor);
        Assert.That(editor.OpenToolTab(pathEditor), Is.True);
        Assert.That(DockableOf(editor, pathEditor).Owner, Is.SameAs(right), "then the timeline was");
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(LayoutsSavedWithoutDefaultDocks))]
    public async Task A_layout_saved_without_default_docks_gets_them_back_where_the_default_layout_has_them(
        string name, int width, int height, JsonObject rootSplit)
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync($"tab-placement-{name}", width, height);
        string expected = Shape(editor.DockHost.Layout.Value);
        JsonObject layout = editor.DockHost.CaptureLayout();
        layout["DockLayout"] = new JsonObject
        {
            ["$type"] = "root",
            ["children"] = new JsonArray(rootSplit.DeepClone()),
        };

        Assert.That(editor.DockHost.ApplyLayout(layout), Is.True);

        BeutlDockFactory factory = editor.DockHost.Factory;
        Assert.Multiple(() =>
        {
            Assert.That(Shape(editor.DockHost.Layout.Value), Is.EqualTo(expected));
            // The layout holds no tab, so the default tabs open again, each in its own dock.
            Assert.That(factory.EnumerateTools(), Is.Not.Empty);
            foreach (BeutlToolDockable tool in factory.EnumerateTools())
            {
                Assert.That(
                    tool.Owner,
                    Is.SameAs(factory.GetAnchoredDock(tool.ToolContext.Extension.DefaultAnchor)),
                    tool.ToolContext.Extension.Name);
            }
        });
    }
}
