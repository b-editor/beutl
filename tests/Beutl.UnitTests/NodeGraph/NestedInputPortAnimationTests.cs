using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class NestedInputPortAnimationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void AnimatedAncestorsRejectDescendantsAndRefreshExistingConnections(bool intermediate)
    {
        var graph = new GraphModel();
        var pen = new Pen();
        var animation = new KeyFrameAnimation<Pen?>();
        animation.KeyFrames.Add(new KeyFrame<Pen?> { Value = new Pen() });
        GraphNode node;
        IInputPort parent;
        Action<bool> setAnimation;
        if (intermediate)
        {
            var factory = new FactoryNode<AnimatedPenHolder>();
            var container = factory.Object.Value.CurrentValue;
            container.Pen.CurrentValue = pen;
            node = factory;
            parent = node.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), container.Pen));
            setAnimation = enabled => container.Pen.Animation = enabled ? animation : null;
        }
        else
        {
            var shape = new GeometryShapeNode();
            shape.Pen.Property!.SetValue(pen);
            node = shape;
            parent = shape.Pen;
            var root = (NodePropertyAdapter<Pen?>)shape.Pen.Property;
            setAnimation = enabled => root.Animation = enabled ? animation : null;
        }
        graph.Nodes.Add(node);
        var port = node.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), pen.Thickness));
        var source = new LayerInputNode();
        var output = new LayerInputNode.LayerInputPort<float>();
        output.SetupProperty("Thickness");
        output.Property!.SetValue(42f);
        source.Items.Add(output);
        graph.Nodes.Add(source);

        setAnimation(true);

        Assert.That(node.CanConnectInput(port), Is.False);
        Assert.Throws<InvalidOperationException>(() => graph.Connect(port, output));
        Assert.That(node.CanConnectInput(parent), Is.True, "The animated input itself can still be overridden by a connection.");
        Assert.That(graph.AllConnections, Is.Empty);

        setAnimation(false);
        var connection = graph.Connect(port, output);
        using var history = new HistoryHarness(graph);
        setAnimation(true);
        history.History.Commit("Animate parent");
        Evaluate(graph);
        Assert.That(port.Connection.Value, Is.SameAs(connection));
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Error));

        Assert.That(history.History.Undo(), Is.True);
        Assert.That(node.CanConnectInput(port), Is.True);
        Evaluate(graph);
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Success));
        Assert.That(pen.Thickness.GetValue(CompositionContext.Default), Is.EqualTo(42f));

        Assert.That(history.History.Redo(), Is.True);
        Assert.That(node.CanConnectInput(port), Is.False);
        Evaluate(graph);
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Error));
    }

    [Test]
    public void AnimationOnTheConnectedPropertyItselfDoesNotBlockThePort()
    {
        var node = new GeometryShapeNode();
        var brush = new SolidColorBrush();
        brush.Color.Animation = new KeyFrameAnimation<Color>();
        node.Fill.Property!.SetValue(brush);
        var port = node.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), brush.Color));

        Assert.That(node.CanConnectInput(port), Is.True);
    }

    private static void Evaluate(GraphModel graph)
    {
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, CompositionContext.Default);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
    }
}

public sealed partial class AnimatedPenHolder : EngineObject
{
    public AnimatedPenHolder() => ScanProperties<AnimatedPenHolder>();

    public IProperty<AnimatedPenContainer> Value { get; } = Property.Create(new AnimatedPenContainer());
}

public sealed partial class AnimatedPenContainer : EngineObject
{
    public AnimatedPenContainer() => ScanProperties<AnimatedPenContainer>();

    public IProperty<Pen?> Pen { get; } = Property.CreateAnimatable<Pen?>();
}
