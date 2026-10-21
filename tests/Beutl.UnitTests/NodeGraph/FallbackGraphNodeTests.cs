using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Serialization;

namespace Beutl.UnitTests.NodeGraph;

public class FallbackGraphNodeTests
{
    [Test]
    public void DisconnectUnavailableInput_UpdatesStatusAndHealthyOutput()
    {
        var source = new RandomSingleNode();
        var output = new OutputNode();
        var graph = new GraphModel();
        graph.Nodes.AddRange([source, output]);
        graph.Connect(output.InputPort, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        json["Nodes"]![1]!["$type"] = "[Missing.Plugin]Missing:Node";
        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        Connection connection = restored.AllConnections.Single();
        connection.Status = ConnectionStatus.Success;

        restored.Disconnect(connection);

        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Disconnected));
        Assert.That(((RandomSingleNode)restored.Nodes[0]).Value.Connections, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GraphRetainsUnavailableNodeAndDisplaysSavedMetadata(bool failed)
    {
        var source = new RandomSingleNode { Name = "Unavailable", Position = (120, 80), IsExpanded = false };
        var output = new OutputNode();
        var graph = new GraphModel();
        graph.Nodes.AddRange([source, output]);
        graph.Connect(output.InputPort, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        JsonObject payload = json["Nodes"]![0]!.AsObject();
        payload["$type"] = "[Missing.Plugin]Missing:Node";
        if (failed) payload.WriteDiscriminator(typeof(UnreadableDisplayNode));
        payload["PluginPayload"] = new JsonObject { ["Unknown"] = 42 };
        JsonNode original = payload.DeepClone();

        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        var fallback = (FallbackGraphNode)restored.Nodes[0];
        Assert.Multiple(() =>
        {
            Assert.That(fallback.Id, Is.EqualTo(source.Id));
            Assert.That(fallback.Name, Is.EqualTo(source.Name));
            Assert.That(fallback.Position, Is.EqualTo(source.Position));
            Assert.That(fallback.IsExpanded, Is.False);
            Assert.That(fallback.Reason, Is.EqualTo(failed ? FallbackReason.DeserializationFailed : FallbackReason.TypeNotFound));
            Assert.That(JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(restored)["Nodes"]![0], original), Is.True);
            Assert.That(restored.AllConnections.Single().Output.Id, Is.EqualTo(source.Value.Id));
        });
        using var snapshot = new GraphSnapshot();
        snapshot.Build(restored, CompositionContext.Default);
        Assert.DoesNotThrow(() => snapshot.Evaluate(CompositionContext.Default));
        // A healthy endpoint must still be able to disconnect an unresolved peer.
        Assert.DoesNotThrow(() => restored.Disconnect(restored.AllConnections.Single()));
        Assert.That(((OutputNode)restored.Nodes[1]).InputPort.Connection.IsNull, Is.True);
        Guid recoveredId = Guid.NewGuid();
        fallback.Id = recoveredId;
        restored.Nodes.Remove(fallback);
        restored.Nodes.Add(fallback);
        Assert.That(fallback.Id, Is.EqualTo(recoveredId));
    }

    // A missing plugin's node saves the metadata every node has, and writes back its ports, its payload and the type it
    // stands in for. Its connection to a port it does not know is kept as it is (#2529).
    [Test]
    public void MovedRenamedAndCollapsedUnavailableNode_SavesThemAndKeepsTheRest()
    {
        var source = new RandomSingleNode { Name = "Unavailable", Position = (120, 80) };
        var output = new OutputNode();
        var graph = new GraphModel();
        graph.Nodes.AddRange([source, output]);
        graph.Connect(output.InputPort, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        JsonObject payload = json["Nodes"]![0]!.AsObject();
        payload["$type"] = "[Missing.Plugin]Missing:Node";
        payload["PluginPayload"] = new JsonObject { ["Unknown"] = 42 };
        JsonObject expected = (JsonObject)payload.DeepClone();
        expected["Position"] = "300,40";
        expected[nameof(GraphNode.Name)] = "Renamed";
        expected[nameof(GraphNode.IsExpanded)] = false;

        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        var fallback = (FallbackGraphNode)restored.Nodes[0];
        fallback.Position = (300, 40);
        fallback.Name = "Renamed";
        fallback.IsExpanded = false;
        JsonObject saved = CoreSerializer.SerializeToJsonObject(restored);
        var reopened = (GraphModel)CoreSerializer.DeserializeFromJsonObject(saved, typeof(GraphModel));

        Assert.Multiple(() =>
        {
            Assert.That(JsonNode.DeepEquals(saved["Nodes"]![0], expected), Is.True);
            Assert.That(reopened.Nodes[0], Is.InstanceOf<FallbackGraphNode>());
            Assert.That(reopened.Nodes[0].Position, Is.EqualTo((300d, 40d)));
            Assert.That(reopened.Nodes[0].Name, Is.EqualTo("Renamed"));
            Assert.That(reopened.Nodes[0].IsExpanded, Is.False);
            Assert.That(reopened.AllConnections.Single().Output.Id, Is.EqualTo(source.Value.Id));
        });
    }
}

public partial class UnreadableDisplayNode : GraphNode
{
    public override void Deserialize(ICoreSerializationContext context) => throw new InvalidOperationException("Invalid node payload.");
}
