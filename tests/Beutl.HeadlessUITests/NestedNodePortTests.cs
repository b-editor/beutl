using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NestedNodePortTests
{
    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task SharedPropertiesRefreshPortAvailabilityAcrossNodes(bool light, bool grouped)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditor();
        var graph = new GraphModel();
        var group = new GroupNode { Position = (30, 750) };
        if (grouped) graph.Nodes.Add(group);
        GraphModel secondGraph = grouped ? group.Group : graph;
        var first = new GeometryShapeNode { Position = (30, 140) };
        var second = new GeometryShapeNode { Position = (grouped ? 30 : 340, 140) };
        var brush = new SolidColorBrush();
        first.Fill.Property!.SetValue(brush);
        second.Fill.Property!.SetValue(brush);
        graph.Nodes.Add(first);
        secondGraph.Nodes.Add(second);
        IOutputPort AddSource(GraphModel target)
        {
            var source = new LayerInputNode { Position = (30, 0) };
            var port = new LayerInputNode.LayerInputPort<Color>();
            port.SetupProperty("Color");
            port.Property!.SetValue(Colors.Red);
            source.Items.Add(port);
            target.Nodes.Add(source);
            return port;
        }
        var output = AddSource(graph);
        var secondOutput = grouped ? AddSource(secondGraph) : output;
        using var vm = new NodeGraphViewModel(graph, editor);
        using var innerVm = grouped ? new NodeGraphViewModel(secondGraph, editor) : null;
        var firstVm = vm.Nodes.Single(n => n.GraphNode == first);
        var secondVm = (innerVm ?? vm).Nodes.Single(n => n.GraphNode == second);
        var firstMember = firstVm.NestedItems.Single(p => ReferenceEquals(p.Model!.Property!.GetEngineProperty(), brush.Color));
        var secondMember = secondVm.NestedItems.Single(p => ReferenceEquals(p.Model!.Property!.GetEngineProperty(), brush.Color));
        var view = new NodeGraphView { DataContext = vm };
        var innerView = grouped ? new NodeGraphView { DataContext = innerVm } : null;
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions(grouped ? "*,*" : "*") };
        content.Children.Add(view);
        if (innerView != null)
        {
            Grid.SetColumn(innerView, 1);
            content.Children.Add(innerView);
        }
        var window = new Window
        {
            Content = content,
            Width = 640,
            Height = 650,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            ((BrushEditorViewModel)firstVm.Items.Single(p => p.Model == first.Fill).PropertyEditorContext!).IsExpanded.Value = true;
            ((BrushEditorViewModel)secondVm.Items.Single(p => p.Model == second.Fill).PropertyEditorContext!).IsExpanded.Value = true;
            await FinishTransition();
            var secondPoint = (innerView ?? view).GetVisualDescendants().OfType<NodePortPoint>()
                .Single(p => ReferenceEquals(p.DataContext, secondMember));
            Assert.That(secondPoint.IsEnabled, Is.True);

            var connection = graph.Connect(firstMember.Model!, output);
            HeadlessTestHelpers.Render(3);
            Assert.That(secondMember.CanConnect.Value, Is.False);
            Assert.That(secondPoint.IsEnabled, Is.False);
            Assert.That(firstMember.CanConnect.Value, Is.True);
            Capture(window, $"shared-property-nodes-{light}-{grouped}");

            if (grouped)
            {
                graph.Nodes.Remove(group);
                HeadlessTestHelpers.Render(3);
                Assert.That(secondPoint.IsEnabled, Is.True);
                graph.Nodes.Add(group);
                HeadlessTestHelpers.Render(3);
                Assert.That(secondPoint.IsEnabled, Is.False);
            }

            first.Fill.Property!.SetValue(new SolidColorBrush());
            HeadlessTestHelpers.Render(3);
            Assert.That(secondPoint.IsEnabled, Is.True);
            first.Fill.Property.SetValue(brush);
            HeadlessTestHelpers.Render(3);
            Assert.That(secondPoint.IsEnabled, Is.False);
            graph.Disconnect(connection);
            HeadlessTestHelpers.Render(3);
            Assert.That(secondPoint.IsEnabled, Is.True);

            secondGraph.Connect(secondMember.Model!, secondOutput);
            HeadlessTestHelpers.Render(3);
            Assert.That(firstMember.CanConnect.Value, Is.False);
            Assert.That(secondMember.CanConnect.Value, Is.True);
        }
        finally
        {
            if (innerView != null) innerView.DataContext = null;
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task InvalidConnectedPortsAllowDisconnectionButCannotStartNewConnections(bool expression, bool contextMenu)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditor();
        var graph = new GraphModel();
        var node = new GeometryShapeNode { Position = (70, 140) };
        var pen = new Pen();
        var brush = new SolidColorBrush();
        pen.Brush.CurrentValue = brush;
        node.Pen.Property!.SetValue(pen);
        graph.Nodes.Add(node);
        var source = new LayerInputNode { Position = (10, 0) };
        var output = new LayerInputNode.LayerInputPort<float>();
        output.SetupProperty("Opacity");
        var otherOutput = new LayerInputNode.LayerInputPort<float>();
        otherOutput.SetupProperty("Other opacity");
        source.Items.Add(output);
        source.Items.Add(otherOutput);
        graph.Nodes.Add(source);
        var input = node.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), brush.Opacity));
        var connection = graph.Connect(input, output);
        using var vm = new NodeGraphViewModel(graph, editor);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = 320,
            Height = 900,
            RequestedThemeVariant = contextMenu ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            var nodeVm = vm.Nodes.Single(n => n.GraphNode == node);
            var penVm = (PenEditorViewModel)nodeVm.Items.Single(p => p.Model == node.Pen).PropertyEditorContext!;
            penVm.IsExpanded.Value = true;
            penVm.MajorProperties.OfType<BrushEditorViewModel>().Single().IsExpanded.Value = true;
            await FinishTransition();
            var row = FindPortView(view, brush.Opacity);
            var member = (InputPortViewModel)row.DataContext!;
            var point = FindPortPoint(view, row);
            Assert.That(point.IsEnabled, Is.True);

            var root = (NodePropertyAdapter<Pen?>)node.Pen.Property;
            if (expression)
                pen.Brush.Expression = Expression.CreateReference<Brush>(Guid.NewGuid());
            else
                root.Animation = new KeyFrameAnimation<Pen?>();
            using (var snapshot = new GraphSnapshot())
                snapshot.Build(graph, CompositionContext.Default);
            HeadlessTestHelpers.Render(3);
            Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Error));
            Assert.That(member.CanConnect.Value, Is.False);
            Assert.That(point.IsEnabled, Is.True, "An invalid existing connection must remain disconnectable.");
            Point position = point.TranslatePoint(new Point(5, 5), window)!.Value;
            Assert.That(window.InputHitTest(position), Is.SameAs(point));
            Capture(window, $"invalid-connected-{expression}-{contextMenu}");

            window.MouseMove(position);
            window.MouseDown(position, MouseButton.Left);
            Assert.That(view.GetVisualDescendants().OfType<ConnectionLine>().Count(), Is.EqualTo(1),
                "An invalid input must not start a temporary connection line.");
            window.MouseUp(position, MouseButton.Left);

            var otherPoint = view.GetVisualDescendants().OfType<NodePortPoint>()
                .Single(p => p.DataContext is NodePortViewModel m && m.Model == otherOutput);
            Point from = otherPoint.TranslatePoint(new Point(5, 5), window)!.Value;
            Assert.That(window.InputHitTest(from), Is.SameAs(otherPoint));
            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            Assert.That(view.GetVisualDescendants().OfType<ConnectionLine>().Count(), Is.EqualTo(2));
            window.MouseMove(position, RawInputModifiers.LeftMouseButton);
            window.MouseUp(position, MouseButton.Left);
            Assert.That(graph.AllConnections, Is.EqualTo(new[] { connection }),
                "Dropping another output on an invalid input must preserve its existing connection.");
            Assert.That(view.GetVisualDescendants().OfType<ConnectionLine>().Count(), Is.EqualTo(1));

            if (contextMenu)
            {
                window.MouseDown(position, MouseButton.Right);
                window.MouseUp(position, MouseButton.Right);
                HeadlessTestHelpers.Render(3);
                ContextMenu menu = point.ContextMenu!;
                Assert.That(menu.IsOpen, Is.True);
                MenuItem disconnect = menu.Items.OfType<MenuItem>().Single();
                TopLevel popup = TopLevel.GetTopLevel(menu)!;
                Capture(popup, $"invalid-connected-menu-{expression}");
                Point action = disconnect.TranslatePoint(new Point(disconnect.Bounds.Width / 2, disconnect.Bounds.Height / 2), popup)!.Value;
                popup.MouseMove(action);
                popup.MouseDown(action, MouseButton.Left);
                popup.MouseUp(action, MouseButton.Left);
                menu.Close();
            }
            else
            {
                for (int i = 0; i < 2; i++)
                {
                    window.MouseDown(position, MouseButton.Left);
                    window.MouseUp(position, MouseButton.Left);
                }
            }
            HeadlessTestHelpers.Render(3);
            Assert.That(graph.AllConnections, Is.Empty);
            Assert.That(member.IsConnected.Value, Is.False);
            Assert.That(point.IsEnabled, Is.False, "The now-unconnected invalid input must reject new connections.");
            pen.Brush.Expression = null;
            root.Animation = null;
            HeadlessTestHelpers.Render(3);
            Assert.That(point.IsEnabled, Is.True);
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task AliasedBrushEditorsUseTheirOwnRootAndPropertyPaths(bool light)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditor();
        var graph = new GraphModel();
        var node = new GeometryShapeNode { Position = (70, 140) };
        var shared = new SolidColorBrush();
        var pen = new Pen();
        pen.Brush.CurrentValue = shared;
        node.Fill.Property!.SetValue(shared);
        node.Pen.Property!.SetValue(pen);
        graph.Nodes.Add(node);
        var source = new LayerInputNode();
        var brushOutput = new LayerInputNode.LayerInputPort<Brush?>();
        brushOutput.SetupProperty("Brush");
        brushOutput.Property!.SetValue(new SolidColorBrush(Colors.Blue));
        var colorOutput = new LayerInputNode.LayerInputPort<Color>();
        colorOutput.SetupProperty("Color");
        colorOutput.Property!.SetValue(Colors.Red);
        source.Items.Add(brushOutput);
        source.Items.Add(colorOutput);
        graph.Nodes.Add(source);
        using var vm = new NodeGraphViewModel(graph, editor);
        var nodeVm = vm.Nodes.Single(n => n.GraphNode == node);
        var fillVm = (BrushEditorViewModel)nodeVm.Items.Single(p => p.Model == node.Fill).PropertyEditorContext!;
        var penVm = (PenEditorViewModel)nodeVm.Items.Single(p => p.Model == node.Pen).PropertyEditorContext!;
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = 640,
            Height = 900,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            fillVm.IsExpanded.Value = true;
            penVm.IsExpanded.Value = true;
            var penBrushVm = penVm.MajorProperties.OfType<BrushEditorViewModel>().Single();
            penBrushVm.IsExpanded.Value = true;
            await FinishTransition();
            NodePortView Row(BrushEditorViewModel brush)
            {
                var context = brush.ChildContext.Value!.Properties.OfType<BaseEditorViewModel>()
                    .Single(p => ReferenceEquals(p.PropertyAdapter.GetEngineProperty(), shared.Color));
                return view.GetVisualDescendants().OfType<NodePortView>()
                    .Single(row => ReferenceEquals(row.ProvidedEditor?.DataContext, context));
            }
            var fillRow = Row(fillVm);
            var penRow = Row(penBrushVm);
            var fillPort = (INestedInputPort)((InputPortViewModel)fillRow.DataContext!).Model!;
            var penPort = (INestedInputPort)((InputPortViewModel)penRow.DataContext!).Model!;
            Assert.That(fillPort.RootMember.Id, Is.EqualTo(node.Fill.Id));
            Assert.That(fillPort.PropertyPath, Is.EqualTo(new[] { "p:Color" }));
            Assert.That(penPort.RootMember.Id, Is.EqualTo(node.Pen.Id));
            Assert.That(penPort.PropertyPath, Is.EqualTo(new[] { "p:Brush", "p:Color" }));
            Assert.That(penRow.DataContext, Is.Not.SameAs(fillRow.DataContext));

            var parentConnection = graph.Connect(node.Fill, brushOutput);
            HeadlessTestHelpers.Render(3);
            Assert.That(((InputPortViewModel)fillRow.DataContext!).CanConnect.Value, Is.False);
            Assert.That(((InputPortViewModel)penRow.DataContext!).CanConnect.Value, Is.True);
            var colorConnection = graph.Connect(penPort, colorOutput);
            HeadlessTestHelpers.Render(3);
            var link = vm.AllConnections.Single(c => c.Connection == colorConnection);
            Assert.That(link.InputPortVM.Value, Is.SameAs(penRow.DataContext));
            Assert.That(link.InputPortPosition.Value, Is.EqualTo(penRow.GetPortPosition()!.Value + nodeVm.Position.Value));
            Capture(window, $"aliased-brush-paths-{light}");

            graph.Disconnect(colorConnection);
            graph.Disconnect(parentConnection);
            await FinishTransition();
            Assert.That(Row(fillVm).DataContext, Is.SameAs(fillRow.DataContext));
            Assert.That(Row(penBrushVm).DataContext, Is.SameAs(penRow.DataContext));
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task AliasedEditorsWithinOneRootTrackPropertyPathsAndListItemIdentity()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditor();
        var shared = new SolidColorBrush();
        var first = new AliasedBrushPair();
        var second = new AliasedBrushPair();
        first.Primary.CurrentValue = first.Secondary.CurrentValue = shared;
        second.Primary.CurrentValue = second.Secondary.CurrentValue = shared;
        var node = new FactoryNode<AliasedBrushList>();
        node.Object.Items.AddRange([first, second]);
        var graph = new GraphModel();
        graph.Nodes.Add(node);
        using var vm = new NodeGraphViewModel(graph, editor);
        var nodeVm = vm.Nodes.Single();
        var list = (ListEditorViewModel<AliasedBrushPair?>)nodeVm.Items
            .Single(p => p.Model!.Name == "Items").PropertyEditorContext!;
        Guid rootId = node.Items.Single(p => p.Name == "Items").Id;
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 1000 };
        try
        {
            window.Show();
            list.IsExpanded.Value = true;
            await FinishTransition();
            async Task<INestedInputPort[]> ReadPorts()
            {
                var expected = new List<(BaseEditorViewModel Color, string[] Path)>();
                foreach (var item in list.Items)
                {
                    var obj = (ICoreObjectEditorViewModel)item.Context!;
                    obj.IsExpanded.Value = true;
                    var pair = (AliasedBrushPair)obj.Value.Value!;
                    foreach (var brush in obj.Properties.Value!.Properties.OfType<BrushEditorViewModel>())
                    {
                        brush.IsExpanded.Value = true;
                        var color = brush.ChildContext.Value!.Properties.OfType<BaseEditorViewModel>()
                            .Single(p => ReferenceEquals(p.PropertyAdapter.GetEngineProperty(), shared.Color));
                        expected.Add((color, ["i:" + pair.Id, "p:" + brush.PropertyAdapter.GetEngineProperty()!.Name, "p:Color"]));
                    }
                }
                await FinishTransition();
                var result = new List<INestedInputPort>();
                foreach (var (context, path) in expected)
                {
                    var row = view.GetVisualDescendants().OfType<NodePortView>()
                        .Single(row => ReferenceEquals(row.ProvidedEditor?.DataContext, context));
                    var port = (INestedInputPort)((InputPortViewModel)row.DataContext!).Model!;
                    Assert.That(port.RootMember.Id, Is.EqualTo(rootId));
                    Assert.That(port.PropertyPath, Is.EqualTo(path));
                    result.Add(port);
                }
                Assert.That(result, Has.Count.EqualTo(4));
                Assert.That(result.Select(p => p.Id), Is.Unique);
                return result.ToArray();
            }
            var initial = await ReadPorts();
            node.Object.Items.Move(0, 1);
            Assert.That(await ReadPorts(), Is.EquivalentTo(initial));

            var replacement = new AliasedBrushPair();
            replacement.Primary.CurrentValue = replacement.Secondary.CurrentValue = shared;
            node.Object.Items[1] = replacement;
            var replaced = await ReadPorts();
            Assert.That(replaced.Count(p => initial.Contains(p)), Is.EqualTo(2));
            Assert.That(replaced.Any(p => p.PropertyPath.Contains("i:" + first.Id)), Is.False);
            Capture(window, "aliased-list-paths");
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public void BulkPortUpdatesPreserveExistingViewModelsAndPublishOneCollectionChange()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<TransformGroup>();
        node.Object.Children.Add(new TranslateTransform());
        graph.Nodes.Add(node);
        var services = new Mock<IEditorContext>();
        services.Setup(s => s.GetService(typeof(IPropertyEditorFactory)))
            .Returns(new Mock<IPropertyEditorFactory>().Object);
        using var vm = new NodeGraphViewModel(graph, services.Object);
        GraphNodeViewModel nodeVm = vm.Nodes.Single();
        var originalItems = nodeVm.NestedItems.ToArray();
        int collectionChanges = 0;
        NotifyCollectionChangedAction? action = null;
        nodeVm.NestedItems.CollectionChanged += (_, e) =>
        {
            collectionChanges++;
            action = e.Action;
        };
        var transforms = Enumerable.Range(0, 800).Select(i => (Transform)new TranslateTransform(i, 0)).ToArray();
        var stopwatch = Stopwatch.StartNew();

        node.Object.Children.AddRange(transforms);

        stopwatch.Stop();
        TestContext.WriteLine($"Adding 800 list elements with the graph view model: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.That(nodeVm.NestedItems, Has.Count.EqualTo(1602));
        Assert.That(nodeVm.NestedItems.Take(2), Is.EqualTo(originalItems));
        Assert.That(nodeVm.NestedItems.Select(p => p.Model), Is.Unique);
        Assert.That(collectionChanges, Is.EqualTo(1));
        Assert.That(action, Is.EqualTo(NotifyCollectionChangedAction.Add));

        node.Object.Children.RemoveRange(1, 800);

        Assert.That(nodeVm.NestedItems, Is.EqualTo(originalItems));
        Assert.That(collectionChanges, Is.EqualTo(2));
        Assert.That(action, Is.EqualTo(NotifyCollectionChangedAction.Remove));
    }

    [AvaloniaTest]
    public async Task ExpandedBrushReplacementReusesPortsAfterFallbackRecovery()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditor();
        var graph = new GraphModel();
        var node = new FactoryNode<Pen>();
        var brush = new SolidColorBrush();
        node.Object.Brush.CurrentValue = brush;
        graph.Nodes.Add(node);
        var source = new LayerInputNode { Position = (300, 0) };
        var output = new LayerInputNode.LayerInputPort<Color>();
        output.SetupProperty("Color");
        output.Property!.SetValue(Colors.Red);
        source.Items.Add(output);
        graph.Nodes.Add(source);
        var port = node.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), brush.Color));
        graph.Connect(port, output);
        using var vm = new NodeGraphViewModel(graph, editor);
        GraphNodeViewModel nodeVm = vm.Nodes.Single(n => n.GraphNode == node);
        InputPortViewModel portVm = nodeVm.NestedItems.Single(p => p.Model == port);
        var brushVm = nodeVm.Items.Select(p => p.PropertyEditorContext).OfType<BrushEditorViewModel>().Single();
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 900 };
        try
        {
            window.Show();
            brushVm.IsExpanded.Value = true;
            await FinishTransition();
            Assert.That(FindPortView(view, brush.Color).DataContext, Is.SameAs(portVm));

            node.Object.Brush.CurrentValue = new FallbackBrush { Json = CoreSerializer.SerializeToJsonObject(brush) };
            await FinishTransition();
            Assert.That(nodeVm.NestedItems, Does.Contain(portVm));
            Assert.That(portVm.CanConnect.Value, Is.False);
            Assert.That(vm.AllConnections.Single().InputPortVM.Value, Is.SameAs(portVm));

            for (int i = 0; i < 2; i++)
            {
                var replacement = new SolidColorBrush();
                node.Object.Brush.CurrentValue = replacement;
                brushVm.IsExpanded.Value = true;
                await FinishTransition();
                NodePortView row = FindPortView(view, replacement.Color);
                Assert.That(row.IsEffectivelyVisible, Is.True);
                Assert.That(row.DataContext, Is.SameAs(portVm));
                Assert.That(portVm.CanConnect.Value, Is.True);
                Assert.That(vm.AllConnections.Single().InputPortVM.Value, Is.SameAs(portVm));
            }
            AssertPortsOnNodeEdge(view.GetVisualDescendants().OfType<GraphNodeView>()
                .Single(n => n.DataContext == nodeVm));
            Capture(window, "recovered-brush");
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task RootTextEditorsKeepMainBranchHorizontalInsets(bool light)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditor();
        var graph = new GraphModel();
        var node = new TextNode { Position = (16, 8) };
        node.Object.Size.CurrentValue = 232;
        node.Object.Spacing.CurrentValue = 0;
        node.Object.Text.CurrentValue = "Text";
        graph.Nodes.Add(node);
        using var vm = new NodeGraphViewModel(graph, editor);
        var view = new NodeGraphView { DataContext = vm, Width = 320 };
        GraphNodeViewModel nodeVm = vm.Nodes.Single();
        var baselineRows = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var baselines = new Dictionary<string, (Border Frame, PropertyEditor Editor)>();
        foreach (string name in new[] { "FontFamily", "Size", "Spacing", "Text" })
        {
            var context = nodeVm.Items.Single(item => item.Model!.Name == name).PropertyEditorContext!;
            Assert.That(context.Extension.TryCreateControlForNode(context, out Control? control), Is.True);
            var baselineEditor = (PropertyEditor)control!;
            baselineEditor.EditorStyle = PropertyEditorStyle.Compact;
            baselineEditor.DataContext = context;
            // main's root rows: a 215px node with a 1px border, 10px socket columns,
            // and the editor's original theme minimum width (no nested overrides).
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("10,*,10") };
            Grid.SetColumn(baselineEditor, 1);
            row.Children.Add(baselineEditor);
            var frame = new Border { Width = 215, BorderThickness = new Thickness(1), Child = row };
            baselineRows.Children.Add(frame);
            baselines.Add(name, (frame, baselineEditor));
        }
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*") };
        content.Children.Add(view);
        Grid.SetColumn(baselineRows, 1);
        content.Children.Add(baselineRows);
        var window = new Window
        {
            Content = content,
            Width = 640,
            Height = 900,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            await FinishTransition();
            GraphNodeView nodeView = view.GetVisualDescendants().OfType<GraphNodeView>().Single();
            foreach (var (name, baseline) in baselines)
            {
                NodePortView row = nodeView.GetVisualDescendants().OfType<NodePortView>()
                    .Single(row => row.DataContext is NodeMemberViewModel member && member.Model!.Name == name);
                PropertyEditor actualEditor = row.GetVisualDescendants().OfType<PropertyEditor>().Single();
                Assert.That(baseline.Editor.MinWidth, Is.EqualTo(200));
                Assert.That(actualEditor.MinWidth, Is.EqualTo(baseline.Editor.MinWidth), name);
                foreach (string part in new[] { "PART_HeaderTextBlock", name == "FontFamily" ? "PART_InnerButton" : "PART_InnerTextBox" })
                {
                    Control actual = actualEditor.GetVisualDescendants().OfType<Control>().Single(c => c.Name == part);
                    Control expected = baseline.Editor.GetVisualDescendants().OfType<Control>().Single(c => c.Name == part);
                    double actualLeft = actual.TranslatePoint(default, nodeView)!.Value.X;
                    double expectedLeft = expected.TranslatePoint(default, baseline.Frame)!.Value.X;
                    Assert.That(actualLeft, Is.EqualTo(expectedLeft).Within(0.01), $"{name}: {part} left edge");
                    Assert.That(nodeView.Bounds.Width - actualLeft - actual.Bounds.Width,
                        Is.EqualTo(baseline.Frame.Bounds.Width - expectedLeft - expected.Bounds.Width).Within(0.01),
                        $"{name}: {part} right edge");
                }
            }
            NodePortView fill = nodeView.GetVisualDescendants().OfType<NodePortView>()
                .Single(row => row.DataContext is NodeMemberViewModel member && member.Model!.Name == "Fill");
            Assert.That(fill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BrushPreviewButton").Bounds.Width,
                Is.EqualTo(80), "Root brush previews must retain main's width.");
            Capture(window, $"root-text-main-baseline-{light}");
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(320, true)]
    [TestCase(640, false)]
    [TestCase(640, true)]
    public async Task ExpandedPropertiesHaveConnectablePortsAndCollapsedConnectionsKeepTheirAnchor(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditor();
        var graph = new GraphModel();
        var node = new GeometryShapeNode { Position = (70, 140) };
        var pen = new Pen();
        var brush = new SolidColorBrush();
        pen.Brush.CurrentValue = brush;
        node.Pen.Property!.SetValue(pen);
        graph.Nodes.Add(node);
        var source = new LayerInputNode { Position = (10, 0) };
        var output = new LayerInputNode.LayerInputPort<float>();
        output.SetupProperty("Thickness");
        output.Property!.SetValue(18f);
        source.Items.Add(output);
        graph.Nodes.Add(source);
        using var vm = new NodeGraphViewModel(graph, editor);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 900,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            GraphNodeViewModel nodeVm = vm.Nodes.Single(n => n.GraphNode == node);
            GraphNodeView nodeView = view.GetVisualDescendants().OfType<GraphNodeView>()
                .Single(n => ReferenceEquals(n.DataContext, nodeVm));
            var penVm = (PenEditorViewModel)nodeVm.Items.Single(p => p.Model == node.Pen).PropertyEditorContext!;
            penVm.IsExpanded.Value = true;
            var brushVm = penVm.MajorProperties.OfType<BrushEditorViewModel>().Single();
            brushVm.IsExpanded.Value = true;
            await FinishTransition();

            NodePortView thicknessView = FindPortView(view, pen.Thickness);
            NodePortView colorView = FindPortView(view, brush.Color);
            Assert.That(thicknessView.IsEffectivelyVisible, Is.True);
            Assert.That(colorView.IsEffectivelyVisible, Is.True);
            Control colorPicker = colorView.ProvidedEditor!.GetVisualDescendants().OfType<Control>()
                .Single(control => control.Name == "PART_ColorPickerButton");
            Assert.That(colorPicker.TranslatePoint(new Point(colorPicker.Bounds.Width, 0), colorView)!.Value.X,
                Is.LessThanOrEqualTo(colorView.Bounds.Width), "The color picker must fit after removing its menu column.");
            foreach (Button preview in view.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "BrushPreviewButton"))
            {
                bool nested = preview.GetVisualAncestors().OfType<NodePortView>().Any(row => row.ProvidedEditor != null);
                Assert.That(preview.Bounds.Width, Is.EqualTo(nested ? 32 : 80));
            }
            Assert.That(thicknessView.ProvidedEditor!.DataContext, Is.InstanceOf<NumberEditorViewModel<float>>());
            AssertNestedEditorSpacing(nodeView);
            double unconnectedHeaderX = thicknessView.ProvidedEditor.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Name == "PART_HeaderTextBlock").TranslatePoint(default, thicknessView)!.Value.X;
            AssertNodeEditorStyles(nodeView);
            AssertNodePropertyMenusHidden(nodeView);
            AssertPortsOnNodeEdge(nodeView);

            NodePortPoint start = view.GetVisualDescendants().OfType<NodePortPoint>()
                .Single(p => p.DataContext is NodePortViewModel m && m.Model == output);
            NodePortPoint end = FindPortPoint(view, thicknessView);
            Point from = start.TranslatePoint(new Point(5, 5), window)!.Value;
            Point to = end.TranslatePoint(new Point(5, 5), window)!.Value;
            Capture(window, $"before-connection-{width}-{light}");
            Assert.That(window.InputHitTest(from), Is.SameAs(start), $"Output at {from}");
            Assert.That(window.InputHitTest(to), Is.SameAs(end), $"Input at {to}");
            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            HeadlessTestHelpers.Render(3);

            Assert.That(graph.AllConnections, Has.Count.EqualTo(1));
            ConnectionViewModel connection = vm.AllConnections.Single();
            Assert.That(connection.InputPortVM.Value!.Model!.Property!.GetEngineProperty(), Is.SameAs(pen.Thickness));
            Assert.That(((InputPortViewModel)nodeVm.Items.Single(p => p.Model == node.Pen)).CanConnect.Value, Is.False);
            Assert.That(((InputPortViewModel)colorView.DataContext!).CanConnect.Value, Is.True);
            Assert.That(thicknessView.GetVisualDescendants().OfType<TextBlock>().Single()
                .TranslatePoint(default, thicknessView)!.Value.X, Is.EqualTo(unconnectedHeaderX).Within(0.01),
                "Connecting a nested property must not move its label horizontally.");

            Point beforeMove = connection.InputPortPosition.Value;
            nodeVm.Position.Value += new Point(9, 15);
            HeadlessTestHelpers.Render(3);
            Assert.That(connection.InputPortPosition.Value, Is.EqualTo(beforeMove + new Point(9, 15)));
            AssertPortsOnNodeEdge(nodeView);
            ZoomBorder zoom = view.FindControl<ZoomBorder>("zoomBorder")!;
            zoom.EnableAnimations = false;
            zoom.SetMatrix(Matrix.CreateScale(new Vector(0.8, 0.8)), true);
            HeadlessTestHelpers.Render(3);
            Assert.That(connection.InputPortPosition.Value, Is.EqualTo(beforeMove + new Point(9, 15)));
            AssertPortsOnNodeEdge(nodeView);
            Capture(window, $"expanded-{width}-{light}");

            penVm.IsExpanded.Value = false;
            await FinishTransition();
            Assert.That(thicknessView.IsEffectivelyVisible, Is.False);
            Assert.That(end.IsEffectivelyVisible, Is.False);
            Assert.That(connection.InputPortPosition.Value.Y, Is.LessThan(beforeMove.Y + 15));
            Assert.That(connection.InputPortPosition.Value.X, Is.EqualTo(nodeVm.Position.Value.X));
            Assert.That(graph.AllConnections, Has.Count.EqualTo(1));
            Assert.That(connection.InputPortVM.Value, Is.SameAs(thicknessView.DataContext));
            Capture(window, $"collapsed-{width}-{light}");

            penVm.IsExpanded.Value = true;
            await FinishTransition();
            Assert.That(connection.InputPortPosition.Value, Is.EqualTo(beforeMove + new Point(9, 15)));
            Assert.That(end.IsEffectivelyVisible, Is.True);
            AssertPortsOnNodeEdge(nodeView);
            nodeVm.IsExpanded.Value = false;
            await FinishTransition();
            Assert.That(connection.InputPortPosition.Value.Y, Is.LessThan(nodeVm.Position.Value.Y + 40));
            Assert.That(end.IsEffectivelyVisible, Is.False);
            Assert.That(nodeView.Bounds.Height, Is.LessThan(45));

            nodeVm.IsExpanded.Value = true;
            await FinishTransition();
            ((InputPortViewModel)thicknessView.DataContext!).DisconnectAll();
            HeadlessTestHelpers.Render(3);
            Assert.That(thicknessView.ProvidedEditor.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Name == "PART_HeaderTextBlock").TranslatePoint(default, thicknessView)!.Value.X,
                Is.EqualTo(unconnectedHeaderX).Within(0.01));
            AssertNestedEditorSpacing(nodeView);
            Capture(window, $"disconnected-{width}-{light}");
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task ListItemPropertiesKeepTheirPortAfterReordering()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditor();
        var graph = new GraphModel();
        var node = new FactoryNode<Beutl.Graphics.Shapes.RectShape>();
        var group = new TransformGroup();
        var first = new TranslateTransform();
        var second = new TranslateTransform();
        group.Children.Add(first);
        group.Children.Add(second);
        node.Items.Single(i => i.Name == "Transform").Property!.SetValue(group);
        graph.Nodes.Add(node);
        using var vm = new NodeGraphViewModel(graph, editor);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 900 };
        try
        {
            window.Show();
            var transform = vm.Nodes.Single().Items.Select(p => p.PropertyEditorContext).OfType<TransformEditorViewModel>().Single();
            transform.IsExpanded.Value = true;
            await FinishTransition();
            foreach (var item in transform.Group.Value!.Items)
                ((TransformEditorViewModel)item.Context!).IsExpanded.Value = true;
            await FinishTransition();
            var firstView = FindPortView(view, first.X);
            var firstPort = ((InputPortViewModel)firstView.DataContext!).Model;
            Assert.That(firstView.IsEffectivelyVisible, Is.True);
            AssertNestedEditorSpacing(view.GetVisualDescendants().OfType<GraphNodeView>().Single());
            AssertNodeEditorStyles(view.GetVisualDescendants().OfType<GraphNodeView>().Single());
            AssertNodePropertyMenusHidden(view.GetVisualDescendants().OfType<GraphNodeView>().Single());
            AssertPortsOnNodeEdge(view.GetVisualDescendants().OfType<GraphNodeView>().Single());

            group.Children.Move(0, 1);
            await FinishTransition();

            Assert.That(((InputPortViewModel)FindPortView(view, first.X).DataContext!).Model, Is.SameAs(firstPort));
            Assert.That(FindPortView(view, second.X), Is.Not.SameAs(FindPortView(view, first.X)));
            AssertNodeEditorStyles(view.GetVisualDescendants().OfType<GraphNodeView>().Single());
            AssertPortsOnNodeEdge(view.GetVisualDescendants().OfType<GraphNodeView>().Single());
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task GradientStopsKeepTheirSliderAndExposeEachStopsProperties()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditor();
        var graph = new GraphModel();
        var node = new FactoryNode<LinearGradientBrush>();
        var stop = new GradientStop(Colors.Red, 0.25f);
        node.Object.GradientStops.Add(stop);
        graph.Nodes.Add(node);
        using var vm = new NodeGraphViewModel(graph, editor);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 900 };
        try
        {
            window.Show();
            var stops = vm.Nodes.Single().Items.Select(p => p.PropertyEditorContext)
                .OfType<GradientStopsEditorViewModel>().Single();
            Assert.That(stops.NodeItems.Value, Is.Not.Null);
            stops.NodeItems.Value!.IsExpanded.Value = true;
            await FinishTransition();
            ((ICoreObjectEditorViewModel)stops.NodeItems.Value.Items.Single().Context!).IsExpanded.Value = true;
            await FinishTransition();

            Assert.That(view.GetVisualDescendants().OfType<Beutl.Controls.PropertyEditors.GradientStopsSlider>().Count(), Is.EqualTo(1));
            Assert.That(FindPortView(view, stop.Color).IsEffectivelyVisible, Is.True);
            Assert.That(FindPortView(view, stop.Offset).IsEffectivelyVisible, Is.True);
            AssertNodePropertyMenusHidden(view.GetVisualDescendants().OfType<GraphNodeView>().Single());
            AssertNestedEditorSpacing(view.GetVisualDescendants().OfType<GraphNodeView>().Single());
            AssertPortsOnNodeEdge(view.GetVisualDescendants().OfType<GraphNodeView>().Single());
            Capture(window, "gradient-stops");

            NodePortView colorRow = FindPortView(view, stop.Color);
            double colorHeaderX = colorRow.ProvidedEditor!.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Name == "PART_HeaderTextBlock").TranslatePoint(default, colorRow)!.Value.X;
            var source = new LayerInputNode { Position = (300, 0) };
            var output = new LayerInputNode.LayerInputPort<Color>();
            output.SetupProperty("Color");
            output.Property!.SetValue(Colors.Blue);
            source.Items.Add(output);
            graph.Nodes.Add(source);
            var connection = graph.Connect(((InputPortViewModel)colorRow.DataContext!).Model!, output);
            HeadlessTestHelpers.Render(3);
            Assert.That(colorRow.GetVisualDescendants().OfType<TextBlock>().Single()
                .TranslatePoint(default, colorRow)!.Value.X, Is.EqualTo(colorHeaderX).Within(0.01));
            Capture(window, "gradient-stop-connected");
            graph.Disconnect(connection);
            HeadlessTestHelpers.Render(3);
            Assert.That(colorRow.ProvidedEditor!.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Name == "PART_HeaderTextBlock").TranslatePoint(default, colorRow)!.Value.X,
                Is.EqualTo(colorHeaderX).Within(0.01));
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task RemovingLayerInputFromThePropertyPanelDisconnectsItsNestedInputs()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditor();
        var graph = new GraphModel();
        var layer = new LayerInputNode();
        var brush = new SolidColorBrush();
        var objectOutput = new LayerInputNode.LayerInputPort<Brush>();
        objectOutput.SetupProperty("Brush");
        objectOutput.Property!.SetValue(brush);
        layer.Items.Add(objectOutput);
        graph.Nodes.Add(layer);
        var source = new LayerInputNode();
        var colorOutput = new LayerInputNode.LayerInputPort<Color>();
        colorOutput.SetupProperty("Color");
        colorOutput.Property!.SetValue(Colors.Red);
        source.Items.Add(colorOutput);
        graph.Nodes.Add(source);
        graph.Connect(layer.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), brush.Color)), colorOutput);
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = graph;
        using var properties = new GraphModelEditorViewModel(new EnginePropertyAdapter<GraphModel?>(drawable.Model, drawable));
        properties.Accept(new EditorVisitor(editor));

        properties.NodeMembers.Single(member => member.GraphNode == layer).Remove();

        Assert.That(graph.Nodes.Contains(layer), Is.False);
        Assert.That(graph.AllConnections, Is.Empty);
        Assert.That(brush.Color.Expression, Is.Null);
    }

    private sealed record EditorVisitor(IEditorContext Context) : IPropertyEditorContextVisitor, IServiceProvider
    {
        public object? GetService(Type serviceType) => Context.GetService(serviceType);
        public void Visit(IPropertyEditorContext context) { }
    }

    private static NodePortView FindPortView(Control view, IProperty property)
        => view.GetVisualDescendants().OfType<NodePortView>().Single(p =>
            p.DataContext is InputPortViewModel vm && ReferenceEquals(vm.Model?.Property?.GetEngineProperty(), property));

    private static NodePortPoint FindPortPoint(Control view, NodePortView row)
        => view.GetVisualDescendants().OfType<NodePortPoint>()
            .Single(p => ReferenceEquals(p.DataContext, row.DataContext));

    private static void AssertNodeEditorStyles(GraphNodeView node)
    {
        // Composite editors create their own children, including initially hidden reference
        // editors. ListItem explicitly selects the template with delete/reorder controls.
        foreach (PropertyEditor editor in node.GetVisualDescendants().OfType<PropertyEditor>())
        {
            if (editor.EditorStyle == PropertyEditorStyle.ListItem) continue;
            Assert.That(editor.EditorStyle, Is.EqualTo(PropertyEditorStyle.Compact),
                $"{editor.GetType().Name} ({editor.Header}) must use the node's compact style.");
        }
    }

    private static void AssertNodePropertyMenusHidden(GraphNodeView node)
    {
        PropertyEditorMenu[] menus = node.GetVisualDescendants().OfType<PropertyEditorMenu>().ToArray();
        Assert.That(menus, Is.Not.Empty, "Exercise both generated and embedded property menus.");
        foreach (PropertyEditorMenu menu in menus)
        {
            Assert.That(menu.IsVisible, Is.False);
            Assert.That(menu.DesiredSize.Width, Is.Zero, "Hidden menus must not reserve a column beside the value.");
        }
    }

    private static void AssertNestedEditorSpacing(GraphNodeView node)
    {
        foreach (NodePortView row in node.GetVisualDescendants().OfType<NodePortView>()
                     .Where(row => row.IsEffectivelyVisible
                         && row.ProvidedEditor is { IsEffectivelyVisible: true } editor
                         && editor.GetVisualParent() != null))
        {
            Control editor = row.ProvidedEditor!;
            Assert.That(editor.Bounds.Left, Is.EqualTo(editor.Margin.Left).Within(0.01),
                $"{editor.GetType().Name} must not reserve an extra left port column.");
            Assert.That(row.Bounds.Width - editor.Bounds.Right, Is.EqualTo(editor.Margin.Right).Within(0.01),
                $"{editor.GetType().Name} must not reserve an extra right port column.");
        }
    }

    private static void AssertPortsOnNodeEdge(GraphNodeView node)
    {
        foreach (NodePortView row in node.GetVisualDescendants().OfType<NodePortView>()
                     .Where(row => row.ProvidedEditor != null && row.IsEffectivelyVisible))
        {
            NodePortPoint point = FindPortPoint(node, row);
            Point center = point.TranslatePoint(new Point(5, 5), node)!.Value;
            Assert.That(point.IsEffectivelyVisible, Is.True);
            Assert.That(center.X, Is.EqualTo(0).Within(0.01));
            Assert.That(center.Y, Is.EqualTo(row.TranslatePoint(new Point(0, 9), node)!.Value.Y).Within(0.01));
        }
    }

    private static async Task FinishTransition()
    {
        HeadlessTestHelpers.Render(3);
        await Task.Delay(300);
        HeadlessTestHelpers.Render(3);
    }

    private static async Task<EditViewModel> CreateEditor()
    {
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"nested-ports-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "nested-ports", root))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static void Capture(TopLevel window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_NESTED_PORT_CAPTURE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}

public sealed partial class AliasedBrushPair : EngineObject
{
    public AliasedBrushPair() => ScanProperties<AliasedBrushPair>();

    public IProperty<Brush?> Primary { get; } = Property.Create<Brush?>();

    public IProperty<Brush?> Secondary { get; } = Property.Create<Brush?>();
}

public sealed partial class AliasedBrushList : EngineObject
{
    public AliasedBrushList() => ScanProperties<AliasedBrushList>();

    public IListProperty<AliasedBrushPair> Items { get; } = Property.CreateList<AliasedBrushPair>();
}
