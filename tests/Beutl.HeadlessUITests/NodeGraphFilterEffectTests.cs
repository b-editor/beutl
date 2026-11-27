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
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using AvaPoint = Avalonia.Point;
using Point = Beutl.Graphics.Point;
using RelativePoint = Beutl.Graphics.RelativePoint;
using RelativeUnit = Beutl.Graphics.RelativeUnit;

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
    [TestCase(0, 100)]
    [TestCase(180, 100)]
    [TestCase(0, 150)]
    public async Task PuppetNode_CanAddAndDragAPinInThePreview(int translation, int scale)
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
        var picture = (IOutputPort)node.Items[0];
        if (translation != 0 || scale != 100)
        {
            var transform = new FilterEffectNode<TransformEffect>
            {
                Position = { Value = (505, 70) },
            };
            transform.Object.Value.Transform.Value = scale == 100
                ? new TranslateTransform(translation, 0)
                : new ScaleTransform(scale, scale);
            output.Position.Value = (745, 70);
            graph.Nodes.Add(transform);
            graph.Connect((IInputPort)transform.Items[1], picture);
            picture = (IOutputPort)transform.Items[0];
        }
        graph.Connect(output.InputPort, picture);

        const float width = 320;
        const float height = 210;
        var center = new Point(width / 2, height / 2);
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
            await RenderPreview(editor);
            var pins = view.GetVisualDescendants().OfType<PuppetPinsEditor>().Single();
            var add = pins.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == GraphicsStrings.Puppet_AddPin);
            Assert.That(add.IsEnabled, Is.True);

            HeadlessTestHelpers.Click(add, move: true);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.PointPick.Value is not null,
                message: "The Pins editor in the node should open the preview point picker.");
            await RenderPreview(editor);
            Assert.That(player.transformHandlesOverlay.Drawable, Is.SameAs(shape));

            var clicked = new Point(80, 100);
            Assert.That(PreviewAlpha(clicked), Is.GreaterThan(200));
            AvaPoint point = ToWindow(clicked);
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Puppet puppet = node.Object.Value;
            await HeadlessTestHelpers.WaitUntilAsync(() => puppet.Pins.Count == 1);
            HeadlessTestHelpers.Click(add, move: true);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.PuppetPlacementEffect.Value is null);
            await RenderPreview(editor);

            PuppetPin pin = puppet.Pins.Single();
            var handle = puppet.GetEditingHandles(MediaTime.Zero).Single();
            Assert.That(player.transformHandlesOverlay.HitTestEditingHandle(ToPreview(clicked))?.Handle.Id, Is.EqualTo(handle.Id));
            Point target = clicked + new Point(34, -24);
            var oldEdge = new Point(10, 100);
            Assert.That(PreviewAlpha(oldEdge), Is.GreaterThan(200));
            AvaPoint start = ToWindow(clicked);
            AvaPoint end = ToWindow(target);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            await RenderPreview(editor);
            if (translation != 0)
                HeadlessCapture.SaveIfRequested(window, nameof(NodeGraphFilterEffectTests), "puppet-downstream-translate");

            Assert.Multiple(() =>
            {
                Assert.That(pin.RestPosition.Value.X, Is.EqualTo(clicked.X / width).Within(0.01f));
                Assert.That(pin.RestPosition.Value.Y, Is.EqualTo(clicked.Y / height).Within(0.01f));
                Assert.That(pin.Position.Value.X, Is.EqualTo(target.X / width).Within(0.01f));
                Assert.That(pin.Position.Value.Y, Is.EqualTo(target.Y / height).Within(0.01f));
                Assert.That(pin.Position.Value, Is.Not.EqualTo(pin.RestPosition.Value));
            });
            if (scale == 100)
            {
                await HeadlessTestHelpers.WaitUntilAsync(() =>
                {
                    HeadlessTestHelpers.Render(2);
                    return PreviewAlpha(oldEdge) == 0
                        && player.transformHandlesOverlay.HitTestEditingHandle(ToPreview(target))?.Handle.Id == handle.Id;
                }, message: "Dragging the pin should move the rendered content and preview handle.");
            }
            if (translation == 0)
                HeadlessCapture.SaveIfRequested(window, nameof(NodeGraphFilterEffectTests), scale == 100 ? "puppet-pin-drag" : "puppet-downstream-scale");
        }
        finally
        {
            window.Close();
        }

        byte? PreviewAlpha(Point local)
        {
            using var image = ((BitmapView)player.image).Source?.TryClone();
            if (image is null) return null;
            Point frame = ToFrame(local);
            return image.Value.SKBitmap.GetPixel(
                (int)(frame.X * image.Value.Width / scene.FrameSize.Value.Width),
                (int)(frame.Y * image.Value.Height / scene.FrameSize.Value.Height)).Alpha;
        }

        Point ToFrame(Point local)
        {
            Point transformed = center + (local - center) * (scale / 100f) + new Point(translation, 0);
            return player.transformHandlesOverlay.UserMatrix.Transform(transformed);
        }

        AvaPoint ToPreview(Point local) => ToPreviewPoint(player, ToFrame(local));

        AvaPoint ToWindow(Point local) => player.image.TranslatePoint(ToPreview(local), window)!.Value;
    }

    public enum Downstream { Translate, TranslateThenOpacity, ScaleThenTranslate, Warp }

    [AvaloniaTest]
    [TestCase(true, Downstream.Translate, false)]
    [TestCase(true, Downstream.TranslateThenOpacity, false)]
    [TestCase(false, Downstream.Translate, false)]
    [TestCase(true, Downstream.ScaleThenTranslate, false)]
    [TestCase(false, Downstream.ScaleThenTranslate, true)]
    [TestCase(true, Downstream.Warp, false)]
    public async Task WarpNode_HandlesFollowTheGraphOutput(bool meshWarp, Downstream downstream, bool drawableGraph)
    {
        TestGpu.RequireAnyDevice();
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("warp-node-handles");
        const float width = 240;
        const float height = 160;
        var center = new Point(width / 2, height / 2);
        var translation = new Point(110, 80);
        var frameSize = editor.Scene.FrameSize.Value;
        Point drawableOrigin = drawableGraph ? default : new Point((frameSize.Width - width) / 2, (frameSize.Height - height) / 2);
        var mesh = new FilterEffectNode<MeshWarp>();
        var corners = new FilterEffectNode<CornerPin>();
        ConfigureNode node = meshWarp ? mesh : corners;
        IEditingStageProvider effect = meshWarp ? mesh.Object.Value : corners.Object.Value;

        GraphModel graph;
        Drawable drawable;
        IOutputPort source;
        if (drawableGraph)
        {
            var graphDrawable = new NodeGraphDrawable();
            graph = graphDrawable.Model.Value!;
            drawable = graphDrawable;
            var rect = new RectGeometryNode();
            rect.Object.Value.Width.Value = width;
            rect.Object.Value.Height.Value = height;
            var shape = new GeometryShapeNode();
            shape.Fill.Property!.SetValue(Brushes.White);
            graph.Nodes.AddRange([rect, shape]);
            graph.Connect(shape.Geometry, rect.OutputPort);
            source = shape.Output;
        }
        else
        {
            var graphEffect = new NodeGraphFilterEffect();
            graph = graphEffect.Model.Value!;
            drawable = new RectShape
            {
                Width = { Value = width },
                Height = { Value = height },
                FilterEffect = { Value = graphEffect },
            };
            var input = new FilterEffectInputNode();
            graph.Nodes.Add(input);
            source = input.Output;
        }
        var output = new OutputNode();
        graph.Nodes.Add(node);
        graph.Connect((IInputPort)node.Items[1], source);
        IOutputPort stageOutput = (IOutputPort)node.Items[0];

        IOutputPort picture;
        switch (downstream)
        {
            case Downstream.ScaleThenTranslate:
                var scale = new FilterEffectNode<TransformEffect>();
                scale.Object.Value.Transform.Value = new ScaleTransform(150, 150);
                scale.Object.Value.ApplyToTarget.Value = false;
                var matrix = new TranslateMatrixNode();
                matrix.X.Property!.SetValue(translation.X);
                matrix.Y.Property!.SetValue(translation.Y);
                var transform = new TransformNode();
                graph.Nodes.AddRange([scale, matrix, transform]);
                graph.Connect((IInputPort)scale.Items[1], stageOutput);
                graph.Connect((IInputPort)transform.Items[1], (IOutputPort)scale.Items[0]);
                graph.Connect(transform.Matrix, (IOutputPort)matrix.Items[0]);
                picture = (IOutputPort)transform.Items[0];
                break;
            case Downstream.Warp:
                var warp = new FilterEffectNode<CornerPin>();
                warp.Object.Value.TopLeft.Value = new RelativePoint(0.15f, 0.08f, RelativeUnit.Relative);
                graph.Nodes.Add(warp);
                graph.Connect((IInputPort)warp.Items[1], stageOutput);
                picture = (IOutputPort)warp.Items[0];
                break;
            default:
                var translate = new FilterEffectNode<TransformEffect>();
                translate.Object.Value.Transform.Value = new TranslateTransform(translation);
                graph.Nodes.Add(translate);
                graph.Connect((IInputPort)translate.Items[1], stageOutput);
                picture = (IOutputPort)translate.Items[0];
                break;
        }
        if (downstream == Downstream.TranslateThenOpacity)
        {
            var opacity = new FilterEffectNode<OpacityEffect>();
            opacity.Object.Value.Opacity.Value = 50;
            graph.Nodes.Add(opacity);
            graph.Connect((IInputPort)opacity.Items[1], picture);
            picture = (IOutputPort)opacity.Items[0];
        }
        graph.Nodes.Add(output);
        Connection outputConnection = graph.Connect(output.InputPort, picture);
        for (int i = 0; i < graph.Nodes.Count; i++)
            graph.Nodes[i].Position.Value = (25 + i * 240, 70);

        await TestShell.AddElementAsync(editor, () => drawable, MediaTime.Zero, MediaTime.FromSeconds(4));
        if (meshWarp && downstream == Downstream.ScaleThenTranslate)
        {
            editor.Scene.Children.Single().AddObject(new DrawableTimeController
            {
                OffsetPosition = { Value = MediaTime.FromSeconds(1) },
            });
        }
        using var model = new NodeGraphViewModel(graph, editor);
        var view = new NodeGraphView { DataContext = model };
        var player = new PlayerView { DataContext = editor.Player };
        Grid.SetColumn(player, 1);
        var window = new Window
        {
            Width = 1400,
            Height = 640,
            Content = new Grid { ColumnDefinitions = new ColumnDefinitions("750,*"), Children = { view, player } },
        };
        try
        {
            window.Show();
            editor.Player.IsMoveMode.Value = true;
            editor.GetRequiredService<IEditorClock>().Seek(MediaTime.Zero);
            HeadlessTestHelpers.Render(3);
            var nodeView = view.GetVisualDescendants().OfType<GraphNodeView>()
                .Single(item => item.DataContext is GraphNodeViewModel nodeModel && ReferenceEquals(nodeModel.GraphNode, node));
            var editInFrame = nodeView.FindControl<Button>("editInFrameButton")!;
            Assert.That(editInFrame.IsEffectivelyVisible, Is.True);
            HeadlessTestHelpers.Click(editInFrame, move: true);
            Assert.That(editor.GetRequiredService<IEditorSelection>().SelectedObject.Value, Is.SameAs(effect));
            await RenderPreview(editor);
            TransformHandlesOverlay overlay = player.transformHandlesOverlay;
            Assert.That(overlay.Drawable, Is.SameAs(drawable));
            if (downstream == Downstream.TranslateThenOpacity)
            {
                using var image = ((BitmapView)player.image).Source?.TryClone();
                Assert.That(image, Is.Not.Null);
                Point frame = center + drawableOrigin + translation;
                byte alpha = image!.Value.SKBitmap.GetPixel(
                    (int)(frame.X * image.Value.Width / frameSize.Width),
                    (int)(frame.Y * image.Value.Height / frameSize.Height)).Alpha;
                Assert.That(alpha, Is.InRange(125, 130), "The downstream opacity should still render while editing.");
                HeadlessCapture.SaveIfRequested(window, nameof(NodeGraphFilterEffectTests), "mesh-warp-downstream-opacity");
            }
            EditingHandle handle = effect.GetEditingHandles(MediaTime.Zero).First();
            if (downstream == Downstream.Warp)
            {
                foreach (EditingHandle item in effect.GetEditingHandles(MediaTime.Zero))
                    Assert.That(overlay.HitTestEditingHandle(PreviewPoint(item.Position)), Is.Null);

                graph.Disconnect(outputConnection);
                graph.Connect(output.InputPort, stageOutput);
                await RenderPreview(editor);
                Assert.That(overlay.HitTestEditingHandle(PreviewPoint(handle.Position))?.Handle.Id, Is.EqualTo(handle.Id));
                return;
            }

            float scale = downstream == Downstream.ScaleThenTranslate ? 1.5f : 1f;
            Point TransformPoint(Point local) => center + (local - center) * scale + translation;
            AvaPoint start = PreviewPoint(TransformPoint(handle.Position));
            Assert.That(overlay.HitTestEditingHandle(start)?.Handle.Id, Is.EqualTo(handle.Id));

            var delta = new Point(32, 20);
            Point moved = handle.Position + delta;
            AvaPoint end = PreviewPoint(TransformPoint(moved));
            AvaPoint windowStart = player.image.TranslatePoint(start, window)!.Value;
            AvaPoint windowEnd = player.image.TranslatePoint(end, window)!.Value;
            window.MouseMove(windowStart);
            window.MouseDown(windowStart, MouseButton.Left);
            window.MouseMove(windowEnd, RawInputModifiers.LeftMouseButton);
            window.MouseUp(windowEnd, MouseButton.Left);
            await RenderPreview(editor);

            Point edited = meshWarp ? mesh.Object.Value.Mesh.Value!.Points[0] : corners.Object.Value.TopLeft.Value.Point;
            Assert.Multiple(() =>
            {
                Assert.That(edited.X, Is.EqualTo(delta.X / width).Within(0.01f));
                Assert.That(edited.Y, Is.EqualTo(delta.Y / height).Within(0.01f));
                Assert.That(overlay.HitTestEditingHandle(end)?.Handle.Id, Is.EqualTo(handle.Id));
            });
            HeadlessCapture.SaveIfRequested(window, nameof(NodeGraphFilterEffectTests),
                $"{(meshWarp ? "mesh-warp" : "corner-pin")}-{downstream}");
        }
        finally
        {
            window.Close();
        }

        AvaPoint PreviewPoint(Point local) => ToPreviewPoint(player, local + drawableOrigin);
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

    private static async Task RenderPreview(EditViewModel editor)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = editor.Player.AfterRendered.Take(1).Subscribe(_ => rendered.TrySetResult());
        editor.Player.QueuePreviewRender();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Render(3);
    }

    private static AvaPoint ToPreviewPoint(PlayerView player, Point frame)
        => player.transformHandlesOverlay.Viewport!.ToPreview(new AvaPoint(frame.X, frame.Y));

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
