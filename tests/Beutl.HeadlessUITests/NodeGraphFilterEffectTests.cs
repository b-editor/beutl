using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using AvaPoint = Avalonia.Point;
using Point = Beutl.Graphics.Point;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class NodeGraphFilterEffectTests
{
    [AvaloniaTest]
    public void FilterEffectMenu_FollowsLibraryCategories()
    {
        using var model = new NodeGraphViewModel(new NodeGraphFilterEffect().Model.Value!, NodeGraphTestContext.Create().Object);
        var view = new NodeGraphView { DataContext = model };
        var window = new Window { Content = view, Width = 820, Height = 650 };
        bool previousOverlayPopups = TestAppBuilder.HeadlessOptions.OverlayPopups;
        try
        {
            TestAppBuilder.HeadlessOptions.OverlayPopups = true;
            window.Show();
            (FAMenuFlyout menu, FAMenuFlyoutSubItem addNode) = OpenCanvasMenu(view);
            OpenSubmenu(addNode);
            HeadlessCapture.SaveIfRequested(addNode, nameof(NodeGraphFilterEffectTests), "add-node-menu");
            var filterEffects = Submenu(addNode, GraphicsStrings.FilterEffect);
            OpenSubmenu(filterEffects);

            Assert.That(filterEffects.Items, Has.All.InstanceOf<FAMenuFlyoutSubItem>());
            Assert.That(filterEffects.Items.OfType<FAMenuFlyoutSubItem>().Select(item => item.Text), Is.EqualTo(new[]
            {
                GraphicsStrings.FilterEffectCategory_BlurAndSharpen,
                GraphicsStrings.FilterEffectCategory_ColorCorrection,
                GraphicsStrings.FilterEffectCategory_LightAndShadow,
                GraphicsStrings.FilterEffectCategory_MaskingAndKeying,
                GraphicsStrings.FilterEffectCategory_TransformAndDistort,
                GraphicsStrings.FilterEffectCategory_TimeAndAnimation,
                GraphicsStrings.FilterEffectCategory_Stylize,
                GraphicsStrings.FilterEffectCategory_Compositing,
                GraphicsStrings.FilterEffectCategory_ScriptAndNodes
            }));

            var light = Submenu(filterEffects, GraphicsStrings.FilterEffectCategory_LightAndShadow);
            OpenSubmenu(light);
            AssertMenuItem<FilterEffectNode<Bloom>>(light, GraphicsStrings.Bloom);
            AssertMenuItem<FilterEffectNode<LightRays>>(light, GraphicsStrings.LightRays);
            HeadlessCapture.SaveIfRequested(light, nameof(NodeGraphFilterEffectTests), "filter-effect-menu");

            var transform = Submenu(filterEffects, GraphicsStrings.FilterEffectCategory_TransformAndDistort);
            OpenSubmenu(transform);
            AssertMenuItem<FilterEffectNode<WaveRefraction>>(transform, GraphicsStrings.WaveRefraction);
            AssertMenuItem<FilterEffectNode<Puppet>>(transform, GraphicsStrings.Puppet);
            var time = Submenu(filterEffects, GraphicsStrings.FilterEffectCategory_TimeAndAnimation);
            OpenSubmenu(time);
            AssertMenuItem<FilterEffectNode<DelayAnimationEffect>>(time, GraphicsStrings.DelayAnimationEffect);
            var scripts = Submenu(filterEffects, GraphicsStrings.FilterEffectCategory_ScriptAndNodes);
            OpenSubmenu(scripts);
            AssertMenuItem<FilterEffectNode<GLSLScriptEffect>>(scripts, GraphicsStrings.GLSLScriptEffect);
            menu.Hide();
        }
        finally
        {
            window.Close();
            TestAppBuilder.HeadlessOptions.OverlayPopups = previousOverlayPopups;
        }
    }

    [AvaloniaTest]
    public async Task AddNodeMenu_RefreshesWhileOpenWhenNodesRegisterOrUnloadOffTheUiThread()
    {
        using var model = new NodeGraphViewModel(new NodeGraphDrawable().Model.Value!, NodeGraphTestContext.Create().Object);
        var view = new NodeGraphView { DataContext = model };
        var window = new Window { Content = view, Width = 820, Height = 650 };
        const string title = "Late node";

        try
        {
            window.Show();
            (FAMenuFlyout menu, FAMenuFlyoutSubItem addNode) = OpenCanvasMenu(view);
            OpenSubmenu(addNode);
            Assert.That(TypesIn(addNode), Does.Not.Contain(typeof(LateCanvasMenuNode)));

            await Task.Run(() => GraphNodeRegistry.RegisterNode<LateCanvasMenuNode>(title));
            HeadlessTestHelpers.Render(3);
            Assert.That(menu.IsOpen, Is.True);
            AssertMenuItem<LateCanvasMenuNode>(addNode, title);

            await Task.Run(() => TypeUnloadNotifier.NotifyUnloading([typeof(LateCanvasMenuNode)]));
            HeadlessTestHelpers.Render(3);
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(TypesIn(addNode), Does.Not.Contain(typeof(LateCanvasMenuNode)));
            menu.Hide();
        }
        finally
        {
            window.Close();
            TypeUnloadNotifier.NotifyUnloading([typeof(LateCanvasMenuNode)]);
        }
    }

    [AvaloniaTest]
    public void AddNodeMenu_UsesTheGraphOwnerAndGroupBoundaries()
    {
        var editor = NodeGraphTestContext.Create(player: true).Object;
        GraphModel effect = new NodeGraphFilterEffect().Model.Value!;
        var group = new GroupNode();
        effect.Nodes.Add(group);
        var loop = new ShaderRepeatNode();
        group.Group.Value.Nodes.Add(loop);
        using var effectView = new NodeGraphViewModel(effect, editor);
        using var groupView = new NodeGraphViewModel(group.Group.Value, editor);
        using var loopView = new NodeGraphViewModel(loop.Group.Value, editor);
        using var drawableView = new NodeGraphViewModel(new NodeGraphDrawable().Model.Value!, editor);
        var view = new NodeGraphView();
        var window = new Window { Content = view, Width = 820, Height = 650 };
        try
        {
            window.Show();
            Type[] effectTypes = ShowFor(effectView);
            Type[] groupTypes = ShowFor(groupView);
            Type[] loopTypes = ShowFor(loopView);
            Type[] drawableTypes = ShowFor(drawableView);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(effectTypes, Does.Contain(typeof(FilterEffectInputNode)).And.Contain(typeof(OutputNode)));
                Assert.That(drawableTypes, Does.Not.Contain(typeof(FilterEffectInputNode)).And.Contain(typeof(OutputNode)));
                Assert.That(groupTypes, Does.Contain(typeof(GroupInput)).And.Contain(typeof(GroupOutput)));
                Assert.That(groupTypes, Does.Not.Contain(typeof(FilterEffectInputNode)).And.Not.Contain(typeof(OutputNode)));
                Assert.That(groupTypes, Does.Not.Contain(typeof(ShaderLoopInput)).And.Not.Contain(typeof(ShaderLoopInfoNode)));
                Assert.That(loopTypes, Does.Contain(typeof(ShaderLoopInfoNode)).And.Not.Contain(typeof(ShaderLoopInput)));
            }
        }
        finally
        {
            window.Close();
        }

        Type[] ShowFor(NodeGraphViewModel model)
        {
            view.DataContext = model;
            (FAMenuFlyout menu, FAMenuFlyoutSubItem addNode) = OpenCanvasMenu(view, new AvaPoint(620, 400));
            Type[] types = TypesIn(addNode).ToArray();
            menu.Hide();
            return types;
        }
    }

    [AvaloniaTest]
    public async Task PuppetNode_CanAddAndDragAPinInThePreview()
    {
        TestGpu.RequireAnyDevice();
        (_, var scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("puppet-node-pins");
        var graphEffect = new NodeGraphFilterEffect();
        GraphModel graph = graphEffect.Model.Value!;
        var input = new FilterEffectInputNode { Position = { Value = (25, 70) } };
        var node = new FilterEffectNode<Puppet> { Position = { Value = (265, 70) } };
        var output = new OutputNode { Position = { Value = (505, 70) } };
        graph.Nodes.Add(input);
        graph.Nodes.Add(node);
        graph.Nodes.Add(output);
        graph.Connect((IInputPort)node.Items[1], input.Output);
        graph.Connect(output.InputPort, (IOutputPort)node.Items[0]);

        const float width = 320;
        const float height = 210;
        var shape = new RectShape
        {
            Width = { Value = width },
            Height = { Value = height },
            Fill = { Value = Brushes.White },
            FilterEffect = { Value = graphEffect },
        };
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(4));

        using var model = new NodeGraphViewModel(graph, editor);
        var view = new NodeGraphView { DataContext = model };
        var player = new PlayerView { DataContext = editor.Player };
        Grid.SetColumn(player, 1);
        var window = new Window
        {
            Width = 1400,
            Height = 630,
            Content = new Grid { ColumnDefinitions = new ColumnDefinitions("750,*"), Children = { view, player } },
        };

        try
        {
            window.Show();
            editor.GetRequiredService<IEditorClock>().Seek(MediaTime.Zero);
            await RenderPreview();
            var pins = view.GetVisualDescendants().OfType<PuppetPinsEditor>().Single();
            var add = pins.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == GraphicsStrings.Puppet_AddPin);
            Assert.That(add.IsEnabled, Is.True);

            HeadlessTestHelpers.Click(add, move: true);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.PointPick.Value is not null,
                message: "The Pins editor in the node should open the preview point picker.");
            await RenderPreview();
            Assert.That(player.transformHandlesOverlay.Drawable, Is.SameAs(shape));

            AvaPoint point = ToWindow(new Point(80, 100));
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Puppet puppet = node.Object.Value;
            await HeadlessTestHelpers.WaitUntilAsync(() => puppet.Pins.Count == 1);
            HeadlessTestHelpers.Click(add, move: true);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.PuppetPlacementEffect.Value is null);
            await RenderPreview();

            PuppetPin pin = puppet.Pins.Single();
            var handle = puppet.GetEditingHandles(MediaTime.Zero).Single();
            Point target = handle.Position + new Point(34, -24);
            var oldEdge = new Point(10, 100);
            Assert.That(PreviewAlpha(oldEdge), Is.GreaterThan(200));
            AvaPoint start = ToWindow(handle.Position);
            AvaPoint end = ToWindow(target);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            await RenderPreview();

            Assert.Multiple(() =>
            {
                Assert.That(pin.Position.Value.X, Is.EqualTo(target.X / width).Within(0.01f));
                Assert.That(pin.Position.Value.Y, Is.EqualTo(target.Y / height).Within(0.01f));
                Assert.That(pin.Position.Value, Is.Not.EqualTo(pin.RestPosition.Value));
            });
            await HeadlessTestHelpers.WaitUntilAsync(() =>
            {
                HeadlessTestHelpers.Render(2);
                return PreviewAlpha(oldEdge) == 0
                    && player.transformHandlesOverlay.HitTestEditingHandle(ToPreview(target))?.Handle.Id == handle.Id;
            }, message: "Dragging the pin should move the rendered content and preview handle.");
            HeadlessCapture.SaveIfRequested(window, nameof(NodeGraphFilterEffectTests), "puppet-pin-drag");
        }
        finally
        {
            window.Close();
        }

        async Task RenderPreview()
        {
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = editor.Player.AfterRendered.Take(1).Subscribe(_ => rendered.TrySetResult());
            editor.Player.QueuePreviewRender();
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Render(3);
        }

        byte? PreviewAlpha(Point local)
        {
            using var image = ((BitmapView)player.image).Source?.TryClone();
            if (image is null) return null;
            Point frame = player.transformHandlesOverlay.UserMatrix.Transform(local);
            return image.Value.SKBitmap.GetPixel(
                (int)(frame.X * image.Value.Width / scene.FrameSize.Value.Width),
                (int)(frame.Y * image.Value.Height / scene.FrameSize.Value.Height)).Alpha;
        }

        AvaPoint ToPreview(Point local)
        {
            Point frame = player.transformHandlesOverlay.UserMatrix.Transform(local);
            return player.transformHandlesOverlay.Viewport!.ToPreview(new AvaPoint(frame.X, frame.Y));
        }

        AvaPoint ToWindow(Point local) => player.image.TranslatePoint(ToPreview(local), window)!.Value;
    }

    [AvaloniaTest]
    public async Task DelayAnimationNode_UsesTheNestedFilterEffectEditor()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("delay-animation-node");
        var graph = new GraphModel();
        var delay = new FilterEffectNode<DelayAnimationEffect>();
        graph.Nodes.Add(delay);

        using var model = new NodeGraphViewModel(graph, editor);
        var member = model.Nodes.Single().Items.Single(item => item.Model?.Name.Value == nameof(DelayAnimationEffect.Effect));
        Assert.That(member.PropertyEditorContext, Is.TypeOf<FilterEffectEditorViewModel>());
        var effectEditor = (FilterEffectEditorViewModel)member.PropertyEditorContext!;
        effectEditor.ChangeFilterType(typeof(OpacityEffect));
        Assert.That(delay.Object.Value.Effect.Value, Is.TypeOf<OpacityEffect>());
    }

    private static (FAMenuFlyout Menu, FAMenuFlyoutSubItem AddNode) OpenCanvasMenu(NodeGraphView view, AvaPoint? position = null)
    {
        HeadlessTestHelpers.Render(3);
        var zoom = view.FindControl<ZoomBorder>("zoomBorder")!;
        HeadlessTestHelpers.Click(zoom, position ?? new AvaPoint(32, 32), MouseButton.Right, move: true, ticks: 3);
        var menu = (FAMenuFlyout)zoom.ContextFlyout!;
        Assert.That(menu.IsOpen, Is.True);
        return (menu, menu.Items.OfType<FAMenuFlyoutSubItem>().Single(item => item.Name == "addNode"));
    }

    private static FAMenuFlyoutSubItem Submenu(FAMenuFlyoutSubItem parent, string title)
        => parent.Items.OfType<FAMenuFlyoutSubItem>().Single(item => item.Text == title);

    private static IEnumerable<Type> TypesIn(FAMenuFlyoutSubItem menu)
    {
        foreach (object item in menu.Items)
        {
            if (item is FAMenuFlyoutSubItem submenu)
            {
                foreach (Type type in TypesIn(submenu)) yield return type;
            }
            else if (item is FAMenuFlyoutItem { DataContext: GraphNodeRegistry.RegistryItem registration })
            {
                yield return registration.Type;
            }
        }
    }

    private static void OpenSubmenu(FAMenuFlyoutSubItem submenu)
    {
        Assert.That(submenu.Focus(), Is.True);
        TopLevel topLevel = TopLevel.GetTopLevel(submenu)!;
        topLevel.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        topLevel.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        HeadlessTestHelpers.Render(3);
    }

    private static FAMenuFlyoutItem AssertMenuItem<T>(FAMenuFlyoutSubItem menu, string label)
    {
        var item = menu.Items.OfType<FAMenuFlyoutItem>()
            .Single(item => item.DataContext is GraphNodeRegistry.RegistryItem registration && registration.Type == typeof(T));
        Assert.Multiple(() =>
        {
            Assert.That(item.Text, Is.EqualTo(label));
            Assert.That(item.IsEffectivelyVisible, Is.True);
            Assert.That(item.Bounds.Height, Is.GreaterThan(0));
        });
        return item;
    }
}

internal sealed partial class LateCanvasMenuNode : GraphNode;
