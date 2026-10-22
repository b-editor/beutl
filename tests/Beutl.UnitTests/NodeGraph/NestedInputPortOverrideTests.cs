using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.UnitTests.TestInfrastructure;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

namespace Beutl.UnitTests.NodeGraph;

// An animation and an expression override an input in the same way, so each case only builds its own graph.
[TestFixture]
public class NestedInputPortOverrideTests
{
    [TestCase("animation", false)]
    [TestCase("animation", true)]
    [TestCase("expression", false)]
    [TestCase("expression", true)]
    public void OverriddenAncestorsRejectDescendantsAndRefreshExistingConnections(string overrideKind, bool intermediate)
    {
        var graph = new GraphModel();
        Scenario scenario = overrideKind == "animation" ? AnimatedPen(intermediate) : BrushWithExpression(intermediate);
        GraphNode node = scenario.Node;
        graph.Nodes.Add(node);
        var port = Find(node, scenario.Connected);
        var output = Source(graph, 42f, scenario.Connected.Name);

        scenario.SetOverride(true);

        Assert.That(node.CanConnectInput(port), Is.False);
        Assert.Throws<InvalidOperationException>(() => graph.Connect(port, output));
        Assert.That(node.CanConnectInput(scenario.Parent), Is.True, "The overridden input itself can still be overridden by a connection.");
        Assert.That(graph.AllConnections, Is.Empty);

        scenario.SetOverride(false);
        var connection = graph.Connect(port, output);
        using var history = new HistoryHarness(graph);
        scenario.SetOverride(true);
        history.History.Commit("Override parent");
        Evaluate(graph);
        Assert.That(port.Connection.Value, Is.SameAs(connection));
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Error));

        Assert.That(history.History.Undo(), Is.True);
        Assert.That(node.CanConnectInput(port), Is.True);
        Evaluate(graph);
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Success));
        Assert.That(scenario.Connected.Evaluate(CompositionContext.Default), Is.EqualTo(42f));

        Assert.That(history.History.Redo(), Is.True);
        Assert.That(node.CanConnectInput(port), Is.False);
        Evaluate(graph);
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Error));
    }

    [TestCase("animation")]
    [TestCase("expression")]
    public void AnOverrideOnTheConnectedPropertyItselfDoesNotBlockThePort(string overrideKind)
    {
        GraphNode node;
        IExpressionProperty connected;
        var brush = new SolidColorBrush();
        if (overrideKind == "animation")
        {
            var shape = new GeometryShapeNode();
            brush.Color.SetKeyFrameAnimation(new KeyFrameAnimation<Color>());
            shape.Fill.Property!.SetValue(brush);
            node = shape;
            connected = brush.Color;
        }
        else
        {
            var factory = new FactoryNode<Pen>();
            factory.Object.Brush.Value = brush;
            brush.Opacity.Expression = new ConstantExpression<float>(25f);
            node = factory;
            connected = brush.Opacity;
        }

        Assert.That(node.CanConnectInput(Find(node, connected)), Is.True);
    }

    // The node, the overridden ancestor input, the descendant property the port drives, and the override switch.
    private sealed record Scenario(GraphNode Node, IInputPort Parent, IExpressionProperty<float> Connected, Action<bool> SetOverride);

    private static Scenario AnimatedPen(bool intermediate)
    {
        var pen = new Pen();
        var animation = new KeyFrameAnimation<Pen?>();
        animation.KeyFrames.Add(new KeyFrame<Pen?> { Value = new Pen() });
        if (intermediate)
        {
            var factory = new FactoryNode<AnimatedPenHolder>();
            var container = factory.Object.Value.Value;
            container.Pen.Value = pen;
            return new Scenario(factory, Find(factory, container.Pen), pen.Thickness,
                enabled => container.Pen.SetKeyFrameAnimation(enabled ? animation : null));
        }

        var shape = new GeometryShapeNode();
        shape.Pen.Property!.SetValue(pen);
        var root = (NodePropertyAdapter<Pen?>)shape.Pen.Property;
        return new Scenario(shape, shape.Pen, pen.Thickness, enabled => root.Animation = enabled ? animation : null);
    }

    private static Scenario BrushWithExpression(bool intermediate)
    {
        var brush = new SolidColorBrush();
        var expression = new ConstantExpression<Brush?>(new SolidColorBrush(Colors.Blue));
        GraphNode node;
        IExpressionProperty<Brush?> property;
        if (intermediate)
        {
            var shape = new GeometryShapeNode();
            var pen = new Pen();
            property = pen.Brush;
            property.Value = brush;
            shape.Pen.Property!.SetValue(pen);
            node = shape;
        }
        else
        {
            var factory = new FactoryNode<Pen>();
            property = factory.Object.Brush;
            property.Value = brush;
            node = factory;
        }

        var parent = node.EnumerateMembers().OfType<IInputPort>()
            .Single(p => ReferenceEquals(p.Property?.GetEngineProperty(), property));
        return new Scenario(node, parent, brush.Opacity, enabled => property.Expression = enabled ? expression : null);
    }
}

public sealed partial class AnimatedPenHolder : EngineObject
{
    public AnimatedPenHolder() => ScanProperties<AnimatedPenHolder>();

    public IExpressionProperty<AnimatedPenContainer> Value { get; } = Property.CreateExpression(new AnimatedPenContainer());
}

public sealed partial class AnimatedPenContainer : EngineObject
{
    public AnimatedPenContainer() => ScanProperties<AnimatedPenContainer>();

    public IExpressionProperty<Pen?> Pen { get; } = Property.CreateAnimatable<Pen?>();
}
