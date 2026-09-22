using System.Collections.Specialized;
using System.Reactive.Linq;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class NestedInputPortTests
{
    private static INestedInputPort Find(GraphNode node, IProperty property)
        => node.NestedInputPorts.Single(p => ReferenceEquals(p.Property?.GetEngineProperty(), property));

    private static LayerInputNode.LayerInputPort<T> Source<T>(GraphModel graph, T value)
    {
        var source = new LayerInputNode();
        var output = new LayerInputNode.LayerInputPort<T>();
        output.SetupProperty("Value");
        output.Property!.SetValue(value);
        source.Items.Add(output);
        graph.Nodes.Add(source);
        return output;
    }

    private static void Evaluate(GraphModel graph)
    {
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, CompositionContext.Default);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PublishingSuppressionStillSynchronizesObjectAndListPorts(bool list)
    {
        var graph = new GraphModel();
        GraphNode node;
        IProperty property;
        Action add;
        Action remove;
        if (list)
        {
            var group = new FactoryNode<TransformGroup>();
            var item = new TranslateTransform();
            node = group;
            property = item.X;
            add = () => group.Object.Children.Add(item);
            remove = () => group.Object.Children.Remove(item);
        }
        else
        {
            var factory = new FactoryNode<Pen>();
            factory.Object.Brush.CurrentValue = null;
            var brush = new SolidColorBrush();
            node = factory;
            property = brush.Opacity;
            add = () => factory.Object.Brush.CurrentValue = brush;
            remove = () => factory.Object.Brush.CurrentValue = null;
        }
        graph.Nodes.Add(node);
        var source = Source(graph, 42f);
        using var history = new HistoryHarness(graph);
        INestedInputPort port;
        using (PublishingSuppression.Enter())
        {
            add();
            port = Find(node, property);
            graph.Connect(port, source);
        }
        Assert.That(history.History.HasPendingOperations, Is.False);
        Evaluate(graph);
        Assert.That(((IProperty<float>)property).GetValue(CompositionContext.Default), Is.EqualTo(42f));

        using (PublishingSuppression.Enter()) remove();

        Assert.That(node.NestedInputPorts, Does.Not.Contain(port));
        Assert.That(graph.AllConnections, Is.Empty);
        Assert.That(property.Expression, Is.Null);
    }

    [Test]
    public void NestedValuesAreEvaluatedWithoutCreatingAnEditor()
    {
        var graph = new GraphModel();
        var node = new GeometryShapeNode();
        var brush = new SolidColorBrush();
        var pen = new Pen();
        pen.Brush.CurrentValue = brush;
        node.Pen.Property!.SetValue(pen);
        graph.Nodes.Add(node);
        int roots = node.Items.Count;
        graph.Connect(Find(node, pen.Thickness), Source(graph, 17f));
        graph.Connect(Find(node, brush.Color), Source(graph, Colors.Red));

        Evaluate(graph);

        Assert.Multiple(() =>
        {
            Assert.That(pen.Thickness.GetValue(CompositionContext.Default), Is.EqualTo(17f));
            Assert.That(brush.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Red));
            Assert.That(node.Items.Count, Is.EqualTo(roots));
            Assert.That(graph.AllConnections.Select(c => c.Status), Is.All.EqualTo(ConnectionStatus.Success));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ParentAndDescendantConnectionsAreExclusive(bool parentFirst)
    {
        var graph = new GraphModel();
        var node = new GeometryShapeNode();
        var pen = new Pen();
        node.Pen.Property!.SetValue(pen);
        graph.Nodes.Add(node);
        var child = Find(node, pen.Thickness);
        var parentSource = Source(graph, new Pen());
        var childSource = Source(graph, 12f);
        IInputPort first = parentFirst ? node.Pen : child;
        IInputPort second = parentFirst ? child : node.Pen;
        var firstConnection = graph.Connect(first, parentFirst ? parentSource : childSource);

        Assert.That(node.CanConnectInput(second), Is.False);
        Assert.Throws<InvalidOperationException>(() => graph.Connect(second, parentFirst ? childSource : parentSource));
        Assert.That(graph.AllConnections, Is.EqualTo(new[] { firstConnection }));
        Assert.That(second.Connection.IsNull, Is.True);
        graph.Disconnect(firstConnection);
        Assert.That(node.CanConnectInput(second), Is.True);
    }

    [Test]
    public void MutationServiceRejectsConflictingConnectionWithoutRecordingAnOperation()
    {
        var graph = new GraphModel();
        var node = new GeometryShapeNode();
        var pen = new Pen();
        node.Pen.Property!.SetValue(pen);
        graph.Nodes.Add(node);
        var source = Source(graph, 12f);
        graph.Connect(Find(node, pen.Thickness), source);
        var parentSource = Source(graph, new Pen());
        using var history = new HistoryHarness(graph);
        var service = new NodeGraphMutationService(history.History);

        var outcome = service.TryConnect(graph, node, node.Pen,
            parentSource.FindRequiredHierarchicalParent<GraphNode>(), parentSource);

        Assert.That(outcome, Is.EqualTo(NodeConnectOutcome.None));
        Assert.That(history.History.HasPendingOperations, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EditingExpressionAfterDisconnect_UndoRestoresTheActualPreviousExpression(bool nested)
    {
        var graph = new GraphModel();
        SolidColorBrush brush;
        GraphNode node;
        IInputPort input;
        if (nested)
        {
            var pen = new FactoryNode<Pen>();
            brush = new SolidColorBrush();
            pen.Object.Brush.CurrentValue = brush;
            node = pen;
            input = Find(node, brush.Opacity);
        }
        else
        {
            var factory = new FactoryNode<SolidColorBrush>();
            brush = factory.Object;
            node = factory;
            input = (IInputPort)node.Items.Single(p => p.Name == nameof(Brush.Opacity));
        }
        brush.Opacity.Expression = Expression.Create<float>("75");
        graph.Nodes.Add(node);
        var output = Source(graph, 25f);
        using var history = new HistoryHarness(graph);

        var connection = graph.Connect(input, output);
        history.History.Commit("Connect");
        graph.Disconnect(connection);
        history.History.Commit("Disconnect");
        Assert.That(brush.Opacity.Expression, Is.Null);

        var editedExpression = Expression.Create<float>("30");
        brush.Opacity.Expression = editedExpression;
        history.History.Commit("Edit expression");
        Assert.That(history.History.UndoCount, Is.EqualTo(3));

        Assert.That(history.History.Undo(), Is.True);
        Assert.That(brush.Opacity.Expression, Is.Null, "Undo must restore the state after disconnection, not the old expression 75.");
        Assert.That(graph.AllConnections, Is.Empty);
        Assert.That(history.History.HasPendingOperations, Is.False);

        Assert.That(history.History.Redo(), Is.True);
        Assert.That(brush.Opacity.Expression, Is.SameAs(editedExpression));
        Assert.That(history.History.UndoCount, Is.EqualTo(3));
        Assert.That(history.History.HasPendingOperations, Is.False);
    }

    [Test]
    public void ReplacingObjectRebindsMatchingPropertiesAndReleasesOldObject()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<Pen>();
        var oldBrush = new SolidColorBrush();
        node.Object.Brush.CurrentValue = oldBrush;
        graph.Nodes.Add(node);
        INestedInputPort port = Find(node, oldBrush.Color);
        var connection = graph.Connect(port, Source(graph, Colors.Red));
        var replacement = new SolidColorBrush();

        node.Object.Brush.CurrentValue = replacement;
        Evaluate(graph);

        Assert.Multiple(() =>
        {
            Assert.That(Find(node, replacement.Color), Is.SameAs(port));
            Assert.That(port.Connection.Value, Is.SameAs(connection));
            Assert.That(replacement.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Red));
            Assert.That(oldBrush.Color.Expression, Is.Null);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReplacementAndNullChangesRestorePortsAndValuesOnUndo(bool setNull)
    {
        var graph = new GraphModel();
        var node = new FactoryNode<Pen>();
        var original = new SolidColorBrush();
        node.Object.Brush.CurrentValue = original;
        graph.Nodes.Add(node);
        var port = Find(node, original.Color);
        var connection = graph.Connect(port, Source(graph, Colors.Red));
        using var history = new HistoryHarness(graph);
        var replacement = setNull ? null : new SolidColorBrush();

        node.Object.Brush.CurrentValue = replacement;
        history.History.Commit("Replace brush");
        Assert.That(history.History.UndoCount, Is.EqualTo(1));
        Assert.That(original.Color.Expression, Is.Null);
        if (setNull) Assert.That(graph.AllConnections, Is.Empty);
        else Assert.That(Find(node, replacement!.Color), Is.SameAs(port));

        history.History.Undo();
        Evaluate(graph);
        Assert.That(node.Object.Brush.CurrentValue, Is.SameAs(original));
        Assert.That(Find(node, original.Color), Is.SameAs(port));
        Assert.That(graph.AllConnections.Single(), Is.SameAs(connection));
        Assert.That(original.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Red));

        history.History.Redo();
        Evaluate(graph);
        Assert.That(node.Object.Brush.CurrentValue, Is.SameAs(replacement));
        Assert.That(original.Color.Expression, Is.Null);
        if (setNull) Assert.That(graph.AllConnections, Is.Empty);
        else Assert.That(replacement!.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Red));
    }

    [Test]
    public void NestedObjectInputConflictsOnlyWithItsDescendants()
    {
        var graph = new GraphModel();
        var node = new GeometryShapeNode();
        var pen = new Pen();
        var brush = new SolidColorBrush();
        pen.Brush.CurrentValue = brush;
        node.Pen.Property!.SetValue(pen);
        graph.Nodes.Add(node);
        graph.Connect(Find(node, brush.Color), Source(graph, Colors.Red));

        Assert.That(node.CanConnectInput(Find(node, pen.Brush)), Is.False);
        Assert.That(node.CanConnectInput(Find(node, pen.Thickness)), Is.True);
        Assert.That(node.CanConnectInput(Find(node, brush.Opacity)), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AliasedPropertiesRejectASecondConnectionAndKeepTheFirst(bool penFirst)
    {
        var graph = new GraphModel();
        var node = new GeometryShapeNode();
        var brush = new SolidColorBrush();
        var pen = new Pen();
        pen.Brush.CurrentValue = brush;
        node.Fill.Property!.SetValue(brush);
        node.Pen.Property!.SetValue(pen);
        graph.Nodes.Add(node);
        var fillColor = node.NestedInputPorts.Single(p => p.RootMember.Id == node.Fill.Id
            && ReferenceEquals(p.Property!.GetEngineProperty(), brush.Color));
        var penColor = node.NestedInputPorts.Single(p => p.RootMember.Id == node.Pen.Id
            && ReferenceEquals(p.Property!.GetEngineProperty(), brush.Color));
        var first = penFirst ? penColor : fillColor;
        var second = penFirst ? fillColor : penColor;
        var red = Source(graph, Colors.Red);
        var blue = Source(graph, Colors.Blue);
        var connection = graph.Connect(first, red);

        Assert.That(node.CanConnectInput(second), Is.False);
        Assert.Throws<InvalidOperationException>(() => graph.Connect(second, blue));
        Evaluate(graph);
        Assert.That(graph.AllConnections.Single(), Is.SameAs(connection));
        Assert.That(brush.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Red));
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Success));

        graph.Disconnect(connection);
        Assert.That(node.CanConnectInput(second), Is.True);
        graph.Connect(second, blue);
        Evaluate(graph);
        Assert.That(brush.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Blue));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AliasedPropertiesOnDifferentNodesRejectASecondConnection(bool reverse)
    {
        var graph = new GraphModel();
        var first = new GeometryShapeNode();
        var second = new GeometryShapeNode();
        var brush = new SolidColorBrush();
        first.Fill.Property!.SetValue(brush);
        second.Fill.Property!.SetValue(brush);
        graph.Nodes.AddRange([first, second]);
        if (reverse) (first, second) = (second, first);
        var firstPort = Find(first, brush.Color);
        var secondPort = Find(second, brush.Color);
        var red = Source(graph, Colors.Red);
        var blue = Source(graph, Colors.Blue);
        Assert.That(second.CanConnectInput(secondPort), Is.True);
        var connection = graph.Connect(firstPort, red);

        Assert.That(second.CanConnectInput(secondPort), Is.False);
        Assert.Throws<InvalidOperationException>(() => graph.Connect(secondPort, blue));
        Assert.That(second.CanConnectInput(Find(second, brush.Opacity)), Is.True);
        Evaluate(graph);
        Assert.That(graph.AllConnections.Single(), Is.SameAs(connection));
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Success));
        Assert.That(brush.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Red));

        graph.Disconnect(connection);
        Assert.That(second.CanConnectInput(secondPort), Is.True);
        graph.Connect(secondPort, blue);
        Evaluate(graph);
        Assert.That(first.CanConnectInput(firstPort), Is.False);
        Assert.That(brush.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Blue));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RebindingConnectedPathsToTheSamePropertyCanRecoverWithoutLosingConnections(bool differentNodes)
    {
        var graph = new GraphModel();
        var node = new GeometryShapeNode();
        var fillBrush = new SolidColorBrush();
        var penBrush = new SolidColorBrush();
        var pen = new Pen();
        pen.Brush.CurrentValue = penBrush;
        node.Fill.Property!.SetValue(fillBrush);
        node.Pen.Property!.SetValue(pen);
        graph.Nodes.Add(node);
        var otherNode = new GeometryShapeNode();
        if (differentNodes)
        {
            otherNode.Fill.Property!.SetValue(penBrush);
            graph.Nodes.Add(otherNode);
        }
        graph.Connect(Find(node, fillBrush.Color), Source(graph, Colors.Red));
        var penConnection = graph.Connect(Find(differentNodes ? otherNode : node, penBrush.Color), Source(graph, Colors.Blue));
        Evaluate(graph);
        using var history = new HistoryHarness(graph);

        if (differentNodes) otherNode.Fill.Property!.SetValue(fillBrush);
        else pen.Brush.CurrentValue = fillBrush;
        history.History.Commit("Share brush");
        Evaluate(graph);

        Assert.That(graph.AllConnections, Has.Count.EqualTo(2));
        Assert.That(graph.AllConnections.Select(c => c.Status), Is.All.EqualTo(ConnectionStatus.Error));

        Assert.That(history.History.Undo(), Is.True);
        Evaluate(graph);
        Assert.That(graph.AllConnections.Select(c => c.Status), Is.All.EqualTo(ConnectionStatus.Success));
        Assert.That(fillBrush.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Red));
        Assert.That(penBrush.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Blue));

        Assert.That(history.History.Redo(), Is.True);
        Evaluate(graph);
        Assert.That(graph.AllConnections.Select(c => c.Status), Is.All.EqualTo(ConnectionStatus.Error));
        graph.Disconnect(penConnection);
        Evaluate(graph);
        Assert.That(graph.AllConnections.Single().Status, Is.EqualTo(ConnectionStatus.Success));
        Assert.That(fillBrush.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Red));
    }

    [Test]
    public void InsertingListItemWithSamePropertyNamesDoesNotChangeExistingBindings()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<TransformGroup>();
        var original = new TranslateTransform();
        node.Object.Children.Add(original);
        graph.Nodes.Add(node);
        var port = Find(node, original.X);
        graph.Connect(port, Source(graph, 42f));

        var inserted = new TranslateTransform();
        node.Object.Children.Insert(0, inserted);
        Evaluate(graph);

        Assert.That(Find(node, original.X), Is.SameAs(port));
        Assert.That(Find(node, inserted.X).Id, Is.Not.EqualTo(port.Id));
        Assert.That(original.X.GetValue(CompositionContext.Default), Is.EqualTo(42f));
        Assert.That(inserted.X.GetValue(CompositionContext.Default), Is.Zero);
    }

    [Test]
    public void NumericListsAndStructureComponentsAreNotExpandedIntoPorts()
    {
        var node = new GeometryShapeNode();
        var pen = new Pen();
        pen.DashArray.CurrentValue = [1f, 2f];
        node.Pen.Property!.SetValue(pen);

        Assert.That(node.NestedInputPorts.Any(p => p.PropertyPath.Any(s => s.StartsWith("i:"))), Is.False);
        Assert.That(node.NestedInputPorts.All(p => p.PropertyPath.Count == 1), Is.True);
    }

    [Test]
    public void ListPropertyWithoutExpressionSupportOnlyExposesItsElementsProperties()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<TransformGroup>();
        var item = new TranslateTransform();
        node.Object.Children.Add(item);
        graph.Nodes.Add(node);
        var listPort = (IInputPort)node.Items.Single(p => p.Name == nameof(TransformGroup.Children));
        var source = Source(graph, node.Object.Children.CurrentValue);

        Assert.That(node.CanConnectInput(listPort), Is.False);
        Assert.That(node.CanConnectInput(Find(node, item.X)), Is.True);
        Assert.Throws<InvalidOperationException>(() => graph.Connect(listPort, source));
        Assert.That(graph.AllConnections, Is.Empty);
    }

    [Test]
    public void GroupInputsKeepTheirRootIndicesWhenNestedPortsArePresent()
    {
        using var scene = new SceneHistoryHarness("nested-group-inputs");
        var application = new BeutlApplication();
        application.Items.Add(scene.Scene);
        var graph = new GraphModel();
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = graph;
        scene.AddElement().AddObject(drawable);
        var group = new GroupNode();
        graph.Nodes.Add(group);
        var input = new GroupInput();
        var output = new GroupOutput();
        var factory = new FactoryNode<Pen>();
        var brush = new SolidColorBrush();
        factory.Object.Brush.CurrentValue = brush;
        group.Group.Nodes.Add(input);
        group.Group.Nodes.Add(output);
        group.Group.Nodes.Add(factory);
        input.AddNodePort((IInputPort)factory.Items.Single(p => p.Name == "Brush"), out _);
        output.AddNodePort((IOutputPort)factory.Items.Single(p => p.Name == "Output"), out _);
        var roots = group.Items.ToArray();
        graph.Connect(Find(group, brush.Opacity), Source(graph, 25f));

        Evaluate(graph);

        Assert.That(group.Items, Is.EqualTo(roots));
        Assert.That(group.EnumerateMembers().Take(roots.Length), Is.EqualTo(roots));
        Assert.That(factory.Object.Brush.GetValue(CompositionContext.Default)!.Opacity.GetValue(CompositionContext.Default),
            Is.EqualTo(25f));
        Assert.That(graph.AllConnections.Single().Status, Is.EqualTo(ConnectionStatus.Success));
    }

    [Test]
    public void DifferentBrushTypePreservesCommonPropertiesAndDisconnectsRemovedOnes()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<Pen>();
        var brush = new SolidColorBrush();
        node.Object.Brush.CurrentValue = brush;
        graph.Nodes.Add(node);
        var opacity = Find(node, brush.Opacity);
        graph.Connect(opacity, Source(graph, 25f));
        graph.Connect(Find(node, brush.Color), Source(graph, Colors.Red));

        var gradient = new LinearGradientBrush();
        node.Object.Brush.CurrentValue = gradient;
        Evaluate(graph);

        Assert.That(Find(node, gradient.Opacity), Is.SameAs(opacity));
        Assert.That(gradient.Opacity.GetValue(CompositionContext.Default), Is.EqualTo(25f));
        Assert.That(graph.AllConnections, Has.Count.EqualTo(1));
        Assert.That(brush.Color.Expression, Is.Null);
    }

    [Test]
    public void ListMoveKeepsConnectionOnItsObjectAndDeletionCanBeUndone()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<TransformGroup>();
        var first = new TranslateTransform();
        var second = new TranslateTransform();
        node.Object.Children.Add(first);
        node.Object.Children.Add(second);
        graph.Nodes.Add(node);
        INestedInputPort port = Find(node, first.X);
        var connection = graph.Connect(port, Source(graph, 42f));
        using var history = new HistoryHarness(graph);

        node.Object.Children.Move(0, 1);
        history.History.Commit("Move");
        Evaluate(graph);
        Assert.That(Find(node, first.X), Is.SameAs(port));
        Assert.That(first.X.GetValue(CompositionContext.Default), Is.EqualTo(42f));
        Assert.That(second.X.GetValue(CompositionContext.Default), Is.Zero);

        node.Object.Children.Remove(first);
        history.History.Commit("Remove");
        Assert.That(graph.AllConnections, Is.Empty);
        Assert.That(first.X.Expression, Is.Null);

        history.History.Undo();
        Evaluate(graph);
        Assert.That(Find(node, first.X), Is.SameAs(port));
        Assert.That(graph.AllConnections.Single(), Is.SameAs(connection));
        Assert.That(first.X.GetValue(CompositionContext.Default), Is.EqualTo(42f));

        history.History.Redo();
        Assert.That(graph.AllConnections, Is.Empty);
        Assert.That(node.NestedInputPorts.Any(p => p.Id == port.Id), Is.False);
    }

    [Test]
    public void ReplacingListItemDoesNotTransferItsConnection()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<TransformGroup>();
        var first = new TranslateTransform();
        node.Object.Children.Add(first);
        graph.Nodes.Add(node);
        var oldPort = Find(node, first.X);
        graph.Connect(oldPort, Source(graph, 42f));
        var replacement = new TranslateTransform();

        node.Object.Children[0] = replacement;

        Assert.That(graph.AllConnections, Is.Empty);
        Assert.That(Find(node, replacement.X).Id, Is.Not.EqualTo(oldPort.Id));
        Assert.That(first.X.Expression, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SaveAndReloadRestoresNestedConnections(bool list)
    {
        var graph = new GraphModel();
        GraphNode node;
        IProperty target;
        if (list)
        {
            var group = new FactoryNode<TransformGroup>();
            var transform = new TranslateTransform();
            group.Object.Children.Add(transform);
            node = group;
            target = transform.X;
        }
        else
        {
            var pen = new FactoryNode<Pen>();
            var brush = new SolidColorBrush();
            pen.Object.Brush.CurrentValue = brush;
            node = pen;
            target = brush.Opacity;
        }
        graph.Nodes.Add(node);
        var port = Find(node, target);
        graph.Connect(port, Source(graph, 42f));

        var json = CoreSerializer.SerializeToJsonObject(graph);
        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        Evaluate(restored);

        var restoredPort = (INestedInputPort)restored.FindNodePort(port.Id)!;
        Assert.That(restoredPort, Is.Not.Null);
        Assert.That(restoredPort.Property!.GetEngineProperty()!.Expression, Is.InstanceOf<NodePortExpression<float>>());
        Assert.That(((IProperty<float>)restoredPort.Property.GetEngineProperty()!).GetValue(CompositionContext.Default), Is.EqualTo(42f));
        Assert.That(restored.AllConnections.Single().Status, Is.EqualTo(ConnectionStatus.Success));
    }

    [Test]
    public void MissingBrushPreservesConnectionsThroughSaveRepairAndUndo()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<Pen>();
        var brush = new SolidColorBrush();
        node.Object.Brush.CurrentValue = brush;
        graph.Nodes.Add(node);
        var originalPort = Find(node, brush.Color);
        var originalConnection = graph.Connect(originalPort, Source(graph, Colors.Red));
        var json = CoreSerializer.SerializeToJsonObject(graph);
        var brushJson = json["Nodes"]![0]!["Object"]!["Brush"]!.AsObject();
        string savedType = brushJson["$type"]!.GetValue<string>();
        brushJson["$type"] = "[Beutl.Engine]Beutl.Media:MissingBrush";

        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        Evaluate(restored);
        Assert.That(restored.AllConnections.Single().Id, Is.EqualTo(originalConnection.Id));
        Assert.That(restored.FindNodePort(originalPort.Id), Is.Not.Null);

        restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(
            CoreSerializer.SerializeToJsonObject(restored), typeof(GraphModel));
        Evaluate(restored);
        var restoredNode = (FactoryNode<Pen>)restored.Nodes[0];
        var fallback = (FallbackBrush)restoredNode.Object.Brush.CurrentValue!;
        var port = (INestedInputPort)restored.FindNodePort(originalPort.Id)!;
        var connection = restored.AllConnections.Single();
        Assert.Multiple(() =>
        {
            Assert.That(port.Property, Is.Null);
            Assert.That(port.Connection.Value, Is.SameAs(connection));
            Assert.That(connection.Id, Is.EqualTo(originalConnection.Id));
            Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Error));
            Assert.That(restoredNode.CanConnectInput(port), Is.False);
        });

        var repairJson = fallback.Json!.DeepClone().AsObject();
        repairJson["$type"] = savedType;
        var repaired = (SolidColorBrush)CoreSerializer.DeserializeFromJsonObject(repairJson, typeof(Brush));
        using var history = new HistoryHarness(restored);
        var recorded = new List<ChangeOperation>();
        using var subscription = history.Observer.Operations.Subscribe(operation =>
        {
            if (!RecordingSuppression.IsSuppressed) recorded.Add(operation);
        });
        restoredNode.Object.Brush.CurrentValue = repaired;
        history.History.Commit("Repair brush");
        Evaluate(restored);
        Assert.That(Find(restoredNode, repaired.Color), Is.SameAs(port));
        Assert.That(repaired.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Red));
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Success));

        Assert.That(history.History.Undo(), Is.True);
        Evaluate(restored);
        Assert.That(restoredNode.Object.Brush.CurrentValue, Is.SameAs(fallback));
        Assert.That(port.Property, Is.Null);
        Assert.That(repaired.Color.Expression, Is.Null);
        Assert.That(restored.AllConnections.Single(), Is.SameAs(connection));
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Error));

        Assert.That(history.History.Redo(), Is.True);
        Evaluate(restored);
        Assert.That(Find(restoredNode, repaired.Color), Is.SameAs(port));
        Assert.That(repaired.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Red));
        Assert.That(restored.AllConnections.Single(), Is.SameAs(connection));
        // Evaluation publishes connection-status changes independently of editing history.
        Assert.That(recorded.Count(operation => operation is not UpdatePropertyValueOperation<ConnectionStatus>),
            Is.EqualTo(1), "Repairing and rebinding must only record the brush replacement.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingListElementKeepsItsSerializedIdentityUntilRepaired(bool unpopulatedFallback)
    {
        var graph = new GraphModel();
        var node = new FactoryNode<TransformGroup>();
        var transform = new TranslateTransform();
        node.Object.Children.AddRange([transform, new TranslateTransform()]);
        graph.Nodes.Add(node);
        var port = Find(node, transform.X);
        var connection = graph.Connect(port, Source(graph, 42f));
        var json = CoreSerializer.SerializeToJsonObject(graph);
        var transformJson = json["Nodes"]![0]!["Object"]!["Children"]![0]!.AsObject();
        string savedType = transformJson["$type"]!.GetValue<string>();
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:MissingTransform";

        if (unpopulatedFallback)
        {
            // Deserialization exceptions retain the JSON without populating the fallback object.
            var fallback = new FallbackTransform { Json = transformJson.DeepClone().AsObject() };
            Assert.That(fallback.Id, Is.Not.EqualTo(transform.Id));
            node.Object.Children[0] = fallback;
        }
        else
        {
            graph = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
            node = (FactoryNode<TransformGroup>)graph.Nodes[0];
        }
        Evaluate(graph);
        Assert.That(graph.AllConnections.Single().Id, Is.EqualTo(connection.Id));
        Assert.That(((INestedInputPort)graph.FindNodePort(port.Id)!).Property, Is.Null);

        node.Object.Children.Move(0, 1);
        graph = (GraphModel)CoreSerializer.DeserializeFromJsonObject(
            CoreSerializer.SerializeToJsonObject(graph), typeof(GraphModel));
        Evaluate(graph);
        node = (FactoryNode<TransformGroup>)graph.Nodes[0];
        var retainedPort = (INestedInputPort)graph.FindNodePort(port.Id)!;
        var repairJson = ((IFallback)node.Object.Children[1]).Json!.DeepClone().AsObject();
        repairJson["$type"] = savedType;
        var repaired = (TranslateTransform)CoreSerializer.DeserializeFromJsonObject(repairJson, typeof(Transform));
        node.Object.Children[1] = repaired;
        Evaluate(graph);

        Assert.That(Find(node, repaired.X), Is.SameAs(retainedPort));
        Assert.That(graph.AllConnections.Single().Id, Is.EqualTo(connection.Id));
        Assert.That(repaired.X.GetValue(CompositionContext.Default), Is.EqualTo(42f));
        Assert.That(((TranslateTransform)node.Object.Children[0]).X.GetValue(CompositionContext.Default), Is.Zero);
        Assert.That(graph.AllConnections.Single().Status, Is.EqualTo(ConnectionStatus.Success));
    }

    [Test]
    public void BulkListChangesPublishOnePortCollectionAndTopologyUpdate()
    {
        var node = new FactoryNode<TransformGroup>();
        int collectionChanges = 0;
        int bindingChanges = 0;
        int topologyChanges = 0;
        NotifyCollectionChangedAction? action = null;
        node.NestedInputPorts.CollectionChanged += (_, e) =>
        {
            collectionChanges++;
            action = e.Action;
        };
        node.NestedInputPortsChanged += (_, _) => bindingChanges++;
        node.TopologyChanged += (_, _) => topologyChanges++;
        var transforms = Enumerable.Range(0, 800).Select(i => (Transform)new TranslateTransform(i, 0)).ToArray();

        node.Object.Children.AddRange(transforms);

        Assert.That(node.NestedInputPorts, Has.Count.EqualTo(1600));
        Assert.That(collectionChanges, Is.EqualTo(1));
        Assert.That(bindingChanges, Is.EqualTo(1));
        Assert.That(topologyChanges, Is.EqualTo(1));
        Assert.That(action, Is.EqualTo(NotifyCollectionChangedAction.Add));

        node.Object.Children.Clear();

        Assert.That(node.NestedInputPorts, Is.Empty);
        Assert.That(collectionChanges, Is.EqualTo(2));
        Assert.That(bindingChanges, Is.EqualTo(2));
        Assert.That(topologyChanges, Is.EqualTo(2));
        Assert.That(action, Is.EqualTo(NotifyCollectionChangedAction.Remove));
    }

    [Test]
    public void BulkDeletionAndUndoRestoreOriginalPortsAndConnections()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<TransformGroup>();
        var first = new TranslateTransform();
        var second = new TranslateTransform();
        var third = new TranslateTransform();
        node.Object.Children.AddRange([first, second, third]);
        graph.Nodes.Add(node);
        var firstConnection = graph.Connect(Find(node, first.X), Source(graph, 42f));
        var thirdConnection = graph.Connect(Find(node, third.X), Source(graph, 73f));
        var originalPorts = node.NestedInputPorts.ToArray();
        // The deleted elements occupy separate ranges in the stable port collection.
        node.Object.Children.Move(1, 2);
        using var history = new HistoryHarness(graph);

        node.Object.Children.RemoveRange(0, 2);
        history.History.Commit("Remove transforms");
        Assert.That(history.History.UndoCount, Is.EqualTo(1));
        Assert.That(graph.AllConnections, Is.Empty);
        Assert.That(node.NestedInputPorts, Has.Count.EqualTo(2));

        Assert.That(history.History.Undo(), Is.True);
        Evaluate(graph);
        Assert.That(node.Object.Children, Is.EqualTo(new[] { first, third, second }));
        Assert.That(node.NestedInputPorts, Is.EqualTo(originalPorts));
        Assert.That(graph.AllConnections, Is.EquivalentTo(new[] { firstConnection, thirdConnection }));
        Assert.That(first.X.GetValue(CompositionContext.Default), Is.EqualTo(42f));
        Assert.That(third.X.GetValue(CompositionContext.Default), Is.EqualTo(73f));

        Assert.That(history.History.Redo(), Is.True);
        Assert.That(node.Object.Children, Is.EqualTo(new[] { second }));
        Assert.That(graph.AllConnections, Is.Empty);
        Assert.That(first.X.Expression, Is.Null);
        Assert.That(third.X.Expression, Is.Null);
    }

    [Test]
    public void LegacyGraphWithoutNestedPortDataBuildsPortsOnLoad()
    {
        var node = new FactoryNode<Pen>();
        node.Object.Brush.CurrentValue = new SolidColorBrush();
        var json = CoreSerializer.SerializeToJsonObject(node);
        json.Remove(nameof(GraphNode.NestedInputPorts));

        var restored = (FactoryNode<Pen>)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphNode));

        Assert.That(Find(restored, ((SolidColorBrush)restored.Object.Brush.CurrentValue!).Color), Is.Not.Null);
    }

    [Test]
    public void SavingAnUnchangedNodeDoesNotInvalidateItsTopology()
    {
        var node = new FactoryNode<Pen>();
        node.Object.Brush.CurrentValue = new SolidColorBrush();
        int changes = 0;
        node.TopologyChanged += (_, _) => changes++;

        CoreSerializer.SerializeToJsonObject(node);

        Assert.That(changes, Is.Zero);
    }

    [Test]
    public void RemovingNodeDisconnectsItsNestedInputsAndUndoRestoresThem()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<Pen>();
        var brush = new SolidColorBrush();
        node.Object.Brush.CurrentValue = brush;
        graph.Nodes.Add(node);
        var connection = graph.Connect(Find(node, brush.Color), Source(graph, Colors.Red));
        using var history = new HistoryHarness(graph);
        var service = new NodeGraphMutationService(history.History);

        service.RemoveNode(graph, node);
        Assert.That(graph.AllConnections, Is.Empty);
        history.History.Undo();
        Evaluate(graph);

        Assert.That(graph.AllConnections.Single(), Is.SameAs(connection));
        Assert.That(brush.Color.GetValue(CompositionContext.Default), Is.EqualTo(Colors.Red));
    }
}
