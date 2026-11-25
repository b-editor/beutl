using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Components.TransitionTab.ViewModels;
using Beutl.Editor.Components.UndisplayableDataTab;
using Beutl.Editor.Components.UndisplayableDataTab.ViewModels;
using Beutl.Editor.Components.UndisplayableDataTab.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class UndisplayableDataToolTests
{
    private const string Missing = "[Missing.Plugin]Missing.Namespace:MissingObject";

    [AvaloniaTest]
    public async Task KeptDataNoticeOpensTheFirstAffectedSceneAndTheToolLocatesAcrossScenes()
    {
        await TestReset.ResetShellAsync();
        string directory = TestShell.NewWorkspace($"undisplayable-notice-{Guid.NewGuid():N}");
        var projectUri = new Uri(Path.Combine(directory, "project.bep"));
        Scene first = SceneFile(directory, "First");
        Scene other = SceneFile(directory, "Affected");
        Element clip = AddClip(other, new RectShape());
        var project = new Project { Uri = projectUri };
        project.Items.Add(first);
        project.Items.Add(other);
        project.KeepUnknownMembers(new JsonObject { ["FutureProjectSetting"] = 42 });
        CoreSerializer.StoreToUri(project, projectUri);
        RewriteClip(clip, json => json[nameof(Element.Objects)]![0]!["$type"] = Missing);

        using var notifications = new NotificationCapture(TestShell.Notifications);
        await TestShell.Project.OpenProject(projectUri.LocalPath);
        var scenes = TestShell.Project.CurrentProject.Value!.Items.OfType<Scene>().ToArray();
        TestShell.Editor.ActivateTabItem(scenes[0]);
        EditViewModel firstEditor = ActiveEditor;
        Window? window = null;
        try
        {
            var action = notifications.All.Single(item => item.Title == MessageStrings.KeptUnknownDataTitle).Actions!.Single();
            Assert.That(action.Text, Is.EqualTo(UndisplayableDataStrings.ShowDetails));
            action.Callback();
            Assert.That(ActiveEditor.Scene, Is.SameAs(scenes[1]));
            var opened = ActiveEditor.FindToolTab<UndisplayableDataViewModel>();
            Assert.That(opened, Is.Not.Null);
            action.Callback();
            Assert.That(ActiveEditor.FindToolTab<UndisplayableDataViewModel>(), Is.SameAs(opened));

            TestShell.Editor.ActivateTabItem(scenes[0]);
            var main = TestShell.MainViewModel;
            Assert.That(main.ToolTabExtensions.Single(extension => extension == UndisplayableDataTabExtension.Instance).Header,
                Is.EqualTo(UndisplayableDataStrings.Title));
            var command = main.MenuBar.EnumeratePaletteCommands().Single(item => item.Id == "MenuBar.ShowUndisplayableData");
            await MenuBarViewModel.ExecuteCommandAsync(command.Command);
            var tool = firstEditor.FindToolTab<UndisplayableDataViewModel>()!;
            window = ShowEditor(firstEditor);
            var view = window.GetVisualDescendants().OfType<UndisplayableDataView>().Single();
            var projectRow = tool.Rows.Value.Single(row => row.Entry.TargetObject is Project);
            var scopeSwitch = view.FindControl<ToggleSwitch>("ScopeSwitch")!;
            Assert.Multiple(() =>
            {
                Assert.That(tool.Rows.Value, Has.Count.EqualTo(2));
                Assert.That(scopeSwitch.IsVisible, Is.True);
                Assert.That(projectRow.CanLocate, Is.False);
                Assert.That(RowControl<Button>(window, projectRow, "LocateButton").IsVisible, Is.False);
            });
            Assert.That(projectRow.LoadPreview(), Does.Contain("42"));
            TestShell.Project.CurrentProject.Value!.KeepUnknownMembers(new JsonObject { ["FutureProjectSetting"] = 43 });
            HeadlessTestHelpers.Click(view.FindControl<Button>("RefreshButton")!);
            Assert.That(projectRow.Preview.Value, Does.Contain("43"));
            HeadlessTestHelpers.Click(scopeSwitch);
            Assert.That(tool.Rows.Value, Is.Empty);
            Assert.That(view.FindControl<Beutl.Controls.EmptyState>("EmptyState")!.IsEffectivelyVisible, Is.True);
            HeadlessTestHelpers.Click(scopeSwitch);
            var row = tool.Rows.Value.Single(item => item.CanLocate);
            HeadlessTestHelpers.Click(RowControl<Button>(window, row, "LocateButton"));
            Assert.That(ActiveEditor.Scene, Is.SameAs(scenes[1]));
            Assert.That(ActiveEditor.GetRequiredService<IEditorSelection>().SelectedObject.Value,
                Is.SameAs(scenes[1].Children.Single()));
        }
        finally
        {
            window?.Close();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task RecoveryNoticeOpensDetailsAndLocatesTheKeyframe(bool globalClock)
    {
        await TestReset.ResetShellAsync();
        var project = await TestShell.CreateProjectAsync($"undisplayable-easing-{Guid.NewGuid():N}");
        Scene scene = project.Items.OfType<Scene>().Single();
        var shape = new RectShape { Name = { Value = "Rectangle" } };
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = { Value = globalClock } };
        MediaTime keyTime = MediaTime.FromSeconds(globalClock ? 3.5 : 1.5);
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = keyTime }, Value = { Value = 60 } });
        shape.Width.SetKeyFrameAnimation(animation);
        Element clip = AddClip(scene, shape);
        CoreSerializer.StoreToUri(project, project.Uri!);
        RewriteClip(clip, json => json[nameof(Element.Objects)]![0]!["Animations"]![nameof(RectShape.Width)]!["KeyFrames"]![0]!["Easing"] = Missing);

        using var notifications = new NotificationCapture(TestShell.Notifications);
        var editor = await TestShell.ReopenAsync(project.Uri!.LocalPath);
        Window? window = null;
        try
        {
            var action = notifications.All.Single(item => item.Title == MessageStrings.RecoveryProtectedElementsTitle).Actions!.Single();
            action.Callback();
            var tool = editor.FindToolTab<UndisplayableDataViewModel>()!;
            var row = tool.Rows.Value.Single();
            clip = editor.Scene.Children.Single();
            window = ShowEditor(editor);
            Assert.That(row.Entry.Kind, Is.EqualTo(UndisplayableDataKind.LossyEasing));

            var details = RowControl<ToggleButton>(window, row, "DetailsToggle");
            HeadlessTestHelpers.Click(details, ticks: 4);
            var preview = RowControl<TextBox>(window, row, "JsonPreview");
            Assert.That(preview.IsReadOnly, Is.True);
            Assert.That(preview.Text, Does.Contain(Missing));
            var copy = RowControl<Button>(window, row, "CopyButton");
            await HeadlessTestHelpers.WaitUntilAsync(() =>
            {
                copy.BringIntoView();
                HeadlessTestHelpers.Render();
                return window.InputHitTest(HeadlessTestHelpers.Center(copy, window)) is Visual hit
                    && (hit == copy || copy.IsVisualAncestorOf(hit));
            });
            HeadlessTestHelpers.Click(copy);
            Assert.That(await window.Clipboard!.TryGetTextAsync(), Is.EqualTo(preview.Text));
            if (!globalClock)
            {
                preview.BringIntoView();
                HeadlessTestHelpers.Render(3);
                HeadlessCapture.SaveIfRequested(window, nameof(UndisplayableDataToolTests), "recovery-details");
            }

            details.BringIntoView();
            HeadlessTestHelpers.Render(3);
            HeadlessTestHelpers.Click(details, ticks: 3);
            RowControl<Button>(window, row, "LocateButton").BringIntoView();
            HeadlessTestHelpers.Render(3);
            HeadlessTestHelpers.Click(RowControl<Button>(window, row, "LocateButton"));
            var graphEditor = editor.FindToolTab<GraphEditorTabViewModel>()!;
            Assert.Multiple(() =>
            {
                Assert.That(graphEditor.Element.Value, Is.SameAs(clip));
                Assert.That(graphEditor.SelectedItem.Value?.Object, Is.SameAs(row.Entry.Animation));
                Assert.That(editor.GetRequiredService<IEditorClock>().CurrentTime.Value,
                    Is.EqualTo(globalClock ? keyTime : clip.Start.Value + keyTime));
            });
        }
        finally
        {
            window?.Close();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task LocatingANestedPropertyFlashesItAndRepairClearsTheToolAndTimelineBadge()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("undisplayable-badge");
        var fallback = new FallbackBrush { Json = new JsonObject { ["$type"] = Missing } };
        var shape = new RectShape { Fill = { Value = fallback } };
        Element clip = AddClip(scene, shape);
        editor.OpenUndisplayableData();
        var tool = editor.FindToolTab<UndisplayableDataViewModel>()!;
        var window = ShowEditor(editor, ThemeVariant.Dark);
        try
        {
            var clipView = window.GetVisualDescendants().OfType<ElementView>().Single();
            var clipModel = (ElementViewModel)clipView.DataContext!;
            var dataBadge = clipView.FindControl<Control>("undisplayableDataIcon")!;
            var recoveryBadge = clipView.FindControl<Control>("recoveryIcon")!;
            Assert.Multiple(() =>
            {
                Assert.That(dataBadge.IsEffectivelyVisible, Is.True);
                Assert.That(recoveryBadge.IsVisible, Is.False);
                Assert.That(clipModel.ClipToolTip.Value, Does.Contain(UndisplayableDataStrings.TypeNotFound));
            });
            HeadlessCapture.SaveIfRequested(window, nameof(UndisplayableDataToolTests), "editable-clip-badge");

            var flashedProperties = new List<object?>();
            using var flashing = AdornerLayer.AdornedElementProperty.Changed.Subscribe(e =>
            {
                if (e.NewValue.GetValueOrDefault() is Control target && target.FindAncestorOfType<ElementPropertyTabView>() is not null)
                    flashedProperties.Add(target.DataContext);
            });
            HeadlessTestHelpers.Click(RowControl<Button>(window, tool.Rows.Value.Single(), "LocateButton"));
            var properties = editor.FindToolTab<ElementPropertyTabViewModel>()!;
            var fillEditor = properties.Items.Single().FindPropertyEditor(shape, nameof(Shape.Fill));
            Assert.That(fillEditor, Is.Not.Null, "The Fill property must have a revealable editor.");
            await HeadlessTestHelpers.WaitUntilAsync(() => flashedProperties.Contains(fillEditor));
            Assert.Multiple(() =>
            {
                Assert.That(editor.GetRequiredService<IEditorSelection>().SelectedObject.Value, Is.SameAs(clip));
                Assert.That(editor.GetRequiredService<IEditorClock>().CurrentTime.Value, Is.EqualTo(clip.Start.Value));
            });

            shape.Fill.Value = new SolidColorBrush();
            await HeadlessTestHelpers.WaitUntilAsync(() => !dataBadge.IsVisible && tool.Rows.Value.Count == 0);
            Assert.That(clipModel.ClipToolTip.Value, Is.Null);
        }
        finally
        {
            window.Close();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task LocateOpensTheTransitionAndCentersTheNodeInsideItsNestedGraph()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("undisplayable-graph");
        var root = new GraphModel();
        var group = new GroupNode();
        root.Nodes.Add(group);
        var missingNode = new FallbackGraphNode
        {
            Name = { Value = "Unavailable node" },
            Position = { Value = (1600, 900) },
            Json = new JsonObject { ["$type"] = Missing },
        };
        group.Group.Value.Nodes.Add(missingNode);
        Element clip = AddClip(scene, new NodeGraphDrawable { Model = { Value = root } });
        var transition = new FallbackClipTransition { Json = new JsonObject { ["$type"] = Missing } };
        clip.EnterTransition.Value = transition;
        editor.OpenUndisplayableData();
        var tool = editor.FindToolTab<UndisplayableDataViewModel>()!;
        var window = ShowEditor(editor, ThemeVariant.Dark);
        try
        {
            var transitionRow = tool.Rows.Value.Single(row => row.Entry.TargetObject == transition);
            HeadlessTestHelpers.Click(RowControl<Button>(window, transitionRow, "LocateButton"));
            var transitionEditor = editor.FindToolTab<TransitionTabViewModel>()!;
            Assert.That(transitionEditor.IsShowing(clip, ElementEdge.Start), Is.True);
            Assert.That(transitionEditor.Transition.Value, Is.SameAs(transition));

            editor.OpenUndisplayableData();
            HeadlessTestHelpers.Render(4);
            var nodeRow = tool.Rows.Value.Single(row => row.Entry.GraphNode == missingNode);
            HeadlessTestHelpers.Click(RowControl<Button>(window, nodeRow, "LocateButton"), ticks: 5);
            var graphTab = editor.FindToolTab<NodeGraphTabViewModel>()!;
            var graph = graphTab.NodeGraph.Value!;
            Assert.That(graph.NodeGraph, Is.SameAs(group.Group.Value));
            Assert.That(graph.Nodes.Single().IsSelected.Value, Is.True);

            var graphView = window.GetVisualDescendants().OfType<NodeGraphView>().Single(view => view.IsEffectivelyVisible);
            var zoom = graphView.FindControl<ZoomBorder>("zoomBorder")!;
            var node = graphView.GetVisualDescendants().OfType<GraphNodeView>().Single();
            await HeadlessTestHelpers.WaitUntilAsync(() => graph.PendingReveal.Value is null);
            Point center = HeadlessTestHelpers.Center(node, zoom);
            Assert.Multiple(() =>
            {
                Assert.That(center.X, Is.EqualTo(zoom.Bounds.Width / 2).Within(1));
                Assert.That(center.Y, Is.EqualTo(zoom.Bounds.Height / 2).Within(1));
            });
            HeadlessCapture.SaveIfRequested(window, nameof(UndisplayableDataToolTests), "nested-node");
        }
        finally
        {
            window.Close();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task SceneLayerAndMarkerRowsOpenSettingsOrScrollTheTimeline()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("undisplayable-scene");
        scene.KeepUnknownMembers(new JsonObject { ["FutureScene"] = 1 });
        var layer = new TimelineLayer { ZIndex = { Value = 3 } };
        layer.KeepUnknownMembers(new JsonObject { ["FutureLayer"] = true });
        scene.Layers.Add(layer);
        var marker = new SceneMarker(MediaTime.FromSeconds(8));
        marker.KeepUnknownMembers(new JsonObject { ["FutureMarker"] = "marker" });
        scene.Markers.Add(marker);
        editor.OpenUndisplayableData();
        var tool = editor.FindToolTab<UndisplayableDataViewModel>()!;
        var window = ShowEditor(editor);
        try
        {
            tool.Locate(tool.Rows.Value.Single(row => row.Entry.TargetObject == scene));
            Assert.That(editor.FindToolTab<SceneSettingsTabViewModel>(), Is.Not.Null);
            var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
            (TimeRange Range, int Layer)? scroll = null;
            using var subscription = timeline.ScrollTo.Subscribe(value => scroll = value);
            tool.Locate(tool.Rows.Value.Single(row => row.Entry.TargetObject == layer));
            await HeadlessTestHelpers.WaitUntilAsync(() => scroll is not null);
            Assert.That(scroll?.Layer, Is.EqualTo(layer.ZIndex.Value));
            scroll = null;
            tool.Locate(tool.Rows.Value.Single(row => row.Entry.TargetObject == marker));
            await HeadlessTestHelpers.WaitUntilAsync(() => scroll is not null);
            Assert.That(scroll?.Range.Start, Is.EqualTo(marker.Time.Value));
        }
        finally
        {
            window.Close();
            await TestReset.ResetShellAsync();
        }
    }

    private static EditViewModel ActiveEditor => (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

    private static Scene SceneFile(string directory, string name)
        => new(320, 240, name) { Uri = new Uri(Path.Combine(directory, name, name + ".scene")) };

    private static Element AddClip(Scene scene, Beutl.Engine.EngineObject obj)
    {
        var clip = new Element
        {
            Name = { Value = "Clip with unavailable data" },
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, $"{Guid.NewGuid():N}.belm")),
            Start = { Value = MediaTime.FromSeconds(2) },
            Length = { Value = MediaTime.FromSeconds(4) },
        };
        clip.AddObject(obj);
        scene.Children.Add(clip);
        return clip;
    }

    private static void RewriteClip(Element clip, Action<JsonObject> edit)
    {
        string path = clip.Uri!.LocalPath;
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(json);
        File.WriteAllText(path, json.ToJsonString());
    }

    private static Window ShowEditor(EditViewModel editor, ThemeVariant? theme = null)
    {
        var window = new Window
        {
            Content = new EditView { DataContext = editor },
            Width = 1280,
            Height = 900,
            RequestedThemeVariant = theme ?? ThemeVariant.Light,
        };
        window.Show();
        HeadlessTestHelpers.Render(5);
        return window;
    }

    private static T RowControl<T>(Window window, UndisplayableDataRow row, string name) where T : Control
        => window.GetVisualDescendants().OfType<T>().Single(item => item.Name == name && item.DataContext == row);
}
