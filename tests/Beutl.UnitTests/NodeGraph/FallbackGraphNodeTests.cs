using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Serialization;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class FallbackGraphNodeTests
{
    private const string MissingType = "[Missing.Plugin]Missing.Nodes:UnavailableNode";

    [Test]
    public void UnknownNode_RestoresIdentityAndLayout()
    {
        var original = new RandomSingleNode { Name = "Missing node", Position = (120, 80), IsExpanded = false };
        JsonObject json = CoreSerializer.SerializeToJsonObject(original);
        json["$type"] = MissingType;

        var restored = (GraphNode)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphNode));

        Assert.That(restored, Is.TypeOf<FallbackGraphNode>());
        var fallback = (IFallback)restored;
        Assert.Multiple(() =>
        {
            Assert.That(restored.Id, Is.EqualTo(original.Id));
            Assert.That(restored.Name, Is.EqualTo(original.Name));
            Assert.That(restored.Position, Is.EqualTo(original.Position));
            Assert.That(restored.IsExpanded, Is.False);
            Assert.That(fallback.Reason, Is.EqualTo(FallbackReason.TypeNotFound));
            Assert.That(fallback.TryGetTypeName(out string? typeName), Is.True);
            Assert.That(typeName, Is.EqualTo(MissingType));
            Assert.That(JsonNode.DeepEquals(fallback.Json, json), Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Graph_RetainsFailedNodePayloadAndConnections_AndStillEvaluates(bool deserializationFails)
    {
        var graph = new GraphModel();
        var source = new RandomSingleNode();
        var output = new OutputNode();
        graph.Nodes.AddRange([source, output]);
        Connection connection = graph.Connect(output.InputPort, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        JsonObject nodeJson = json[nameof(GraphModel.Nodes)]![0]!.AsObject();
        nodeJson["$type"] = MissingType;
        if (deserializationFails) nodeJson.WriteDiscriminator(typeof(UnreadableGraphNode));
        nodeJson["PluginPayload"] = new JsonObject { ["Value"] = new JsonArray(1, 2, 3) };
        JsonNode originalPayload = nodeJson.DeepClone();
        JsonNode originalConnections = json[nameof(GraphModel.AllConnections)]!.DeepClone();

        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));

        Assert.That(restored.Nodes, Has.Count.EqualTo(2));
        Assert.That(restored.Nodes[0], Is.TypeOf<FallbackGraphNode>());
        Assert.That(restored.Nodes[1], Is.TypeOf<OutputNode>());
        var fallback = (IFallback)restored.Nodes[0];
        Assert.Multiple(() =>
        {
            Assert.That(fallback.Reason, Is.EqualTo(deserializationFails
                ? FallbackReason.DeserializationFailed : FallbackReason.TypeNotFound));
            Assert.That(fallback.ErrorMessage, deserializationFails
                ? Is.EqualTo("Node payload could not be read.") : Is.Null);
            Assert.That(JsonNode.DeepEquals(fallback.Json, originalPayload), Is.True);
            Assert.That(restored.AllConnections.Single().Id, Is.EqualTo(connection.Id));
            Assert.That(restored.AllConnections.Single().Output.Id, Is.EqualTo(source.Value.Id));
        });

        using var snapshot = new GraphSnapshot();
        Assert.DoesNotThrow(() =>
        {
            snapshot.Build(restored, CompositionContext.Default);
            snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
        });

        JsonObject saved = CoreSerializer.SerializeToJsonObject(restored);
        Assert.That(JsonNode.DeepEquals(saved[nameof(GraphModel.Nodes)]![0], originalPayload), Is.True);
        Assert.That(JsonNode.DeepEquals(saved[nameof(GraphModel.AllConnections)], originalConnections), Is.True);
        var reloaded = (GraphModel)CoreSerializer.DeserializeFromJsonObject(saved, typeof(GraphModel));
        Assert.That(reloaded.Nodes[0], Is.TypeOf<FallbackGraphNode>());
        Assert.That(JsonNode.DeepEquals(((IFallback)reloaded.Nodes[0]).Json, originalPayload), Is.True);
    }
}

public partial class UnreadableGraphNode : GraphNode
{
    public override void Deserialize(ICoreSerializationContext context)
        => throw new InvalidOperationException("Node payload could not be read.");
}
