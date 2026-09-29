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
        Assert.DoesNotThrow(() => snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default));
        // A healthy endpoint must still be able to disconnect an unresolved peer.
        Assert.DoesNotThrow(() => restored.Disconnect(restored.AllConnections.Single()));
        Assert.That(((OutputNode)restored.Nodes[1]).InputPort.Connection.IsNull, Is.True);
        Guid recoveredId = Guid.NewGuid();
        fallback.Id = recoveredId;
        restored.Nodes.Remove(fallback);
        restored.Nodes.Add(fallback);
        Assert.That(fallback.Id, Is.EqualTo(recoveredId));
    }
}

public partial class UnreadableDisplayNode : GraphNode
{
    public override void Deserialize(ICoreSerializationContext context) => throw new InvalidOperationException("Invalid node payload.");
}
