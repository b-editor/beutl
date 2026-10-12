using System.Text.Json.Nodes;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities.Struct;
using Beutl.Serialization;

namespace Beutl.UnitTests.NodeGraph;

// Node ports and members read every key they write, so a project with a node graph opens without keeping data this
// build does not know, and without the notice about such data.
public class NodePortUnknownMemberTests
{
    [Test]
    public void DefaultPortsAndMembers_WriteOnlyTheirValue_AndKeepNothingUnknown()
    {
        JsonObject json = Serialize(new RelativePointNode());
        JsonObject[] withValue = Items(json).Where(static item => item.ContainsKey("Setter")).ToArray();

        (GraphModel restored, bool keptUnknownData) = Restore(json);

        Assert.Multiple(() =>
        {
            Assert.That(withValue, Has.Length.EqualTo(3));
            Assert.That(withValue.Select(static item => item.ContainsKey("Property") || item.ContainsKey("Target")),
                Is.All.False);
            Assert.That(UnknownMembersOf(restored), Is.All.Null);
            Assert.That(keptUnknownData, Is.False);
        });
    }

    // Earlier builds wrote the property name and the adapter type next to the value of every port and member that has
    // a property.
    [Test]
    public void TheNameAndTypeEarlierBuildsWrote_AreDropped()
    {
        JsonObject json = Serialize(new RelativePointNode());
        foreach (JsonObject item in Items(json).Where(static item => item.ContainsKey("Setter")))
        {
            item["Property"] = item["Name"]!.DeepClone();
            item["Target"] = TypeFormat.ToString(typeof(NodePropertyAdapter<float>));
        }

        (GraphModel restored, bool keptUnknownData) = Restore(json);

        Assert.Multiple(() =>
        {
            Assert.That(UnknownMembersOf(restored), Is.All.Null);
            Assert.That(keptUnknownData, Is.False);
        });
    }

    [Test]
    public void LayerInputPort_WritesAndRestoresItsProperty_AndDropsTheTypeEarlierBuildsWrote()
    {
        var node = new LayerInputNode();
        var port = new LayerInputNode.LayerInputPort<float>();
        port.SetupProperty("Opacity");
        port.Property!.SetValue(0.5f);
        node.Items.Add(port);
        JsonObject json = Serialize(node);
        JsonObject saved = Items(json).Last();
        string? savedProperty = saved["Property"]?.GetValue<string>();
        bool savedTarget = saved.ContainsKey("Target");
        saved["Target"] = TypeFormat.ToString(typeof(NodePropertyAdapter<float>));

        (GraphModel restored, bool keptUnknownData) = Restore(json);
        LayerInputNode.LayerInputPort<float> restoredPort =
            restored.Nodes[0].Items.OfType<LayerInputNode.LayerInputPort<float>>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(savedProperty, Is.EqualTo("Opacity"));
            Assert.That(savedTarget, Is.False);
            Assert.That((restoredPort.Property as NodePropertyAdapter<float>)?.Name, Is.EqualTo("Opacity"));
            Assert.That(restoredPort.Property?.GetValue(), Is.EqualTo(0.5f));
            Assert.That(UnknownMembersOf(restored), Is.All.Null);
            Assert.That(keptUnknownData, Is.False);
        });
    }

    private static JsonObject Serialize(GraphNode node)
    {
        var graph = new GraphModel();
        graph.Nodes.Add(node);
        return CoreSerializer.SerializeToJsonObject(graph);
    }

    private static IEnumerable<JsonObject> Items(JsonObject graph)
        => graph["Nodes"]![0]!["Items"]!.AsArray().OfType<JsonObject>();

    private static IEnumerable<JsonObject?> UnknownMembersOf(GraphModel graph)
        => graph.Nodes[0].Items.Cast<CoreObject>().Select(static item => item.UnknownMembers);

    private static (GraphModel Graph, bool KeptUnknownData) Restore(JsonObject json)
    {
        var project = new Project();
        GraphModel restored;
        using (RootFileVersions.BeginReading(project))
        {
            restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        }

        return (restored, RootFileVersions.HasKeptUnknownData(project));
    }
}
