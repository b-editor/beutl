using System.Reactive.Linq;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class InvalidNestedInputExpressionTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void RejectedAliasesUseLocalOrAnimatedValuesAndRecoverThroughHistory(bool grouped, bool animated)
    {
        var graph = new GraphModel();
        var group = new GroupNode();
        if (grouped) graph.Nodes.Add(group);
        GraphModel otherGraph = grouped ? group.Group : graph;
        var first = new GeometryShapeNode();
        var second = new GeometryShapeNode();
        var shared = new SolidColorBrush(Colors.White, 35f);
        var separate = new SolidColorBrush(Colors.White, 65f);
        if (animated)
        {
            var animation = new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(new KeyFrame<float> { Value = 55f });
            shared.Opacity.Animation = animation;
        }
        first.Fill.Property!.SetValue(shared);
        second.Fill.Property!.SetValue(separate);
        graph.Nodes.Add(first);
        otherGraph.Nodes.Add(second);
        var firstConnection = graph.Connect(Find(first, shared.Opacity), Source(graph, 12f));
        var secondConnection = otherGraph.Connect(Find(second, separate.Opacity), Source(otherGraph, 90f));
        Evaluate(graph);
        using var resource = shared.ToResource(CompositionContext.Default);
        Assert.That(resource.Opacity, Is.EqualTo(12f));
        using var history = new HistoryHarness(graph);
        var recordedExpressions = new List<ChangeOperation>();
        using var subscription = history.Observer.Operations.Subscribe(operation =>
        {
            if (!RecordingSuppression.IsSuppressed && operation is IPropertyPathProvider path
                && path.PropertyPath.EndsWith(".Expression", StringComparison.Ordinal))
                recordedExpressions.Add(operation);
        });

        void CheckFallback()
        {
            Evaluate(graph);
            bool updateOnly = false;
            resource.Update(shared, CompositionContext.Default, ref updateOnly);
            Assert.Multiple(() =>
            {
                Assert.That(firstConnection.Status, Is.EqualTo(ConnectionStatus.Error));
                Assert.That(secondConnection.Status, Is.EqualTo(ConnectionStatus.Error));
                Assert.That(shared.Opacity.Expression, Is.Null);
                Assert.That(shared.Opacity.GetValue(CompositionContext.Default), Is.EqualTo(animated ? 55f : 35f));
                Assert.That(resource.Opacity, Is.EqualTo(animated ? 55f : 35f));
                Assert.That(recordedExpressions, Is.Empty, "Derived expression changes must not be recorded.");
            });
        }

        second.Fill.Property.SetValue(shared);
        history.History.Commit("Share brush");
        Assert.That(history.History.HasPendingOperations, Is.False);
        CheckFallback();
        Assert.That(history.History.UndoCount, Is.EqualTo(1));

        Assert.That(history.History.Undo(), Is.True);
        Evaluate(graph);
        Assert.That(firstConnection.Status, Is.EqualTo(ConnectionStatus.Success));
        Assert.That(secondConnection.Status, Is.EqualTo(ConnectionStatus.Success));
        Assert.That(shared.Opacity.GetValue(CompositionContext.Default), Is.EqualTo(12f));
        Assert.That(separate.Opacity.GetValue(CompositionContext.Default), Is.EqualTo(90f));
        Assert.That(recordedExpressions, Is.Empty);

        Assert.That(history.History.Redo(), Is.True);
        CheckFallback();
        otherGraph.Disconnect(secondConnection);
        Evaluate(graph);
        Assert.That(firstConnection.Status, Is.EqualTo(ConnectionStatus.Success));
        Assert.That(shared.Opacity.GetValue(CompositionContext.Default), Is.EqualTo(12f));
    }

    [Test]
    public void RejectedConnectionDoesNotClearAUserExpression()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<Pen>();
        var brush = new SolidColorBrush();
        node.Object.Brush.CurrentValue = brush;
        graph.Nodes.Add(node);
        var connection = graph.Connect(Find(node, brush.Opacity), Source(graph, 12f));
        var expression = Expression.Create<float>("73");
        brush.Opacity.Expression = expression;
        node.Object.Brush.Expression = Expression.CreateReference<Brush>(Guid.NewGuid());

        Evaluate(graph);

        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Error));
        Assert.That(brush.Opacity.Expression, Is.SameAs(expression));
        Assert.That(brush.Opacity.GetValue(CompositionContext.Default), Is.EqualTo(73f));
    }

    private static INestedInputPort Find(GraphNode node, IProperty property)
        => node.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), property));

    private static IOutputPort Source(GraphModel graph, float value)
    {
        var source = new LayerInputNode();
        var output = new LayerInputNode.LayerInputPort<float>();
        output.SetupProperty("Opacity");
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
}
