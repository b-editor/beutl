using System.Reflection;
using Beutl.Composition;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class GraphTraversalTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void EnumeratingSelfOrMutuallyReferencingGroupsTerminates(bool mutual)
    {
        var outer = new GroupNode();
        var inner = new GroupNode();
        var graph = new GraphModel();
        graph.Nodes.Add(outer);
        outer.Group.Value.Nodes.Add(inner);
        if (mutual)
        {
            var backReference = new GroupNode();
            inner.Group.Value.Nodes.Add(backReference);
            SetGroup(backReference, outer.Group.Value);
        }
        else
        {
            SetGroup(inner, outer.Group.Value);
        }
        Assert.That(graph.EnumerateGraphs().Take(4), Is.EqualTo(mutual
            ? new GraphModel[] { graph, outer.Group.Value, inner.Group.Value }
            : new GraphModel[] { graph, outer.Group.Value }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AddingAGroupNodeInsideItselfThrowsAndLeavesTheGraphUnchanged(bool nested)
    {
        var outer = new GroupNode();
        var inner = new GroupNode();
        outer.Group.Value.Nodes.Add(inner);
        GraphModel target = nested ? inner.Group.Value : outer.Group.Value;
        GraphNode[] nodes = [.. target.Nodes];

        Assert.Throws<InvalidOperationException>(() => target.Nodes.Add(outer));
        Assert.Multiple(() =>
        {
            Assert.That(target.Nodes, Is.EqualTo(nodes));
            Assert.That(((IHierarchical)outer).HierarchicalParent, Is.Null);
        });
    }

    [Test]
    public void SharedGroupCountsItsConnectedWriterAndTopologyNotificationOnce()
    {
        var graph = new GraphModel();
        var first = new GroupNode();
        var second = new GroupNode();
        SetGroup(second, first.Group.Value);
        var brush = new SolidColorBrush();
        var node = new GeometryShapeNode();
        node.Fill.Property!.SetValue(brush);
        first.Group.Value.Nodes.Add(node);
        var output = Source(first.Group.Value, 37f, "Opacity");
        graph.Nodes.AddRange([first, second]);
        var port = Find(node, brush.Opacity);
        var connection = first.Group.Value.Connect(port, output);
        int notifications = 0;
        first.Group.Value.TopologyChanged += (_, _) => notifications++;
        graph.Nodes.Add(new LayerInputNode());
        int notificationsForOneEdit = notifications;

        using var snapshot = new GraphSnapshot();
        snapshot.Build(first.Group.Value, CompositionContext.Default);
        snapshot.Evaluate(CompositionContext.Default);

        Assert.Multiple(() =>
        {
            Assert.That(notificationsForOneEdit, Is.EqualTo(1));
            Assert.That(node.CanConnectInput(port), Is.True);
            Assert.That(connection.Status.Value, Is.EqualTo(ConnectionStatus.Success));
            Assert.That(brush.Opacity.Evaluate(CompositionContext.Default), Is.EqualTo(37f));
        });
    }

    private static void SetGroup(GroupNode node, GraphGroup group)
        => typeof(ValueOnlyProperty<GraphGroup>).GetField("_currentValue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(node.Group, group);
}
