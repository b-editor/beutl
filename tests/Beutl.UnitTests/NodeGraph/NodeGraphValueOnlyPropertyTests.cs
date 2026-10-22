using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

namespace Beutl.UnitTests.NodeGraph;

// The node graph members that were CoreProperty fields keep their lists and children as value-only properties.
[TestFixture]
public class NodeGraphValueOnlyPropertyTests
{
    [Test]
    public void ReadingAGraphFillsTheListsItHolds()
    {
        var source = new GraphModel();
        var factory = new FactoryNode<Pen>();
        source.Nodes.Add(factory);
        source.Connect(factory.Items.OfType<IInputPort>().First(port => port.Name == "Thickness"), Source(source, 1f, "Thickness"));
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);

        var graph = new GraphModel();
        HierarchicalList<GraphNode> nodes = graph.Nodes;
        HierarchicalList<Connection> connections = graph.AllConnections;
        CoreSerializer.PopulateFromJsonObject(graph, json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.NodesProperty.Value, Is.SameAs(nodes));
            Assert.That(graph.AllConnectionsProperty.Value, Is.SameAs(connections));
            Assert.That(graph.Nodes, Has.Count.EqualTo(2));
            Assert.That(graph.AllConnections, Has.Count.EqualTo(1));
            Assert.That(graph.Nodes.All(node => node.HierarchicalParent == graph), Is.True);
        }
    }

    // As the CoreProperty setters did, setting a kept list replaces its items, so the graph saves what it uses.
    [Test]
    public void SettingAKeptListReplacesItsItems()
    {
        var graph = new GraphModel();
        HierarchicalList<GraphNode> nodes = graph.Nodes;
        HierarchicalList<Connection> connections = graph.AllConnections;
        var factory = new FactoryNode<Pen>();
        var output = new OutputNode();

        graph.NodesProperty.Value = [factory, output];
        graph.AllConnectionsProperty.Value = [];
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.NodesProperty.Value, Is.SameAs(nodes));
            Assert.That(graph.AllConnectionsProperty.Value, Is.SameAs(connections));
            Assert.That(graph.Nodes, Is.EqualTo(new GraphNode[] { factory, output }));
            Assert.That(factory.HierarchicalParent, Is.SameAs(graph));
            Assert.That(json["Nodes"]!.AsArray().Select(node => Guid.Parse(node!["Id"]!.GetValue<string>())),
                Is.EqualTo(new[] { factory.Id, output.Id }));
        }
    }

    // A port keeps the connection list it watches, so moving a connection after another list was set changes the topology.
    [Test]
    public void SettingAPortsConnectionsKeepsTheListItWatches()
    {
        var port = new ListInputPort<float>();
        CoreList<Reference<Connection>> connections = port.Connections;
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        int changes = 0;

        port.ConnectionsProperty.Value = [new Reference<Connection>(first), new Reference<Connection>(second)];
        port.TopologyChanged += (_, _) => changes++;
        port.MoveConnection(0, 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(port.ConnectionsProperty.Value, Is.SameAs(connections));
            Assert.That(connections.Select(reference => reference.Id), Is.EqualTo(new[] { second, first }));
            Assert.That(changes, Is.Positive);
        }
    }

    // Position and IsExpanded are not drawn, so, as their CoreProperty forms, they edit nothing; the history still records them.
    [Test]
    public void MovingAndFoldingANodeEditNeitherTheGraphNorTheScene()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<Pen>();
        graph.Nodes.Add(node);
        using SceneHistoryHarness scene = AttachToScene(graph, "nodegraph-edited");
        int edits = 0;
        graph.Edited += (_, _) => edits++;
        scene.Scene.Edited += (_, _) => edits++;

        node.Position = (10, 20);
        node.IsExpanded = false;
        int editsByTheChanges = edits;
        scene.History.Commit("move");
        scene.History.Undo();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editsByTheChanges, Is.Zero);
            Assert.That(node.Position, Is.EqualTo((0d, 0d)));
            Assert.That(node.IsExpanded, Is.True);
        }
    }

    [Test]
    public void AGroupsInputStaysASingleChildOfItsGraph()
    {
        var group = new GraphGroup();
        var input = new GroupInput();

        group.Nodes.Add(input);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(group.Input, Is.SameAs(input));
            Assert.That(((IHierarchical)group).HierarchicalChildren.Count(child => child == input), Is.EqualTo(1));
        }
    }

    [Test]
    public void ASecondGroupInputTakesThePlaceOfTheFirst()
    {
        var group = new GraphGroup();
        var first = new GroupInput();
        var second = new GroupInput();
        group.Nodes.Add(first);

        group.Nodes.Add(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(group.Input, Is.SameAs(second));
            Assert.That(group.Nodes, Is.EqualTo(new[] { second }));
            Assert.That(first.HierarchicalParent, Is.Null);
        }
    }
}
