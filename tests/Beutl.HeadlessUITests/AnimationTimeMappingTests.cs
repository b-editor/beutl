using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using DrawableGroup = Beutl.Graphics.DrawableGroup;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class AnimationTimeMappingTests
{
    [AvaloniaTest]
    [TestCase(0)]
    [TestCase(2)]
    public async Task Editors_seek_insert_and_place_keys_using_the_animations_owner(int origin)
    {
        await TestReset.ResetShellAsync();
        using var graph = await GraphScope.CreateAsync(elementStart: MediaTime.FromSeconds(5));
        var editor = (EditViewModel)graph.Model.EditorContext;
        Element element = graph.Model.Element!;
        var shape = element.Objects.OfType<RectShape>().Single();
        shape.IsTimeAnchor.Value = true;
        shape.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(origin), MediaTime.FromSeconds(10));
        var adapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Width, shape);
        var clock = (IEditorClock)editor.GetService(typeof(IEditorClock))!;
        using var property = new NumberEditorViewModel<float>(adapter);
        property.Accept(new PropertyEditorTestHost.ServiceVisitor(
            (typeof(Element), element), (typeof(Scene), editor.Scene), (typeof(IEditorClock), clock)));
        var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.AttachInline(adapter, element);
        InlineAnimationLayerViewModel inline = timeline.Inlines.Single(x => ReferenceEquals(x.Property, adapter));
        try
        {
            property.KeyFrameIndex.Value = 1;
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(origin + 1.5)));
            clock.Seek(MediaTime.FromSeconds(origin + 0.5));
            Assert.That(property.IsAtKeyFrame.Value, Is.True);
            Assert.That(property.KeyFrameIndex.Value, Is.Zero);
            Assert.That(graph.Model.ConvertKeyTime(clock.CurrentTime.Value), Is.EqualTo(graph.First.KeyTime.Value));
            Assert.That(inline.ConvertKeyTime(clock.CurrentTime.Value, graph.Animation), Is.EqualTo(graph.First.KeyTime.Value));
            Assert.That(graph.Model.Margin.Value.Left, Is.EqualTo(origin * 150));
            Assert.That(inline.LeftMargin.Value.Left, Is.EqualTo(origin * 150));

            graph.Model.InsertKeyFrame(new Beutl.Animation.Easings.SplineEasing(), MediaTime.FromSeconds(origin + 1));
            Assert.That(graph.Animation.KeyFrames.Select(k => k.KeyTime.Value), Does.Contain(MediaTime.FromSeconds(1)));
            shape.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(3), MediaTime.FromSeconds(10));
            HeadlessTestHelpers.Settle();
            Assert.That(graph.Model.Margin.Value.Left, Is.EqualTo(450));
            Assert.That(inline.LeftMargin.Value.Left, Is.EqualTo(450));
            graph.Animation.UseGlobalClock.Value = true;
            Assert.That(graph.Model.Margin.Value.Left, Is.Zero);
            Assert.That(inline.LeftMargin.Value.Left, Is.Zero);
            property.KeyFrameIndex.Value = 2;
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(1.5)));
        }
        finally
        {
            timeline.DetachInline(inline);
        }
    }

    [AvaloniaTest]
    public async Task Enabling_animation_marks_the_initial_key_after_attachment()
    {
        await TestReset.ResetShellAsync();
        using var graph = await GraphScope.CreateAsync(elementStart: MediaTime.FromSeconds(5));
        var editor = (EditViewModel)graph.Model.EditorContext;
        Element element = graph.Model.Element!;
        var shape = element.Objects.OfType<RectShape>().Single();
        var adapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Height, shape);
        var clock = (IEditorClock)editor.GetService(typeof(IEditorClock))!;
        clock.Seek(element.Start.Value);
        using var property = new NumberEditorViewModel<float>(adapter);
        property.Accept(new PropertyEditorTestHost.ServiceVisitor(
            (typeof(Element), element), (typeof(Scene), editor.Scene), (typeof(IEditorClock), clock),
            (typeof(HistoryManager), editor.HistoryManager)));

        property.PrepareToEditAnimation();

        Assert.Multiple(() =>
        {
            Assert.That(adapter.Animation!.KeyFrames.Single().KeyTime.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(property.IsAtKeyFrame.Value, Is.True);
            Assert.That(property.KeyFrameIndex.Value, Is.Zero);
            Assert.That(property.EditingKeyFrame.Value, Is.SameAs(adapter.Animation.KeyFrames[0]));
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(element.Start.Value));
        });
    }

    [AvaloniaTest]
    public async Task The_indicator_tracks_clock_mode_and_owner_moves_without_seeking()
    {
        await TestReset.ResetShellAsync();
        using var graph = await GraphScope.CreateAsync(elementStart: MediaTime.FromSeconds(5));
        var editor = (EditViewModel)graph.Model.EditorContext;
        Element element = graph.Model.Element!;
        var shape = element.Objects.OfType<RectShape>().Single();
        var adapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Width, shape);
        var clock = (IEditorClock)editor.GetService(typeof(IEditorClock))!;
        using var property = new NumberEditorViewModel<float>(adapter);
        property.Accept(new PropertyEditorTestHost.ServiceVisitor(
            (typeof(Element), element), (typeof(Scene), editor.Scene), (typeof(IEditorClock), clock)));
        clock.Seek(MediaTime.FromSeconds(5.5));
        Assert.That(property.IsAtKeyFrame.Value, Is.True);

        graph.Animation.UseGlobalClock.Value = true;
        Assert.That(property.IsAtKeyFrame.Value, Is.False);
        Assert.That(property.KeyFrameIndex.Value, Is.EqualTo(1.5f));
        Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(5.5)));
        clock.Seek(MediaTime.FromSeconds(1.5));
        Assert.That(property.IsAtKeyFrame.Value, Is.True);
        Assert.That(property.KeyFrameIndex.Value, Is.EqualTo(1));

        graph.Animation.UseGlobalClock.Value = false;
        Assert.That(property.IsAtKeyFrame.Value, Is.False);
        Assert.That(property.KeyFrameIndex.Value, Is.EqualTo(-0.5f));
        shape.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(1), shape.Duration);
        Assert.That(property.IsAtKeyFrame.Value, Is.True);
        Assert.That(property.KeyFrameIndex.Value, Is.Zero);
        Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(1.5)));
    }

    [AvaloniaTest]
    public async Task Replacement_inline_keys_follow_the_owner_after_attachment()
    {
        await TestReset.ResetShellAsync();
        using var graph = await GraphScope.CreateAsync(elementStart: MediaTime.FromSeconds(5));
        var editor = (EditViewModel)graph.Model.EditorContext;
        Element element = graph.Model.Element!;
        var shape = element.Objects.OfType<RectShape>().Single();
        shape.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(2), shape.Duration);
        var adapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Width, shape);
        var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.Options.Value = timeline.Options.Value with { Scale = 1 };
        timeline.AttachInline(adapter, element);
        InlineAnimationLayerViewModel inline = timeline.Inlines.Single(x => ReferenceEquals(x.Property, adapter));
        try
        {
            var replacement = new KeyFrameAnimation<float>();
            replacement.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) } });
            adapter.Animation = replacement;
            Assert.That(inline.Items.Single().Model, Is.SameAs(replacement.KeyFrames[0]));
            Assert.That(inline.LeftMargin.Value.Left, Is.EqualTo(300));

            shape.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(3), shape.Duration);
            HeadlessTestHelpers.Settle();
            Assert.That(timeline.Inlines, Does.Contain(inline));
            Assert.That(inline.LeftMargin.Value.Left, Is.EqualTo(450));
            graph.Animation.UseGlobalClock.Value = true;
            Assert.That(inline.LeftMargin.Value.Left, Is.EqualTo(450), "The old animation is no longer observed.");
            replacement.UseGlobalClock.Value = true;
            Assert.That(inline.LeftMargin.Value.Left, Is.Zero);
        }
        finally
        {
            timeline.DetachInline(inline);
        }
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Node_adapter_editors_use_the_same_clock_as_graph_evaluation(bool member, bool global)
    {
        await TestReset.ResetShellAsync();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("node-animation-time");
        await TestShell.AddElementAsync(editor, () => new NodeGraphDrawable(), MediaTime.FromSeconds(5), MediaTime.FromSeconds(3));
        Element element = scene.Children.Single();
        GraphModel graph = ((NodeGraphDrawable)element.Objects.Single()).Model.Value!;
        var node = new AnimationTimeMappingProbeNode();
        var adapter = new NodePropertyAdapter<float>("Animated");
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = { Value = global } };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 100 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = 500 } });
        adapter.Animation = animation;
        if (member)
        {
            var item = new DefaultNodeMember<float>();
            item.SetProperty(adapter);
            node.Items.Add(item);
        }
        else
        {
            var port = new DefaultInputPort<float>();
            port.SetPropertyAdapter(adapter);
            node.Items.Add(port);
        }
        graph.Nodes.Add(node);
        using var viewModel = new NodeGraphViewModel(graph, editor);
        var property = viewModel.Nodes.Single().Items.Select(item => item.PropertyEditorContext)
            .OfType<NumberEditorViewModel<float>>().Single(p => ReferenceEquals(p.PropertyAdapter, adapter));
        var clock = (IEditorClock)editor.GetService(typeof(IEditorClock))!;
        using var graphTab = new GraphEditorTabViewModel(editor) { Element = { Value = element } };
        graphTab.Select(animation);
        var graphEditor = graphTab.SelectedAnimation.Value!;
        var treeItem = graphTab.SelectedTreeItem.Value!;
        graphEditor.Options.Value = graphEditor.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        var view = new GraphEditorView { DataContext = graphEditor };
        var window = new Window { Content = view, Width = 1200, Height = 420 };
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, CompositionContext.Default);
        try
        {
            window.Show();
            property.KeyFrameIndex.Value = 1;
            Assert.That(animation.HierarchicalParent, Is.Null);
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(global ? 1 : 6)));
            Assert.That(property.IsAtKeyFrame.Value, Is.True);
            AssertGraphClock(global ? 0 : 5);
            HeadlessCapture.SaveIfRequested(window, nameof(AnimationTimeMappingTests), $"node-member-{member}-global-{global}");

            clock.Seek(MediaTime.FromSeconds(global ? 0.5 : 5.5));
            Assert.That(treeItem.HasKeyFrame.Value, Is.False);
            graphTab.ToggleKeyFrame(treeItem);
            Assert.That(animation.KeyFrames.Select(key => key.KeyTime.Value), Does.Contain(MediaTime.FromSeconds(0.5)));
            Assert.That(treeItem.HasKeyFrame.Value, Is.True);
            graphTab.ToggleKeyFrame(treeItem);
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));

            clock.Seek(MediaTime.FromSeconds(global ? 1 : 6));
            element.Start.Value = MediaTime.FromSeconds(6);
            Assert.That(property.IsAtKeyFrame.Value, Is.True);
            Assert.That(property.KeyFrameIndex.Value, Is.EqualTo(global ? 1 : 0));
            Assert.That(treeItem.HasKeyFrame.Value, Is.True);
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(global ? 1 : 6)));
            AssertGraphClock(global ? 0 : 6);

            node.IsTimeAnchor.Value = true;
            node.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(2), node.Duration);
            Assert.That(treeItem.HasKeyFrame.Value, Is.EqualTo(global));
            AssertGraphClock(global ? 0 : 2);
            animation.UseGlobalClock.Value = !global;
            AssertGraphClock(global ? 2 : 0);
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }

        void AssertGraphClock(int origin)
        {
            HeadlessTestHelpers.Render(3);
            var panel = view.FindControl<Panel>("graphPanel")!;
            var key = view.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                .Single(path => path.Name == "KeyTimeIcon"
                    && path.DataContext is GraphEditorKeyFrameViewModel model && model.Model == animation.KeyFrames[1]);
            Assert.That(graphEditor.Margin.Value.Left, Is.EqualTo(origin * 150));
            Assert.That(key.TranslatePoint(default, panel)!.Value.X, Is.EqualTo((origin + 1) * 150).Within(0.001));
            Assert.That(graphEditor.ConvertKeyTime(MediaTime.FromSeconds(origin + 1)), Is.EqualTo(MediaTime.FromSeconds(1)));
            clock.Seek(MediaTime.FromSeconds(origin + 0.5));
            view.Focus();
            window.KeyPress(Key.J, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.J, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(origin)));
            window.KeyPress(Key.K, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.K, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(origin + 1)));
            Assert.That(treeItem.HasKeyFrame.Value, Is.True);
            snapshot.Evaluate(new CompositionContext(clock.CurrentTime.Value));
            Assert.That(node.LastValue, Is.EqualTo(500));
        }
    }

    [AvaloniaTest]
    [TestCase(false, false, false)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    public async Task Inline_keys_and_easing_follow_only_inherited_clip_drag_previews(bool global, bool independent, bool direct)
    {
        await TestReset.ResetShellAsync();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("inline-drag-time");
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = { Value = global } };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(0.5) }, Value = { Value = 100 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1.5) }, Value = { Value = 500 } });
        var shape = new RectShape();
        shape.Width.SetKeyFrameAnimation(animation);
        var group = new DrawableGroup();
        if (!direct) group.Children.Add(shape);
        await TestShell.AddElementAsync(editor, () => direct ? shape : group, MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));
        if (!direct) group.IsTimeAnchor.Value = false;
        shape.IsTimeAnchor.Value = independent;
        Element element = scene.Children.Single();
        Assert.That(shape.Start, Is.EqualTo(element.Start.Value));
        var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.Options.Value = timeline.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        var adapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Width, shape);
        timeline.AttachInline(adapter, element);
        InlineAnimationLayerViewModel inline = timeline.Inlines.Single(x => ReferenceEquals(x.Property, adapter));
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 1200, Height = 420 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            Border clip = view.GetVisualDescendants().OfType<ElementView>().Single().FindControl<Border>("border")!;
            InlineAnimationLayer layer = view.GetVisualDescendants().OfType<InlineAnimationLayer>().Single();
            var key = layer.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                .Single(path => path.DataContext is InlineKeyFrameViewModel { Model: var model } && model == animation.KeyFrames[0]);
            var easing = layer.GetVisualDescendants().OfType<InlineEasingGraphControl>().Single();
            double keyX = HeadlessTestHelpers.Center(key, window).X;
            double easingX = HeadlessTestHelpers.Center(easing, window).X;
            double left = inline.LeftMargin.Value.Left;
            Point press = HeadlessTestHelpers.Center(clip, window);
            Point moved = press + new Vector(75, 0);
            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(moved, RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);

            double displacement = inline.Element.BorderMargin.Value.Left - 300;
            double expected = global || independent ? 0 : displacement;
            Assert.Multiple(() =>
            {
                Assert.That(displacement, Is.GreaterThan(0));
                Assert.That(element.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
                Assert.That(inline.LeftMargin.Value.Left - left, Is.EqualTo(expected).Within(0.001));
                Assert.That(HeadlessTestHelpers.Center(key, window).X - keyX, Is.EqualTo(expected).Within(0.001));
                Assert.That(HeadlessTestHelpers.Center(easing, window).X - easingX, Is.EqualTo(expected).Within(0.001));
            });
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            HeadlessCapture.SaveIfRequested(window, nameof(AnimationTimeMappingTests), $"inline-global-{global}-anchor-{independent}-direct-{direct}");

            window.MouseUp(moved, MouseButton.Left);
            await HeadlessTestHelpers.WaitUntilAsync(() =>
            {
                HeadlessTestHelpers.Render();
                return !editor.HistoryManager.IsEditing
                    && Math.Abs(inline.LeftMargin.Value.Left - animation.ToScene(MediaTime.Zero).ToSeconds() * 150) < 0.001;
            });
            Assert.That(inline.LeftMargin.Value.Left, Is.EqualTo(animation.ToScene(MediaTime.Zero).ToSeconds() * 150).Within(0.001));
        }
        finally
        {
            view.DataContext = null;
            window.Close();
            timeline.DetachInline(inline);
        }
    }
}

internal sealed partial class AnimationTimeMappingProbeNode : GraphNode
{
    public float LastValue { get; private set; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            RequireOriginal().LastValue = (float)ItemValues[0].GetBoxed()!;
        }
    }
}
