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
        outer.Group.Nodes.Add(inner);
        if (mutual)
        {
            var backReference = new GroupNode();
            inner.Group.Nodes.Add(backReference);
            SetGroup(backReference, outer.Group);
        }
        else
        {
            SetGroup(inner, outer.Group);
        }
        Assert.That(graph.EnumerateGraphs().Take(4), Is.EqualTo(mutual
            ? new GraphModel[] { graph, outer.Group, inner.Group }
            : new GraphModel[] { graph, outer.Group }));
    }

    [Test]
    public void SharedGroupCountsItsConnectedWriterAndTopologyNotificationOnce()
    {
        var graph = new GraphModel();
        var first = new GroupNode();
        var second = new GroupNode();
        SetGroup(second, first.Group);
        var brush = new SolidColorBrush();
        var node = new GeometryShapeNode();
        node.Fill.Property!.SetValue(brush);
        first.Group.Nodes.Add(node);
        var output = Source(first.Group, 37f, "Opacity");
        graph.Nodes.AddRange([first, second]);
        var port = Find(node, brush.Opacity);
        var connection = first.Group.Connect(port, output);
        int notifications = 0;
        first.Group.TopologyChanged += (_, _) => notifications++;
        graph.Nodes.Add(new LayerInputNode());
        int notificationsForOneEdit = notifications;

        using var snapshot = new GraphSnapshot();
        snapshot.Build(first.Group, CompositionContext.Default);
        snapshot.Evaluate(CompositionContext.Default);

        Assert.Multiple(() =>
        {
            Assert.That(notificationsForOneEdit, Is.EqualTo(1));
            Assert.That(node.CanConnectInput(port), Is.True);
            Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Success));
            Assert.That(brush.Opacity.Evaluate(CompositionContext.Default), Is.EqualTo(37f));
        });
    }

    private static void SetGroup(GroupNode node, GraphGroup group)
        => typeof(ValueOnlyProperty<GraphGroup>).GetField("_currentValue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(node.GroupProperty, group);
}
