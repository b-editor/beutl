using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class NestedInputPortExpressionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ExpressionAncestorsRejectDescendantsAndRefreshExistingConnections(bool intermediate)
    {
        var graph = new GraphModel();
        var brush = new SolidColorBrush();
        var replacement = new SolidColorBrush(Colors.Blue);
        var expression = new ConstantObjectExpression<Brush?>(replacement);
        GraphNode node;
        IProperty<Brush?> property;
        if (intermediate)
        {
            var shape = new GeometryShapeNode();
            var pen = new Pen();
            property = pen.Brush;
            property.CurrentValue = brush;
            shape.Pen.Property!.SetValue(pen);
            node = shape;
        }
        else
        {
            var factory = new FactoryNode<Pen>();
            property = factory.Object.Brush;
            property.CurrentValue = brush;
            node = factory;
        }
        graph.Nodes.Add(node);
        var port = node.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), brush.Opacity));
        var source = new LayerInputNode();
        var output = new LayerInputNode.LayerInputPort<float>();
        output.SetupProperty("Opacity");
        output.Property!.SetValue(42f);
        source.Items.Add(output);
        graph.Nodes.Add(source);

        property.Expression = expression;

        Assert.That(property.GetValue(CompositionContext.Default), Is.SameAs(replacement));
        Assert.That(node.CanConnectInput(port), Is.False);
        Assert.Throws<InvalidOperationException>(() => graph.Connect(port, output));
        var parent = node.EnumerateMembers().OfType<IInputPort>()
            .Single(p => ReferenceEquals(p.Property?.GetEngineProperty(), property));
        Assert.That(node.CanConnectInput(parent), Is.True);
        Assert.That(graph.AllConnections, Is.Empty);

        property.Expression = null;
        var connection = graph.Connect(port, output);
        using var history = new HistoryHarness(graph);
        property.Expression = expression;
        history.History.Commit("Set parent expression");
        Evaluate(graph);
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Error));
        Assert.That(port.Connection.Value, Is.SameAs(connection));

        Assert.That(history.History.Undo(), Is.True);
        Assert.That(node.CanConnectInput(port), Is.True);
        Evaluate(graph);
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Success));
        Assert.That(brush.Opacity.GetValue(CompositionContext.Default), Is.EqualTo(42f));

        Assert.That(history.History.Redo(), Is.True);
        Assert.That(node.CanConnectInput(port), Is.False);
        Evaluate(graph);
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Error));
    }

    [Test]
    public void ExpressionOnTheConnectedPropertyItselfDoesNotBlockThePort()
    {
        var node = new FactoryNode<Pen>();
        var brush = new SolidColorBrush();
        node.Object.Brush.CurrentValue = brush;
        brush.Opacity.Expression = new ConstantObjectExpression<float>(25f);
        var port = node.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), brush.Opacity));

        Assert.That(node.CanConnectInput(port), Is.True);
    }

    private static void Evaluate(GraphModel graph)
    {
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, CompositionContext.Default);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
    }

    private sealed class ConstantObjectExpression<T>(T value) : IExpression<T>
    {
        public string ExpressionString => "test constant";
        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
        public T Evaluate(ExpressionContext context) => value;
    }
}
