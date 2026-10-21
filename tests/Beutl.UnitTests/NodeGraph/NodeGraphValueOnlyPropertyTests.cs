using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.Serialization;
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
