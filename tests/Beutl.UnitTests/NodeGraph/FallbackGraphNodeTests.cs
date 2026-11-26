using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Editor.Services;
using Beutl.Graphics.Transformation;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

public class FallbackGraphNodeTests
{
    private const string MissingType = "[Missing.Plugin]Missing:Node";

    [Test]
    public void DisconnectUnavailableInput_UpdatesStatusAndHealthyOutput()
    {
        var source = new RandomSingleNode();
        var output = new OutputNode();
        var graph = new GraphModel();
        graph.Nodes.AddRange([source, output]);
        graph.Connect(output.InputPort, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        json["Nodes"]![1]!["$type"] = MissingType;
        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        Connection connection = restored.AllConnections.Single();
        connection.Status.Value = ConnectionStatus.Success;

        restored.Disconnect(connection);

        Assert.That(connection.Status.Value, Is.EqualTo(ConnectionStatus.Disconnected));
        Assert.That(((RandomSingleNode)restored.Nodes[0]).Value.Connections, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GraphRetainsUnavailableNodeAndDisplaysSavedMetadata(bool failed)
    {
        var source = new RandomSingleNode
        {
            Name = { Value = "Unavailable" },
            Position = { Value = (120, 80) },
            IsExpanded = { Value = false },
            EditorSize = { Value = new(275.5, 192) }
        };
        var output = new OutputNode();
        var graph = new GraphModel();
        graph.Nodes.AddRange([source, output]);
        graph.Connect(output.InputPort, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        JsonObject payload = json["Nodes"]![0]!.AsObject();
        payload["$type"] = MissingType;
        if (failed) payload.WriteDiscriminator(typeof(UnreadableDisplayNode));
        payload["PluginPayload"] = new JsonObject { ["Unknown"] = 42 };
        JsonNode original = payload.DeepClone();

        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        var fallback = (FallbackGraphNode)restored.Nodes[0];
        Assert.Multiple(() =>
        {
            Assert.That(fallback.Id.Value, Is.EqualTo(source.Id.Value));
            Assert.That(fallback.Name.Value, Is.EqualTo(source.Name.Value));
            Assert.That(fallback.Position.Value, Is.EqualTo(source.Position.Value));
            Assert.That(fallback.EditorSize.Value, Is.EqualTo(source.EditorSize.Value));
            Assert.That(fallback.IsExpanded.Value, Is.False);
            Assert.That(fallback.Reason, Is.EqualTo(failed ? FallbackReason.DeserializationFailed : FallbackReason.TypeNotFound));
            Assert.That(JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(restored)["Nodes"]![0], original), Is.True);
            Assert.That(restored.AllConnections.Single().Output.Value.Id, Is.EqualTo(source.Value.Id.Value));
        });
        using var snapshot = new GraphSnapshot();
        snapshot.Build(restored, CompositionContext.Default);
        Assert.DoesNotThrow(() => snapshot.Evaluate(CompositionContext.Default));
        // A healthy endpoint must still be able to disconnect an unresolved peer.
        Assert.DoesNotThrow(() => restored.Disconnect(restored.AllConnections.Single()));
        Assert.That(((OutputNode)restored.Nodes[1]).InputPort.Connection.Value.IsNull, Is.True);
        Guid recoveredId = Guid.NewGuid();
        fallback.Id.Value = recoveredId;
        restored.Nodes.Remove(fallback);
        restored.Nodes.Add(fallback);
        Assert.That(fallback.Id.Value, Is.EqualTo(recoveredId));
    }

    // A missing plugin's node saves the metadata every node has, and writes back its ports, its payload and the type it
    // stands in for. Its connection to a port it does not know is kept as it is (#2529).
    [Test]
    public void MovedRenamedAndCollapsedUnavailableNode_SavesThemAndKeepsTheRest()
    {
        var source = new RandomSingleNode { Name = { Value = "Unavailable" }, Position = { Value = (120, 80) } };
        var output = new OutputNode();
        var graph = new GraphModel();
        graph.Nodes.AddRange([source, output]);
        graph.Connect(output.InputPort, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        JsonObject payload = json["Nodes"]![0]!.AsObject();
        payload["$type"] = MissingType;
        payload["PluginPayload"] = new JsonObject { ["Unknown"] = 42 };
        JsonObject expected = (JsonObject)payload.DeepClone();
        expected["Position"] = "300,40";
        expected[nameof(GraphNode.Name)] = "Renamed";
        expected[nameof(GraphNode.IsExpanded)] = false;

        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        var fallback = (FallbackGraphNode)restored.Nodes[0];
        fallback.Position.Value = (300, 40);
        fallback.Name.Value = "Renamed";
        fallback.IsExpanded.Value = false;
        JsonObject saved = CoreSerializer.SerializeToJsonObject(restored);
        var reopened = (GraphModel)CoreSerializer.DeserializeFromJsonObject(saved, typeof(GraphModel));

        Assert.Multiple(() =>
        {
            Assert.That(JsonNode.DeepEquals(saved["Nodes"]![0], expected), Is.True);
            Assert.That(reopened.Nodes[0], Is.InstanceOf<FallbackGraphNode>());
            Assert.That(reopened.Nodes[0].Position.Value, Is.EqualTo((300d, 40d)));
            Assert.That(reopened.Nodes[0].Name.Value, Is.EqualTo("Renamed"));
            Assert.That(reopened.Nodes[0].IsExpanded.Value, Is.False);
            Assert.That(reopened.AllConnections.Single().Output.Value.Id, Is.EqualTo(source.Value.Id.Value));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SceneSaveAndUndoKeepFallbackListOrderAndRestoreSurvivingEdges(bool failed)
    {
        var graph = new GraphModel();
        var listNode = new ExpressionNode();
        graph.Nodes.Add(listNode);
        var ids = Enumerable.Range(0, 3)
            .Select(i => graph.Connect(listNode.InputPort, NodeGraphTestHelpers.Source(graph, i)).Id.Value)
            .ToArray();
        listNode.InputPort.MoveConnection(2, 0);
        using var fixture = NodeGraphTestHelpers.AttachToScene(graph, "beutl_fallback_list");
        Uri sceneUri = fixture.Scene.Uri!;
        string elementPath = fixture.Scene.Children.Single().Uri!.LocalPath;
        CoreSerializer.StoreToUri(fixture.Scene, sceneUri);
        JsonObject json = ReadElement(elementPath);
        JsonObject node = FirstNode(json);
        JsonNode pluginType = node["$type"]!.DeepClone();
        if (failed)
            node.WriteDiscriminator(typeof(UnreadableDisplayNode));
        else
            node["$type"] = MissingType;
        node["PluginPayload"] = new JsonObject { ["Connection"] = ids[0] };
        Guid orphan = Guid.NewGuid();
        ListReferences(node).Insert(1, orphan);
        File.WriteAllText(elementPath, json.ToJsonString());

        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        GraphModel restored = ElementGraph(scene);
        var fallback = (FallbackGraphNode)restored.Nodes[0];
        using var history = new HistoryHarness(scene);
        var service = new NodeGraphMutationService(history.History);
        if (!failed)
        {
            service.MoveNodes([(fallback, 45, 65)]);
            service.RenameNode(fallback, "Renamed");
        }
        Guid[] surviving = [ids[2], orphan, ids[1]];
        service.DisconnectConnection(restored, restored.AllConnections.Single(c => c.Id.Value == ids[0]));
        CoreSerializer.StoreToUri(scene, sceneUri);
        JsonObject saved = ReadElement(elementPath);
        JsonObject savedNode = FirstNode(saved);
        Assert.Multiple(() =>
        {
            Assert.That(ListReferences(savedNode).Select(item => item!.GetValue<Guid>()), Is.EqualTo(surviving));
            Assert.That(JsonNode.DeepEquals(savedNode["$type"], node["$type"]), Is.True);
            Assert.That(JsonNode.DeepEquals(savedNode["PluginPayload"], node["PluginPayload"]), Is.True);
            Assert.That(JsonNode.DeepEquals(fallback.Json, node), Is.True, "Editing the graph preserves the original fallback JSON.");
            Assert.That(savedNode["Position"]!.GetValue<string>(), Is.EqualTo(failed ? "0,0" : "45,65"));
        });
        GraphModel reopened = ElementGraph(CoreSerializer.RestoreFromUri<Scene>(sceneUri));
        Assert.That(reopened.AllConnections.Select(c => c.Id.Value), Is.EqualTo(new[] { ids[1], ids[2] }));
        Assert.That(reopened.Nodes[0].Name.Value, Is.EqualTo(failed ? string.Empty : "Renamed"));

        Assert.That(history.History.Undo(), Is.True);
        CoreSerializer.StoreToUri(scene, sceneUri);
        Assert.That(JsonNode.DeepEquals(ListReferences(FirstNode(ReadElement(elementPath))), ListReferences(node)), Is.True);
        Assert.That(history.History.Redo(), Is.True);
        if (!failed)
        {
            service.RemoveNode(restored, fallback);
            CoreSerializer.StoreToUri(scene, sceneUri);
            GraphModel afterDeletion = ElementGraph(CoreSerializer.RestoreFromUri<Scene>(sceneUri));
            Assert.That(afterDeletion.Nodes.OfType<FallbackGraphNode>(), Is.Empty);
            Assert.That(afterDeletion.AllConnections, Is.Empty);
            Assert.That(history.History.Undo(), Is.True);
        }
        CoreSerializer.StoreToUri(scene, sceneUri);

        saved = ReadElement(elementPath);
        FirstNode(saved)["$type"] = pluginType;
        File.WriteAllText(elementPath, saved.ToJsonString());
        GraphModel recovered = ElementGraph(CoreSerializer.RestoreFromUri<Scene>(sceneUri));
        var recoveredNode = (ExpressionNode)recovered.Nodes[0];
        Assert.Multiple(() =>
        {
            Assert.That(recoveredNode.InputPort.Connections.Select(reference => reference.Id), Is.EqualTo(surviving));
            Assert.That(recovered.AllConnections.Select(c => c.Id.Value), Is.EqualTo(new[] { ids[1], ids[2] }));
            Assert.That(recovered.AllConnections.All(c => c.Input.Value.Value == recoveredNode.InputPort), Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClearingHistoryReleasesRemovedFallbackConnections(bool deleteNode)
    {
        var source = new GraphModel();
        var input = new ExpressionNode();
        source.Nodes.Add(input);
        source.Connect(input.InputPort, NodeGraphTestHelpers.Source(source, 42f));
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        json[nameof(GraphModel.Nodes)]![0]!["$type"] = MissingType;
        var graph = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        using var history = new HistoryHarness(graph);
        var (node, connection) = RemoveAndClearHistory(graph, history, deleteNode);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Multiple(() =>
        {
            Assert.That(connection.IsAlive, Is.False, "The graph must release the removed connection.");
            if (deleteNode)
            {
                Assert.That(node.IsAlive, Is.False, "The graph must release the removed node and its JSON.");
            }
            else
            {
                JsonObject saved = CoreSerializer.SerializeToJsonObject(graph);
                Assert.That(ListReferences(saved[nameof(GraphModel.Nodes)]![0]!.AsObject()), Is.Empty,
                    "A cut still persists after the connection has been collected.");
            }
        });
        GC.KeepAlive(graph);
    }

    [Test]
    public void SceneRecoveryKeepsProtectedElementBytesWhenFallbackConnectionCutIsRefused()
    {
        var graph = new GraphModel();
        var target = new OutputNode();
        graph.Nodes.Add(target);
        graph.Connect(target.InputPort, NodeGraphTestHelpers.Source(graph, 42));
        using var fixture = NodeGraphTestHelpers.AttachToScene(graph, "beutl_protected_fallback");
        Uri sceneUri = fixture.Scene.Uri!;
        string elementPath = fixture.Scene.Children.Single().Uri!.LocalPath;
        CoreSerializer.StoreToUri(fixture.Scene, sceneUri);
        JsonObject json = ReadElement(elementPath);
        FirstNode(json).WriteDiscriminator(typeof(UnreadableDisplayNode));
        json[nameof(Element.Objects)]!.AsArray().Add(RecoveryBlockers.CreateShapeWithUnreadableEasing());
        File.WriteAllText(elementPath, json.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        GraphModel restored = ElementGraph(scene);
        using var history = new HistoryHarness(scene);
        var service = new NodeGraphMutationService(history.History);
        Assert.That(ElementEditability.IsEditable(scene.Children.Single()), Is.False);
        service.DisconnectConnection(restored, restored.AllConnections.Single());
        CoreSerializer.StoreToUri(scene, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(restored.AllConnections, Has.Count.EqualTo(1));
            Assert.That(history.History.UndoCount, Is.Zero);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void CuttingAnUnavailableNestedInputLeavesTheRestoredPortFree()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<TransformGroup>();
        var transform = new TranslateTransform();
        node.Object.Value.Children.Add(transform);
        graph.Nodes.Add(node);
        INestedInputPort port = NodeGraphTestHelpers.Find(node, transform.X);
        graph.Connect(port, NodeGraphTestHelpers.Source(graph, 42f));
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        JsonObject payload = json[nameof(GraphModel.Nodes)]![0]!.AsObject();
        JsonNode type = payload["$type"]!.DeepClone();
        payload["$type"] = MissingType;
        var unavailable = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        unavailable.Disconnect(unavailable.AllConnections.Single());

        JsonObject saved = CoreSerializer.SerializeToJsonObject(unavailable);
        saved[nameof(GraphModel.Nodes)]![0]!["$type"] = type;
        var recovered = (GraphModel)CoreSerializer.DeserializeFromJsonObject(saved, typeof(GraphModel));
        var recoveredPort = (INestedInputPort)recovered.FindNodePort(port.Id.Value)!;
        Assert.That(recoveredPort.Connection.Value.IsNull, Is.True);
        Assert.That(recovered.AllConnections, Is.Empty);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Node, WeakReference Connection) RemoveAndClearHistory(
        GraphModel graph, HistoryHarness history, bool deleteNode)
    {
        GraphNode node = graph.Nodes[0];
        Connection connection = graph.AllConnections.Single();
        var service = new NodeGraphMutationService(history.History);
        if (deleteNode)
            service.RemoveNode(graph, node);
        else
            service.DisconnectConnection(graph, connection);

        Assert.That(history.History.UndoCount, Is.EqualTo(1));
        history.History.Clear();
        return (new WeakReference(node), new WeakReference(connection));
    }

    private static JsonObject ReadElement(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static JsonObject FirstNode(JsonObject element)
        => element[nameof(Element.Objects)]![0]![nameof(NodeGraphDrawable.Model)]![nameof(GraphModel.Nodes)]![0]!.AsObject();

    private static JsonArray ListReferences(JsonObject node)
        => node[nameof(GraphNode.Items)]![0]![nameof(IListPort.Connections)]!.AsArray();

    private static GraphModel ElementGraph(Scene scene)
        => scene.Children.Single().Objects.OfType<NodeGraphDrawable>().Single().Model.Value!;
}

public partial class UnreadableDisplayNode : GraphNode
{
    public override void Deserialize(ICoreSerializationContext context) => throw new InvalidOperationException("Invalid node payload.");
}
