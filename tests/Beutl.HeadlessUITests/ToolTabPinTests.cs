using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.ElementPropertyTab;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.ObjectPropertyTab;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using Beutl.Views;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ToolTabPinTests
{
    private static async Task<EditViewModel> OpenEditor(string name)
    {
        await TestReset.ResetShellAsync();
        string root = System.IO.Path.Combine(BeutlHomeIsolation.CurrentHome!, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, name, root))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static async Task<Element> AddElement(EditViewModel editor, int layer, Func<EngineObject> create)
    {
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync(
            [new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(2), layer, new ElementSource.EngineObject(create))],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();
        return editor.Scene.Children.Single(e => e.ZIndex == layer);
    }

    private static IEditorSelection SelectionOf(EditViewModel editor)
    {
        return (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;
    }

    private static string PinnedHeader(string tabName, Element element)
    {
        return ToolTabHeaderHelper.Compose(tabName, ToolTabHeaderHelper.ElementLabel(element.Name, element));
    }

    [AvaloniaTest]
    public async Task Pinned_element_property_tab_keeps_its_element()
    {
        EditViewModel editor = await OpenEditor("pin-element-property");
        Element first = await AddElement(editor, 0, () => new RectShape());
        Element second = await AddElement(editor, 1, () => new EllipseShape());
        IEditorSelection selection = SelectionOf(editor);
        ElementPropertyTabViewModel tab = editor.FindToolTab<ElementPropertyTabViewModel>()!;

        selection.SelectedObject.Value = first;
        Assert.Multiple(() =>
        {
            Assert.That(tab.Element.Value, Is.SameAs(first));
            Assert.That(tab.HasTarget.Value, Is.True);
            Assert.That(tab.Header.Value, Is.EqualTo(Strings.ElementProperty));
        });

        tab.IsPinned.Value = true;
        selection.SelectedObject.Value = second;
        Assert.Multiple(() =>
        {
            Assert.That(tab.Element.Value, Is.SameAs(first), "a pinned tab ignores the selection");
            Assert.That(tab.Header.Value, Is.EqualTo(PinnedHeader(Strings.ElementProperty, first)));
        });

        tab.IsPinned.Value = false;
        Assert.Multiple(() =>
        {
            Assert.That(tab.Element.Value, Is.SameAs(first), "an unpinned tab keeps what it shows");
            Assert.That(tab.Header.Value, Is.EqualTo(Strings.ElementProperty));
        });
    }

    [AvaloniaTest]
    public async Task Removing_the_pinned_element_empties_and_unpins_the_tab()
    {
        EditViewModel editor = await OpenEditor("pin-element-removed");
        Element first = await AddElement(editor, 0, () => new RectShape());
        Element second = await AddElement(editor, 1, () => new EllipseShape());
        IEditorSelection selection = SelectionOf(editor);
        ElementPropertyTabViewModel tab = editor.FindToolTab<ElementPropertyTabViewModel>()!;
        selection.SelectedObject.Value = first;
        tab.IsPinned.Value = true;
        selection.SelectedObject.Value = second;

        editor.Scene.RemoveChild(first);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(tab.IsPinned.Value, Is.False);
            Assert.That(tab.Element.Value, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task Pinned_element_property_tab_comes_back_pinned_from_the_layout()
    {
        EditViewModel editor = await OpenEditor("pin-element-layout");
        Element first = await AddElement(editor, 0, () => new RectShape());
        var json = new JsonObject();
        using (var tab = new ElementPropertyTabViewModel(editor))
        {
            tab.Element.Value = first;
            tab.IsPinned.Value = true;
            tab.WriteToJson(json);
        }

        using var restored = new ElementPropertyTabViewModel(editor);
        restored.ReadFromJson(json);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Element.Value, Is.SameAs(first));
            Assert.That(restored.IsPinned.Value, Is.True);
        });

        editor.Scene.RemoveChild(first);
        HeadlessTestHelpers.Settle();
        using var orphaned = new ElementPropertyTabViewModel(editor);
        orphaned.ReadFromJson(json);
        Assert.Multiple(() =>
        {
            Assert.That(orphaned.IsPinned.Value, Is.False, "a tab whose element is gone comes back unpinned");
            Assert.That(orphaned.Element.Value, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task Pinned_object_property_tab_ignores_the_selection_and_is_not_reused()
    {
        EditViewModel editor = await OpenEditor("pin-object-property");
        Element first = await AddElement(editor, 0, () => new RectShape());
        Element second = await AddElement(editor, 1, () => new EllipseShape());
        EngineObject firstShape = first.Objects[0];
        EngineObject secondShape = second.Objects[0];
        IEditorSelection selection = SelectionOf(editor);
        selection.SelectedObject.Value = null;
        var tab = new ObjectPropertyTabViewModel(editor);
        Assert.That(editor.OpenToolTab(tab), Is.True);

        selection.SelectedObject.Value = firstShape;
        Assert.That(tab.ChildContext.Value?.Target, Is.SameAs(firstShape));
        tab.IsPinned.Value = true;
        Assert.Multiple(() =>
        {
            Assert.That(tab.CanBack.Value, Is.False, "going back would change what the pinned tab shows");
            Assert.That(ObjectPropertyTabViewModel.FindReusable(editor, firstShape), Is.SameAs(tab));
            Assert.That(ObjectPropertyTabViewModel.FindReusable(editor, secondShape), Is.Null,
                "navigating elsewhere opens another tab instead of retargeting the pinned one");
        });

        selection.SelectedObject.Value = secondShape;
        ObjectPropertyTabViewModel follower = editor.FindToolTab<ObjectPropertyTabViewModel>(t => !t.IsPinned.Value)!;
        Assert.Multiple(() =>
        {
            Assert.That(tab.ChildContext.Value?.Target, Is.SameAs(firstShape), "a pinned tab ignores the selection");
            Assert.That(follower, Is.Not.Null, "the selection opens another tab next to the pinned one");
            Assert.That(follower?.ChildContext.Value?.Target, Is.SameAs(secondShape));
            Assert.That(ObjectPropertyTabViewModel.FindReusable(editor, secondShape), Is.SameAs(follower));
        });

        tab.IsPinned.Value = false;
        Assert.That(tab.ChildContext.Value?.Target, Is.SameAs(firstShape), "an unpinned tab keeps what it shows");
        editor.CloseToolTab(follower);
        editor.CloseToolTab(tab);
    }

    [AvaloniaTest]
    public async Task Another_tab_of_an_open_tool_opens_beside_it()
    {
        EditViewModel editor = await OpenEditor("pin-tab-placement");
        IToolDock bottom = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)!;
        var first = new ObjectPropertyTabViewModel(editor);
        Assert.That(editor.DockHost.OpenToolTab(first, bottom), Is.True);

        var second = new ObjectPropertyTabViewModel(editor);
        Assert.That(editor.OpenToolTab(second), Is.True);

        BeutlToolDockable secondTab = editor.DockHost.Factory.EnumerateTools()
            .Single(tool => ReferenceEquals(tool.ToolContext, second));
        Assert.That(secondTab.Owner, Is.SameAs(bottom), "the second tab joins the first one's dock instead of the fallback dock");
        editor.CloseToolTab(second);
        editor.CloseToolTab(first);
    }

    [AvaloniaTest]
    public async Task Selecting_another_element_with_every_property_tab_pinned_opens_a_following_tab()
    {
        EditViewModel editor = await OpenEditor("pin-opens-follower");
        Element ellipse = await AddElement(editor, 0, () => new EllipseShape());
        IEditorSelection selection = SelectionOf(editor);
        selection.SelectedObject.Value = ellipse;
        BeutlToolDockable pinned = editor.DockHost.Factory.EnumerateTools()
            .Single(tool => tool.ToolContext is ElementPropertyTabViewModel);
        var pinnedTab = (ElementPropertyTabViewModel)pinned.ToolContext;
        Assert.That(pinnedTab.Element.Value, Is.SameAs(ellipse));
        pinnedTab.IsPinned.Value = true;

        selection.SelectedObject.Value = null;
        selection.SelectedObject.Value = ellipse;
        Assert.That(PropertyTabs(editor), Has.Length.EqualTo(1), "the pinned tab already shows the element");

        Element rect = await AddElement(editor, 1, () => new RectShape());
        var dock = (IDock)pinned.Owner!;
        var window = new Window { Content = new EditView { DataContext = editor }, Width = 1200, Height = 700 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            selection.SelectedObject.Value = rect;
            HeadlessTestHelpers.Settle();
            Capture(window, "follower");

            BeutlToolDockable[] tabs = PropertyTabs(editor);
            BeutlToolDockable? follower = tabs.SingleOrDefault(tool => !ReferenceEquals(tool, pinned));
            Assert.Multiple(() =>
            {
                Assert.That(tabs, Has.Length.EqualTo(2));
                Assert.That(pinnedTab.Element.Value, Is.SameAs(ellipse));
                Assert.That(follower?.Pinnable?.IsPinned.Value, Is.False);
                Assert.That(((ElementPropertyTabViewModel?)follower?.ToolContext)?.Element.Value, Is.SameAs(rect));
                Assert.That(follower?.Owner, Is.SameAs(dock), "the following tab opens in the pinned tab's dock");
                Assert.That(dock.ActiveDockable, Is.SameAs(follower), "the following tab comes to the front");
            });

            selection.SelectedObject.Value = ellipse;
            HeadlessTestHelpers.Settle();
            Capture(window, "pinned-forward");
            Assert.Multiple(() =>
            {
                Assert.That(dock.ActiveDockable, Is.SameAs(pinned), "selecting the pinned element brings its tab forward");
                Assert.That(((ElementPropertyTabViewModel?)follower?.ToolContext)?.Element.Value, Is.SameAs(rect),
                    "the other tab keeps what it shows");
            });

            selection.SelectedObject.Value = rect;
            Assert.Multiple(() =>
            {
                Assert.That(dock.ActiveDockable, Is.SameAs(follower), "the following tab comes back for another element");
                Assert.That(PropertyTabs(editor), Has.Length.EqualTo(2), "the following tab keeps following instead of multiplying");
            });

            IDockable other = dock.VisibleDockables!.First(d => !ReferenceEquals(d, pinned) && !ReferenceEquals(d, follower));
            editor.DockHost.Factory.SetActiveDockable(other);
            selection.SelectedObject.Value = ellipse;
            Assert.That(dock.ActiveDockable, Is.SameAs(pinned), "the pinned tab comes forward over a different tool too");

            editor.DockHost.Factory.SetActiveDockable(other);
            selection.SelectedObject.Value = rect;
            Assert.That(dock.ActiveDockable, Is.SameAs(follower), "the following tab comes forward over a different tool too");

            pinnedTab.IsPinned.Value = false;
            selection.SelectedObject.Value = rect;
            selection.SelectedObject.Value = ellipse;
            Assert.Multiple(() =>
            {
                Assert.That(dock.ActiveDockable, Is.SameAs(pinned), "the unpinned tab still showing the element comes forward");
                Assert.That(pinnedTab.Element.Value, Is.SameAs(ellipse));
            });
            selection.SelectedObject.Value = rect;
            Assert.Multiple(() =>
            {
                Assert.That(dock.ActiveDockable, Is.SameAs(follower), "the tab showing the element comes forward");
                Assert.That(pinnedTab.Element.Value, Is.SameAs(ellipse), "only one tab turns to a new selection");
                Assert.That(PropertyTabs(editor), Has.Length.EqualTo(2));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }

        static BeutlToolDockable[] PropertyTabs(EditViewModel editor)
        {
            return editor.DockHost.Factory.EnumerateTools()
                .Where(tool => tool.ToolContext is ElementPropertyTabViewModel)
                .ToArray();
        }
    }

    [AvaloniaTest]
    public async Task Selecting_an_element_with_every_element_property_tab_closed_opens_one()
    {
        EditViewModel editor = await OpenEditor("property-tab-reopens");
        Element element = await AddElement(editor, 0, () => new RectShape());
        IEditorSelection selection = SelectionOf(editor);
        selection.SelectedObject.Value = null;
        editor.CloseToolTab(editor.FindToolTab<ElementPropertyTabViewModel>()!);
        Assert.That(editor.FindToolTab<ElementPropertyTabViewModel>(), Is.Null);

        selection.SelectedObject.Value = element;

        BeutlToolDockable reopened = editor.DockHost.Factory.EnumerateTools()
            .Single(tool => tool.ToolContext is ElementPropertyTabViewModel);
        ToolTabExtension[] picked = editor.DockHost.Factory.EnumerateToolTabExtensions().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(((ElementPropertyTabViewModel)reopened.ToolContext).Element.Value, Is.SameAs(element));
            Assert.That(reopened.Owner, Is.SameAs(editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Right)),
                "with no tab to join, the tab opens in its tool's own dock");
            Assert.That(picked, Does.Not.Contain(ElementPropertyTabExtension.Instance),
                "selecting opens the element property tab, so the tool pickers leave it out");
            Assert.That(picked, Does.Not.Contain(ObjectPropertyTabExtension.Instance),
                "property editors open the property tab, so the tool pickers leave it out");
        });
    }

    [AvaloniaTest]
    public async Task Selecting_an_element_leaves_its_element_property_tab_in_front_of_the_property_tab()
    {
        EditViewModel editor = await OpenEditor("property-tabs-order");
        Element element = await AddElement(editor, 0, () => new RectShape());
        IEditorSelection selection = SelectionOf(editor);
        selection.SelectedObject.Value = null;
        IToolDock right = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Right)!;
        Assert.That(editor.DockHost.OpenToolTab(new ObjectPropertyTabViewModel(editor), right), Is.True);

        selection.SelectedObject.Value = element;

        Assert.Multiple(() =>
        {
            Assert.That(editor.FindToolTab<ObjectPropertyTabViewModel>()?.ChildContext.Value?.Target, Is.SameAs(element));
            Assert.That((right.ActiveDockable as BeutlToolDockable)?.ToolContext, Is.InstanceOf<ElementPropertyTabViewModel>());
        });
    }

    [AvaloniaTest]
    public async Task Clicking_the_selected_element_reopens_a_closed_element_property_tab()
    {
        EditViewModel editor = await OpenEditor("property-tab-reclick");
        Element element = await AddElement(editor, 0, () => new RectShape());
        SelectionOf(editor).SelectedObject.Value = element;
        var window = new Window { Content = new EditView { DataContext = editor }, Width = 1200, Height = 700 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            editor.CloseToolTab(editor.FindToolTab<ElementPropertyTabViewModel>()!);
            HeadlessTestHelpers.Render(3);
            Assert.That(editor.FindToolTab<ElementPropertyTabViewModel>(), Is.Null);

            ElementView view = window.GetVisualDescendants().OfType<ElementView>()
                .Single(v => ((ElementViewModel)v.DataContext!).Model == element);
            Point center = view.border.TranslatePoint(
                new Point(view.border.Bounds.Width / 2, view.border.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            HeadlessTestHelpers.Settle();

            Assert.That(editor.FindToolTab<ElementPropertyTabViewModel>()?.Element.Value, Is.SameAs(element),
                "the selection did not change, but the click still reopens the tab");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Changing_the_layout_shows_the_selection_in_the_new_property_tab()
    {
        EditViewModel editor = await OpenEditor("property-tab-layout-change");
        Element element = await AddElement(editor, 0, () => new RectShape());
        SelectionOf(editor).SelectedObject.Value = element;

        editor.DockHost.ResetLayout();
        Assert.That(editor.FindToolTab<ElementPropertyTabViewModel>()?.Element.Value, Is.SameAs(element),
            "after resetting the layout");

        IToolDock right = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Right)!;
        editor.DockHost.Factory.SetActiveDockable(right.VisibleDockables!
            .First(d => d is not BeutlToolDockable { ToolContext: ElementPropertyTabViewModel }));
        Assert.That(editor.DockHost.ApplyLayout(editor.DockHost.CaptureLayout()), Is.True);
        IToolDock restoredRight = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Right)!;
        Assert.Multiple(() =>
        {
            Assert.That(editor.FindToolTab<ElementPropertyTabViewModel>()?.Element.Value, Is.SameAs(element),
                "after applying a saved layout");
            Assert.That((restoredRight.ActiveDockable as BeutlToolDockable)?.ToolContext,
                Is.Not.InstanceOf<ElementPropertyTabViewModel>(), "the layout keeps the tab it put in front");
        });
    }

    [AvaloniaTest]
    public async Task Pinned_transition_tabs_name_their_edge()
    {
        EditViewModel editor = await OpenEditor("pin-transition-edge");
        Element element = await AddElement(editor, 0, () => new RectShape());
        using var start = new TransitionTabViewModel(editor);
        using var end = new TransitionTabViewModel(editor);
        start.Show(element, ElementEdge.Start);
        end.Show(element, ElementEdge.End);

        start.IsPinned.Value = true;
        end.IsPinned.Value = true;

        string label = ToolTabHeaderHelper.ElementLabel(element.Name, element);
        Assert.Multiple(() =>
        {
            Assert.That(start.Header.Value, Is.EqualTo(ToolTabHeaderHelper.Compose(Strings.EnterTransition, label)));
            Assert.That(end.Header.Value, Is.EqualTo(ToolTabHeaderHelper.Compose(Strings.ExitTransition, label)));
        });
    }

    [AvaloniaTest]
    public async Task A_pinned_object_property_tab_takes_its_editors_services_from_itself()
    {
        EditViewModel editor = await OpenEditor("pin-object-property-services");
        Element element = await AddElement(editor, 0, () => new RectShape());
        using var tab = new ObjectPropertyTabViewModel(editor);
        tab.NavigateCore(element.Objects[0], false, new DisposedSourceEditor());
        var property = (IServiceProvider)tab.ChildContext.Value!.Properties.First(p => p is IServiceProvider);
        Assert.That(property.GetService(typeof(Element)), Is.Null);

        tab.IsPinned.Value = true;

        Assert.That(property.GetService(typeof(Element)), Is.SameAs(element),
            "a pinned tab outlives the editor that opened its object");
    }

    [AvaloniaTest]
    public async Task Removing_the_pinned_graph_empties_and_unpins_the_node_graph_tab()
    {
        EditViewModel editor = await OpenEditor("pin-node-graph-removed");
        var graph = new GraphModel();
        Element element = await AddElement(editor, 0, () => new NodeGraphDrawable { Model = { CurrentValue = graph } });
        using var tab = new NodeGraphTabViewModel(editor);
        tab.Model.Value = graph;
        tab.IsPinned.Value = true;

        editor.Scene.RemoveChild(element);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(tab.Model.Value, Is.Null);
            Assert.That(tab.IsPinned.Value, Is.False);
        });
    }

    // Stands in for the property editor that opened an object, after it was disposed: it answers nothing.
    private sealed class DisposedSourceEditor : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [AvaloniaTest]
    public async Task Pinned_path_editor_keeps_its_figure_when_the_property_tab_moves_on()
    {
        EditViewModel editor = await OpenEditor("pin-path-editor");
        var figure = new PathFigure();
        figure.Segments.Add(new LineSegment(80, 100));
        figure.Segments.Add(new LineSegment(180, 100));
        Element shaped = await AddElement(editor, 0,
            () => new GeometryShape { Data = { CurrentValue = new PathGeometry { Figures = { figure } } } });
        Element other = await AddElement(editor, 1, () => new RectShape());
        IEditorSelection selection = SelectionOf(editor);
        ElementPropertyTabViewModel properties = editor.FindToolTab<ElementPropertyTabViewModel>()!;
        selection.SelectedObject.Value = shaped;
        GeometryEditorViewModel geometry = properties.Items
            .SelectMany(item => item.Properties)
            .OfType<GeometryEditorViewModel>()
            .Single();
        geometry.ExpandForEditing();
        IPathFigureEditorContext lent = geometry.FindPathFigureContext(figure)!;
        var tab = new PathEditorTabViewModel(editor);
        Assert.That(editor.OpenToolTab(tab), Is.True);
        tab.StartOrFinishEdit(lent);

        tab.IsPinned.Value = true;
        Assert.Multiple(() =>
        {
            Assert.That(tab.FigureContext.Value, Is.Not.SameAs(lent), "pinning swaps in editors the tab owns");
            Assert.That(tab.PathFigure.Value, Is.SameAs(figure));
            Assert.That(tab.Element.Value, Is.SameAs(shaped));
            Assert.That(tab.Header.Value, Is.EqualTo(PinnedHeader(Strings.PathEditor, shaped)));
        });

        selection.SelectedObject.Value = other;
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(properties.Element.Value, Is.SameAs(other));
            Assert.That(tab.IsPinned.Value, Is.True);
            Assert.That(tab.PathFigure.Value, Is.SameAs(figure), "the figure outlives the editors the property tab lent");
            Assert.That(PathEditorTabViewModel.FindReusable(editor, figure), Is.SameAs(tab));
            Assert.That(PathEditorTabViewModel.FindReusable(editor, new PathFigure()), Is.Null);
        });

        tab.FinishEdit();
        Assert.Multiple(() =>
        {
            Assert.That(tab.FigureContext.Value, Is.Null);
            Assert.That(tab.IsPinned.Value, Is.False, "an empty tab is never left pinned");
        });
        editor.CloseToolTab(tab);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Tab_menu_pins_the_tab_and_its_header_button_unpins_it(bool light)
    {
        EditViewModel editor = await OpenEditor("pin-tab-header");
        Element element = await AddElement(editor, 0, () => new RectShape());
        SelectionOf(editor).SelectedObject.Value = element;
        BeutlToolDockable dockable = editor.DockHost.Factory.EnumerateTools()
            .Single(tool => tool.ToolContext is ElementPropertyTabViewModel);
        IPinnableToolContext pinnable = dockable.Pinnable!;

        var view = new EditView { DataContext = editor };
        var window = new Window
        {
            Content = view,
            Width = 1200,
            Height = 700,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ToolTabStripItem tab = view.GetVisualDescendants()
                .OfType<ToolTabStripItem>()
                .Single(item => ReferenceEquals(item.DataContext, dockable));
            Button unpin = tab.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_TabUnpinButton");
            Assert.That(unpin.IsVisible, Is.False);

            Control menuHost = tab.GetVisualDescendants().OfType<Control>().First(c => c.ContextMenu != null);
            ContextMenu menu = menuHost.ContextMenu!;
            menu.Open(menuHost);
            HeadlessTestHelpers.Render();
            MenuItem pinItem = menu.Items.OfType<MenuItem>().First();
            Assert.Multiple(() =>
            {
                Assert.That(pinItem.Header, Is.EqualTo(Strings.PinTab));
                Assert.That(pinItem.IsVisible, Is.True);
                Assert.That(pinItem.IsEffectivelyEnabled, Is.True);
                Assert.That(pinItem.IsChecked, Is.False);
            });
            Capture(TopLevel.GetTopLevel(pinItem)!, $"menu-{(light ? "light" : "dark")}");

            // The menu toggles a check item only on a real click, not on a raised Click event.
            TopLevel popupRoot = TopLevel.GetTopLevel(pinItem)!;
            Point itemPoint = pinItem.TranslatePoint(new Point(16, pinItem.Bounds.Height / 2), popupRoot)!.Value;
            popupRoot.MouseMove(itemPoint);
            popupRoot.MouseDown(itemPoint, MouseButton.Left);
            popupRoot.MouseUp(itemPoint, MouseButton.Left);
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(menu.IsOpen, Is.False);
                Assert.That(pinnable.IsPinned.Value, Is.True);
                Assert.That(unpin.IsVisible, Is.True);
                Assert.That(dockable.Title, Is.EqualTo(PinnedHeader(Strings.ElementProperty, element)));
                Assert.That(ToolTip.GetTip(unpin), Is.EqualTo(Strings.UnpinTab));
            });
            Capture(window, $"pinned-{(light ? "light" : "dark")}");

            Point center = unpin.TranslatePoint(new Point(unpin.Bounds.Width / 2, unpin.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center);
            HeadlessTestHelpers.Settle();
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(pinnable.IsPinned.Value, Is.False);
                Assert.That(unpin.IsVisible, Is.False);
                Assert.That(dockable.Title, Is.EqualTo(Strings.ElementProperty));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Tools_that_cannot_be_pinned_hide_the_menu_item()
    {
        EditViewModel editor = await OpenEditor("pin-tab-unpinnable");
        BeutlToolDockable timeline = editor.DockHost.Factory.EnumerateTools()
            .Single(tool => tool.ToolContext.Extension == Beutl.Services.PrimitiveImpls.TimelineTabExtension.Instance);

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ToolTabStripItem tab = view.GetVisualDescendants()
                .OfType<ToolTabStripItem>()
                .Single(item => ReferenceEquals(item.DataContext, timeline));
            Control menuHost = tab.GetVisualDescendants().OfType<Control>().First(c => c.ContextMenu != null);
            ContextMenu menu = menuHost.ContextMenu!;
            menu.Open(menuHost);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(timeline.Pinnable, Is.Null);
                Assert.That(menu.Items.OfType<MenuItem>().First().IsVisible, Is.False);
                Assert.That(menu.Items.OfType<Separator>().First().IsVisible, Is.False);
            });
            menu.Close();
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static void Capture(TopLevel topLevel, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("BEUTL_TOOL_TAB_PIN_CAPTURE_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        HeadlessTestHelpers.Render();
        using var frame = topLevel.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        frame!.Save(System.IO.Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
