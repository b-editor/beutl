using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

namespace Beutl.UnitTests.NodeGraph;

// The node graph members that were CoreProperty fields keep their lists and children as property objects.
[TestFixture]
public class NodeGraphValueOnlyPropertyTests
{
    [Test]
    public void ReadingAGraphFillsTheListsItHolds()
    {
        var source = new GraphModel();
        var factory = new FactoryNode<Pen>();
        source.Nodes.Add(factory);
        source.Connect(factory.Items.OfType<IInputPort>().First(port => port.Name.Value == "Thickness"), Source(source, 1f, "Thickness"));
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);

        var graph = new GraphModel();
        ICoreList<GraphNode> nodes = graph.Nodes.Value;
        ICoreList<Connection> connections = graph.AllConnections.Value;
        CoreSerializer.PopulateFromJsonObject(graph, json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.Nodes.Value, Is.SameAs(nodes));
            Assert.That(graph.AllConnections.Value, Is.SameAs(connections));
            Assert.That(graph.Nodes, Has.Count.EqualTo(2));
            Assert.That(graph.AllConnections, Has.Count.EqualTo(1));
            Assert.That(graph.Nodes.All(node => node.HierarchicalParent == graph), Is.True);
        }
    }

    // As the CoreProperty setters did, setting a list property replaces its items, so the graph saves what it uses.
    [Test]
    public void SettingAListPropertyReplacesItsItems()
    {
        var graph = new GraphModel();
        ICoreList<GraphNode> nodes = graph.Nodes.Value;
        ICoreList<Connection> connections = graph.AllConnections.Value;
        var factory = new FactoryNode<Pen>();
        var output = new OutputNode();

        graph.Nodes.Value = new CoreList<GraphNode> { factory, output };
        graph.AllConnections.Value = new CoreList<Connection>();
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.Nodes.Value, Is.SameAs(nodes));
            Assert.That(graph.AllConnections.Value, Is.SameAs(connections));
            Assert.That(graph.Nodes, Is.EqualTo(new GraphNode[] { factory, output }));
            Assert.That(factory.HierarchicalParent, Is.SameAs(graph));
            Assert.That(json["Nodes"]!.AsArray().Select(node => Guid.Parse(node!["Id"]!.GetValue<string>())),
                Is.EqualTo(new[] { factory.Id.Value, output.Id.Value }));
        }
    }

    // A port keeps the connection list it watches, so moving a connection after another list was set changes the topology.
    [Test]
    public void SettingAPortsConnectionsKeepsTheListItWatches()
    {
        var port = new ListInputPort<float>();
        ICoreList<Reference<Connection>> connections = port.Connections.Value;
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        int changes = 0;

        port.Connections.Value = new CoreList<Reference<Connection>> { new(first), new(second) };
        port.TopologyChanged += (_, _) => changes++;
        port.MoveConnection(0, 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(port.Connections.Value, Is.SameAs(connections));
            Assert.That(connections.Select(reference => reference.Id), Is.EqualTo(new[] { second, first }));
            Assert.That(changes, Is.Positive);
        }
    }

    // Editor layout is not drawn in the scene; history still records it.
    [Test]
    public void ChangingNodeLayoutEditsNeitherTheGraphNorTheScene()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<Pen>();
        graph.Nodes.Add(node);
        using SceneHistoryHarness scene = AttachToScene(graph, "nodegraph-edited");
        int edits = 0;
        graph.Edited += (_, _) => edits++;
        scene.Scene.Edited += (_, _) => edits++;

        node.Position.Value = (10, 20);
        node.EditorSize.Value = new(310, 96);
        node.IsExpanded.Value = false;
        int editsByTheChanges = edits;
        scene.History.Commit("move");
        scene.History.Undo();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editsByTheChanges, Is.Zero);
            Assert.That(node.Position.Value, Is.EqualTo((0d, 0d)));
            Assert.That(node.IsExpanded.Value, Is.True);
            Assert.That(node.EditorSize.Value, Is.EqualTo(default(NodeEditorSize)));
        }
    }

    [Test]
    public void EditorSizeRoundTripsPerNodeAndOlderGraphsUseTheTypeDefault()
    {
        var graph = new GraphModel();
        graph.Nodes.AddRange([
            new ShaderFullscreenPassNode { EditorSize = { Value = new(490.5, 128) } },
            new ShaderFullscreenPassNode(),
            new OutputNode { EditorSize = { Value = new(175) } }
        ]);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        json["Nodes"]![1]!.AsObject().Remove(nameof(GraphNode.EditorSize));
        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));

        Assert.That(restored.Nodes.Select(n => n.EditorSize.Value), Is.EqualTo(new NodeEditorSize[]
            { new(490.5, 128), default, new(175) }));
        Assert.That(restored.Nodes[1].EditorWidth, Is.EqualTo(380));
    }

    [Test]
    public void AGroupsInputStaysASingleChildOfItsGraph()
    {
        var group = new GraphGroup();
        var input = new GroupInput();

        group.Nodes.Add(input);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(group.Input.Value, Is.SameAs(input));
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
            Assert.That(group.Input.Value, Is.SameAs(second));
            Assert.That(group.Nodes, Is.EqualTo(new[] { second }));
            Assert.That(first.HierarchicalParent, Is.Null);
        }
    }
}
