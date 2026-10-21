using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.UnitTests.TestInfrastructure;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class OverriddenAliasInputTests
{
    [TestCase("same", false, false)]
    [TestCase("same", false, true)]
    [TestCase("same", true, false)]
    [TestCase("same", true, true)]
    [TestCase("nodes", false, false)]
    [TestCase("nodes", false, true)]
    [TestCase("nodes", true, false)]
    [TestCase("nodes", true, true)]
    [TestCase("groups", false, false)]
    [TestCase("groups", false, true)]
    [TestCase("groups", true, false)]
    [TestCase("groups", true, true)]
    public void OverriddenAliasDoesNotBlockTheRemainingWriter(string layout, bool expression, bool rejectedFirst)
    {
        var graph = new GraphModel();
        GraphModel acceptedGraph = graph;
        GraphModel rejectedGraph = graph;
        if (layout == "groups")
        {
            var group = new GroupNode();
            graph.Nodes.Add(group);
            if (rejectedFirst) acceptedGraph = group.Group;
            else rejectedGraph = group.Group;
        }
        var acceptedNode = new FactoryNode<OverridableBrushPair>();
        var rejectedNode = layout == "same" ? acceptedNode : new FactoryNode<OverridableBrushPair>();
        if (layout == "same") graph.Nodes.Add(acceptedNode);
        else if (rejectedFirst)
        {
            rejectedGraph.Nodes.Add(rejectedNode);
            acceptedGraph.Nodes.Add(acceptedNode);
        }
        else
        {
            acceptedGraph.Nodes.Add(acceptedNode);
            rejectedGraph.Nodes.Add(rejectedNode);
        }
        IExpressionProperty<Brush?> acceptedParent = acceptedNode.Object.First;
        IExpressionProperty<Brush?> rejectedParent = rejectedNode.Object.Second;
        if (layout == "same" && rejectedFirst)
            (acceptedParent, rejectedParent) = (rejectedParent, acceptedParent);
        var shared = new SolidColorBrush(Colors.White, 35f);
        var separate = new SolidColorBrush(Colors.White, 65f);
        acceptedParent.Value = shared;
        rejectedParent.Value = separate;
        var acceptedPort = Find(acceptedNode, shared.Opacity);
        var rejectedPort = Find(rejectedNode, separate.Opacity);
        var accepted = acceptedGraph.Connect(acceptedPort, Source(acceptedGraph, 12f, "Opacity"));
        var rejected = rejectedGraph.Connect(rejectedPort, Source(rejectedGraph, 90f, "Opacity"));
        Evaluate(graph);
        using var resource = shared.ToResource(CompositionContext.Default);
        using var history = new HistoryHarness(graph);
        var replacement = new SolidColorBrush(Colors.Blue);
        if (expression) rejectedParent.Expression = new ConstantExpression<Brush?>(replacement);
        else
        {
            var animation = new KeyFrameAnimation<Brush?>();
            animation.KeyFrames.Add(new KeyFrame<Brush?> { Value = replacement });
            rejectedParent.SetKeyFrameAnimation(animation);
        }
        rejectedParent.Value = shared;
        history.History.Commit("Override and share brush");

        void CheckSingleWriter()
        {
            Assert.That(rejectedNode.CanConnectInput(rejectedPort), Is.False);
            Assert.That(acceptedNode.CanConnectInput(acceptedPort), Is.True);
            Evaluate(graph);
            bool versionBumped = false;
            resource.Reconcile(shared, CompositionContext.Default, ref versionBumped);
            Assert.Multiple(() =>
            {
                Assert.That(accepted.Status, Is.EqualTo(ConnectionStatus.Success));
                Assert.That(rejected.Status, Is.EqualTo(ConnectionStatus.Error));
                Assert.That(acceptedPort.Connection.Value, Is.SameAs(accepted));
                Assert.That(rejectedPort.Connection.Value, Is.SameAs(rejected));
                Assert.That(shared.Opacity.GetValue(CompositionContext.Default), Is.EqualTo(12f));
                Assert.That(resource.Opacity, Is.EqualTo(12f));
            });
        }

        CheckSingleWriter();
        Assert.That(history.History.Undo(), Is.True);
        Evaluate(graph);
        Assert.That(accepted.Status, Is.EqualTo(ConnectionStatus.Success));
        Assert.That(rejected.Status, Is.EqualTo(ConnectionStatus.Success));
        Assert.That(separate.Opacity.GetValue(CompositionContext.Default), Is.EqualTo(90f));
        Assert.That(history.History.Redo(), Is.True);
        CheckSingleWriter();

        rejectedParent.Expression = null;
        rejectedParent.SetKeyFrameAnimation(null);
        history.History.Commit("Restore aliased path");
        Assert.That(acceptedNode.CanConnectInput(acceptedPort), Is.False);
        Assert.That(rejectedNode.CanConnectInput(rejectedPort), Is.False);
        Evaluate(graph);
        Assert.That(accepted.Status, Is.EqualTo(ConnectionStatus.Error));
        Assert.That(rejected.Status, Is.EqualTo(ConnectionStatus.Error));
        Assert.That(shared.Opacity.GetValue(CompositionContext.Default), Is.EqualTo(35f));
        Assert.That(history.History.Undo(), Is.True);
        CheckSingleWriter();
    }

}

internal sealed partial class OverridableBrushPair : EngineObject
{
    public OverridableBrushPair() => ScanProperties<OverridableBrushPair>();

    public IExpressionProperty<Brush?> First { get; } = Property.CreateAnimatable<Brush?>();
    public IExpressionProperty<Brush?> Second { get; } = Property.CreateAnimatable<Brush?>();
}
