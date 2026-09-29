using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class FallbackGraphNodeTests
{
    private const string MissingType = "[Missing.Plugin]Missing.Nodes:UnavailableNode";

    [TestCase(false)]
    [TestCase(true)]
    public void FailedNode_RestoresIdentityAndLayout(bool deserializationFails)
    {
        var original = new RandomSingleNode { Name = "Missing node", Position = (120, 80), IsExpanded = false };
        JsonObject json = CoreSerializer.SerializeToJsonObject(original);
        json["$type"] = MissingType;
        if (deserializationFails) json.WriteDiscriminator(typeof(UnreadableGraphNode));

        var restored = (GraphNode)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphNode));

        Assert.That(restored, Is.TypeOf<FallbackGraphNode>());
        var fallback = (IFallback)restored;
        Assert.Multiple(() =>
        {
            Assert.That(restored.Id, Is.EqualTo(original.Id));
            Assert.That(restored.Name, Is.EqualTo(original.Name));
            Assert.That(restored.Position, Is.EqualTo(original.Position));
            Assert.That(restored.IsExpanded, Is.False);
            Assert.That(fallback.Reason, Is.EqualTo(deserializationFails
                ? FallbackReason.DeserializationFailed : FallbackReason.TypeNotFound));
            Assert.That(fallback.TryGetTypeName(out string? typeName), Is.True);
            Assert.That(typeName, Is.EqualTo(json["$type"]!.GetValue<string>()));
            Assert.That(JsonNode.DeepEquals(fallback.Json, json), Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FallbackEdits_SurviveReload_AndUndoRestoresOriginalPayload(bool deserializationFails)
    {
        GraphModel graph = RestoreConnectedGraph(deserializationFails);
        GraphNode fallback = graph.Nodes[1];
        JsonNode original = CoreSerializer.SerializeToJsonObject(graph)["Nodes"]![1]!.DeepClone();
        using var history = new HistoryHarness(graph);
        var service = new NodeGraphMutationService(history.History);
        service.MoveNodes([(fallback, 321, 123)]);
        service.RenameNode(fallback, "Renamed fallback");
        fallback.IsExpanded = false;
        history.History.Commit("Collapse");

        JsonObject saved = CoreSerializer.SerializeToJsonObject(graph);
        var reloaded = (GraphModel)CoreSerializer.DeserializeFromJsonObject(saved, typeof(GraphModel));
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Nodes[1].Id, Is.EqualTo(fallback.Id));
            Assert.That(reloaded.Nodes[1].Position, Is.EqualTo((321d, 123d)));
            Assert.That(reloaded.Nodes[1].Name, Is.EqualTo("Renamed fallback"));
            Assert.That(reloaded.Nodes[1].IsExpanded, Is.False);
            Assert.That(JsonNode.DeepEquals(saved["Nodes"]![1]!["Items"], original["Items"]), Is.True);
            Assert.That(JsonNode.DeepEquals(saved["Nodes"]![1]!["PluginPayload"], original["PluginPayload"]), Is.True);
        });
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(graph)["Nodes"]![1], original), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FallbackPorts_ResolveConnections_AndDeletionIsUndoable(bool deserializationFails)
    {
        GraphModel graph = RestoreConnectedGraph(deserializationFails);
        GraphNode fallback = graph.Nodes[1];
        Connection[] touching = graph.AllConnections.Take(2).ToArray();
        using var history = new HistoryHarness(graph);
        var service = new NodeGraphMutationService(history.History);
        Assert.Multiple(() =>
        {
            Assert.That(touching[0].Input.Value?.FindHierarchicalParent<GraphNode>(), Is.SameAs(fallback));
            Assert.That(touching[1].Output.Value?.FindHierarchicalParent<GraphNode>(), Is.SameAs(fallback));
        });
        service.RemoveNode(graph, fallback);
        Assert.That(graph.AllConnections, Has.Count.EqualTo(1));
        Assert.That(((RandomSingleNode)graph.Nodes[1]).Minimum.Connection.IsNull, Is.True);
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(graph.Nodes, Does.Contain(fallback));
        Assert.That(graph.AllConnections, Has.Count.EqualTo(3));
        Assert.That(touching[0].Input.Value?.FindHierarchicalParent<GraphNode>(), Is.SameAs(fallback));
        Assert.That(history.History.Redo(), Is.True);
        Assert.That(graph.AllConnections, Has.Count.EqualTo(1));
    }

    private static GraphModel RestoreConnectedGraph(bool deserializationFails)
    {
        var graph = new GraphModel();
        var source = new RandomSingleNode();
        var middle = new RandomSingleNode { Name = "Unavailable", Position = (120, 80) };
        var target = new RandomSingleNode();
        graph.Nodes.AddRange([source, middle, target]);
        graph.Connect(middle.Minimum, source.Value);
        graph.Connect(target.Minimum, middle.Value);
        graph.Connect(target.Maximum, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        JsonObject node = json["Nodes"]![1]!.AsObject();
        node["$type"] = MissingType;
        if (deserializationFails) node.WriteDiscriminator(typeof(UnreadableGraphNode));
        node["PluginPayload"] = new JsonObject { ["Opaque"] = "retained" };
        return (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecoveredScene_PersistsFallbackEditsAndDeletion(bool deserializationFails)
    {
        string root = Path.Combine(Path.GetTempPath(), $"fallback-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            var elementUri = new Uri(Path.Combine(root, "element.belm"));
            var drawable = new NodeGraphDrawable();
            drawable.Model.CurrentValue = RestoreConnectedGraph(deserializationFails);
            var element = new Element { Uri = elementUri, Length = TimeSpan.FromSeconds(1) };
            element.AddObject(drawable);
            var scene = new Scene(64, 64, "Fallback") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);

            element = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            GraphModel graph = ((NodeGraphDrawable)element.Objects.Single()).Model.CurrentValue!;
            GraphNode node = graph.Nodes[1];
            using var history = new HistoryHarness(element);
            var service = new NodeGraphMutationService(history.History);
            service.MoveNodes([(node, 432, 234)]);
            service.RenameNode(node, "Saved fallback");
            node.IsExpanded = false;
            history.History.Commit("Collapse");
            CoreSerializer.StoreToUri(element, elementUri);

            var reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            GraphModel reloadedGraph = ((NodeGraphDrawable)reloaded.Objects.Single()).Model.CurrentValue!;
            Assert.That(reloadedGraph.Nodes[1].Position, Is.EqualTo((432d, 234d)));
            Assert.That(reloadedGraph.Nodes[1].Name, Is.EqualTo("Saved fallback"));
            Assert.That(reloadedGraph.Nodes[1].IsExpanded, Is.False);
            Assert.That(reloadedGraph.AllConnections[0].Input.Value, Is.Not.Null);
            Assert.That(reloadedGraph.AllConnections[1].Output.Value, Is.Not.Null);

            service.RemoveNode(graph, node);
            CoreSerializer.StoreToUri(element, elementUri);
            reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            reloadedGraph = ((NodeGraphDrawable)reloaded.Objects.Single()).Model.CurrentValue!;
            Assert.That(reloadedGraph.Nodes, Has.Count.EqualTo(2));
            Assert.That(reloadedGraph.AllConnections, Has.Count.EqualTo(1));
            Assert.That(history.History.Undo(), Is.True);
            CoreSerializer.StoreToUri(element, elementUri);
            reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            reloadedGraph = ((NodeGraphDrawable)reloaded.Objects.Single()).Model.CurrentValue!;
            Assert.That(reloadedGraph.Nodes[1].Name, Is.EqualTo("Saved fallback"));
            Assert.That(reloadedGraph.AllConnections, Has.Count.EqualTo(3));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void PartiallyDeserializedPorts_AreReplacedByFallbackEndpoints()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(RestoreConnectedGraph(false));
        json["Nodes"]![1]!.AsObject().WriteDiscriminator(typeof(PartiallyUnreadableGraphNode));
        var graph = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        Assert.That(graph.Nodes[1], Is.InstanceOf<FallbackGraphNode>());
        Assert.That(graph.AllConnections[0].Input.Value?.FindHierarchicalParent<GraphNode>(), Is.SameAs(graph.Nodes[1]));
        Assert.That(graph.AllConnections[1].Output.Value?.FindHierarchicalParent<GraphNode>(), Is.SameAs(graph.Nodes[1]));
    }

    [Test]
    public void NestedFallbackEndpoint_IsRetainedAndRemovedWithItsNode()
    {
        var graph = new GraphModel();
        var source = new RandomSingleNode();
        var node = new FactoryNode<Pen>();
        var brush = new SolidColorBrush();
        node.Object.Brush.CurrentValue = brush;
        graph.Nodes.AddRange([source, node]);
        INestedInputPort nested = node.NestedInputPorts.Single(p => ReferenceEquals(p.Property?.GetEngineProperty(), brush.Opacity));
        graph.Connect(nested, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        json["Nodes"]![1]!["$type"] = MissingType;
        JsonNode savedNested = json["Nodes"]![1]!["NestedInputPorts"]!.DeepClone();
        graph = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        Assert.That(graph.AllConnections.Single().Input.Value?.Id, Is.EqualTo(nested.Id));
        Assert.That(graph.AllConnections.Single().Input.Value?.FindHierarchicalParent<GraphNode>(), Is.SameAs(graph.Nodes[1]));
        Assert.That(JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(graph)["Nodes"]![1]!["NestedInputPorts"], savedNested), Is.True);
        using var history = new HistoryHarness(graph);
        new NodeGraphMutationService(history.History).RemoveNode(graph, graph.Nodes[1]);
        Assert.That(graph.AllConnections, Is.Empty);
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(graph.AllConnections.Single().Input.Value?.Id, Is.EqualTo(nested.Id));
    }

    [Test]
    public void ListFallbackEndpoint_PreservesOrderAndPersistsDisconnection()
    {
        var graph = new GraphModel();
        var first = new RandomSingleNode();
        var second = new RandomSingleNode();
        var target = new FallbackTestListNode();
        graph.Nodes.AddRange([first, second, target]);
        Connection firstConnection = graph.Connect(target.Input, first.Value);
        Connection secondConnection = graph.Connect(target.Input, second.Value);
        target.Input.MoveConnection(0, 1);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        json["Nodes"]![2]!["$type"] = MissingType;
        graph = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        var port = (IListInputPort)graph.AllConnections[0].Input.Value!;
        Assert.That(port.Connections.Select(c => c.Id), Is.EqualTo(new[] { secondConnection.Id, firstConnection.Id }));
        using var history = new HistoryHarness(graph);
        new NodeGraphMutationService(history.History).DisconnectConnection(graph, graph.AllConnections[0]);
        JsonObject saved = CoreSerializer.SerializeToJsonObject(graph);
        saved["Nodes"]![2]!.AsObject().WriteDiscriminator(typeof(FallbackTestListNode));
        var repaired = (GraphModel)CoreSerializer.DeserializeFromJsonObject(saved, typeof(GraphModel));
        var repairedPort = ((FallbackTestListNode)repaired.Nodes[2]).Input;
        Assert.That(repairedPort.Connections.Single().Id, Is.EqualTo(secondConnection.Id));
        Assert.That(repairedPort.Connections.Single().Value, Is.Not.Null);
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(port.Connections.Select(c => c.Id), Is.EqualTo(new[] { secondConnection.Id, firstConnection.Id }));
    }

    [Test]
    public void RemappedFallbackEndpoints_DoNotClaimReusedIdsDuringDeletionAndUndo()
    {
        GraphModel graph = RestoreConnectedGraph(false);
        GraphNode fallback = graph.Nodes[1];
        NodeMember input = graph.AllConnections[0].Input.Value!;
        NodeMember output = graph.AllConnections[1].Output.Value!;
        Guid previousOutputId = output.Id;
        input.Id = Guid.NewGuid();
        output.Id = Guid.NewGuid();
        var source = (RandomSingleNode)graph.Nodes[0];
        source.Value.Id = previousOutputId;
        Connection surviving = graph.AllConnections[2];
        using var history = new HistoryHarness(graph);
        var service = new NodeGraphMutationService(history.History);

        service.RemoveNode(graph, fallback);

        Assert.That(graph.AllConnections, Is.EqualTo(new[] { surviving }));
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(graph.AllConnections, Has.Count.EqualTo(3));
        Assert.That(surviving.Output.Value, Is.SameAs(source.Value));
        Assert.That(graph.AllConnections[0].Input.Value, Is.SameAs(input));
        Assert.That(graph.AllConnections[1].Output.Value, Is.SameAs(output));

        JsonObject saved = CoreSerializer.SerializeToJsonObject(graph);
        saved["Nodes"]![1]!.AsObject().WriteDiscriminator(typeof(RandomSingleNode));
        var repaired = (GraphModel)CoreSerializer.DeserializeFromJsonObject(saved, typeof(GraphModel));
        Assert.That(repaired.AllConnections[0].Input.Value?.Id, Is.EqualTo(input.Id));
        Assert.That(repaired.AllConnections[1].Output.Value?.Id, Is.EqualTo(output.Id));
        Assert.That(repaired.AllConnections[2].Output.Value?.Id, Is.EqualTo(source.Value.Id));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RemappedNestedFallbackEndpoints_PreserveRootReferences(bool connectedRoot)
    {
        Guid rootId = Guid.NewGuid();
        Guid nestedId = Guid.NewGuid();
        var payload = new JsonObject
        {
            ["$type"] = MissingType,
            ["Items"] = new JsonArray(new JsonObject { ["Id"] = rootId, ["Name"] = "Root" }),
            ["NestedInputPorts"] = new JsonArray(new JsonObject
            {
                ["Id"] = nestedId,
                ["Name"] = "Nested",
                ["RootMember"] = rootId,
                ["PropertyPath"] = new JsonArray("p:Value")
            })
        };
        var fallback = new FallbackGraphNode { Json = payload };
        var source = new RandomSingleNode();
        var graph = new GraphModel();
        graph.Nodes.AddRange([source, fallback]);
        foreach (Guid id in connectedRoot ? new[] { rootId, nestedId } : new[] { nestedId })
        {
            var connection = new Connection();
            connection.SetValue(Connection.InputProperty, new Reference<NodeMember>(id));
            connection.SetValue(Connection.OutputProperty, new Reference<NodeMember>(source.Value));
            graph.AllConnections.Add(connection);
            source.Value.NotifyConnected(connection);
        }
        NodeMember root = ((IHierarchical)fallback).HierarchicalChildren.OfType<NodeMember>().Single(member => member.Name == "Root");
        NodeMember nested = graph.AllConnections.Last().Input.Value!;
        root.Id = Guid.NewGuid();
        nested.Id = Guid.NewGuid();

        JsonObject saved = CoreSerializer.SerializeToJsonObject(graph);
        JsonNode savedFallback = saved["Nodes"]![1]!;
        Assert.That(savedFallback["Items"]![0]!["Id"]!.GetValue<Guid>(), Is.EqualTo(root.Id));
        Assert.That(savedFallback["NestedInputPorts"]![0]!["Id"]!.GetValue<Guid>(), Is.EqualTo(nested.Id));
        Assert.That(savedFallback["NestedInputPorts"]![0]!["RootMember"]!.GetValue<Guid>(), Is.EqualTo(root.Id));
        Assert.That(JsonNode.DeepEquals(fallback.Json, payload), Is.True);
        var reloaded = (GraphModel)CoreSerializer.DeserializeFromJsonObject(saved, typeof(GraphModel));
        if (connectedRoot) Assert.That(reloaded.AllConnections[0].Input.Value?.Id, Is.EqualTo(root.Id));
        Assert.That(reloaded.AllConnections.Last().Input.Value?.Id, Is.EqualTo(nested.Id));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecoveredScene_RemappedFallbackEndpointsReconnectAfterPluginRepair(bool lossyCompanion)
    {
        string root = Path.Combine(Path.GetTempPath(), $"fallback-remapped-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            var elementUri = new Uri(Path.Combine(root, "recovered.belm"));
            GraphModel graph = RestoreConnectedGraph(false);
            Guid inputId = graph.AllConnections[0].Input.Id;
            Guid outputId = graph.AllConnections[1].Output.Id;
            Guid connectionId = graph.AllConnections[0].Id;
            Guid unconnectedId = ((IFallback)graph.Nodes[1]).Json!["Items"]!.AsArray().OfType<JsonObject>()
                .Single(item => item["Name"]!.GetValue<string>() == "Maximum")["Id"]!.GetValue<Guid>();
            var healthy = new Element { Uri = new Uri(Path.Combine(root, "healthy.belm")), Length = TimeSpan.FromSeconds(1) };
            foreach (Guid id in new[] { inputId, outputId, connectionId, graph.Nodes[1].Id, unconnectedId })
                healthy.AddObject(new RectShape { Id = id });
            var drawable = new NodeGraphDrawable();
            drawable.Model.CurrentValue = graph;
            var element = new Element { Uri = elementUri, Length = TimeSpan.FromSeconds(1) };
            element.AddObject(drawable);
            if (lossyCompanion)
                element.AddObject(new FallbackEngineObject
                {
                    Json = new JsonObject { ["$type"] = "[Missing.Plugin]Missing:LossyObject", ["Id"] = Guid.NewGuid() }
                });
            var scene = new Scene(64, 64, "Remapped fallback") { Uri = sceneUri };
            scene.Children.AddRange([healthy, element]);
            CoreSerializer.StoreToUri(scene, sceneUri);

            scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            element = scene.Children.Single(item => item.Uri == elementUri);
            graph = element.Objects.OfType<NodeGraphDrawable>().Single().Model.CurrentValue!;
            Guid remappedInputId = graph.AllConnections[0].Input.Id;
            Guid remappedOutputId = graph.AllConnections[1].Output.Id;
            Guid remappedConnectionId = graph.AllConnections[0].Id;
            Assert.That(remappedInputId, Is.Not.EqualTo(inputId));
            Assert.That(remappedOutputId, Is.Not.EqualTo(outputId));
            Assert.That(remappedConnectionId, Is.Not.EqualTo(connectionId));
            using var history = new HistoryHarness(element);
            if (lossyCompanion)
                new ElementObjectService(history.History).Remove(element, element.Objects.OfType<FallbackEngineObject>().Single());
            CoreSerializer.StoreToUri(element, elementUri);

            JsonObject saved = JsonNode.Parse(File.ReadAllText(elementUri.LocalPath))!.AsObject();
            JsonObject node = saved["Objects"]![0]!["Model"]!["Nodes"]![1]!.AsObject();
            var members = node["Items"]!.AsArray().OfType<JsonObject>().ToDictionary(item => item["Name"]!.GetValue<string>());
            Assert.That(members["Minimum"]["Id"]!.GetValue<Guid>(), Is.EqualTo(remappedInputId));
            Assert.That(members["Value"]["Id"]!.GetValue<Guid>(), Is.EqualTo(remappedOutputId));
            Assert.That(members["Minimum"]["Connection"]!.GetValue<Guid>(), Is.EqualTo(remappedConnectionId));
            Assert.That(members["Maximum"]["Id"]!.GetValue<Guid>(), Is.Not.EqualTo(unconnectedId));
            node.WriteDiscriminator(typeof(RandomSingleNode));
            File.WriteAllText(elementUri.LocalPath, saved.ToJsonString());

            var repaired = CoreSerializer.RestoreFromUri<Element>(elementUri);
            GraphModel repairedGraph = repaired.Objects.OfType<NodeGraphDrawable>().Single().Model.CurrentValue!;
            Assert.That(repairedGraph.AllConnections[0].Input.Value?.Id, Is.EqualTo(remappedInputId));
            Assert.That(repairedGraph.AllConnections[1].Output.Value?.Id, Is.EqualTo(remappedOutputId));
            Assert.That(((RandomSingleNode)repairedGraph.Nodes[1]).Minimum.Connection.Value,
                Is.SameAs(repairedGraph.AllConnections[0]));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void LosslessIdlessFallbackNode_HasStableIdentityAcrossSceneRestores()
    {
        string root = Path.Combine(Path.GetTempPath(), $"fallback-idless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            var element = new Element { Uri = new Uri(Path.Combine(root, "element.belm")), Length = TimeSpan.FromSeconds(1) };
            var graph = new GraphModel();
            graph.Nodes.Add(new FallbackGraphNode { Json = new JsonObject { ["$type"] = MissingType } });
            var drawable = new NodeGraphDrawable();
            drawable.Model.CurrentValue = graph;
            element.AddObject(drawable);
            var scene = new Scene(64, 64, "Idless") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);

            Scene first = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Guid firstId = ((NodeGraphDrawable)first.Children.Single().Objects.Single()).Model.CurrentValue!.Nodes.Single().Id;
            Scene second = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Guid secondId = ((NodeGraphDrawable)second.Children.Single().Objects.Single()).Model.CurrentValue!.Nodes.Single().Id;
            Assert.That(secondId, Is.EqualTo(firstId));
            CoreSerializer.StoreToUri(second, sceneUri);
            Scene third = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Assert.That(((NodeGraphDrawable)third.Children.Single().Objects.Single()).Model.CurrentValue!.Nodes.Single().Id,
                Is.EqualTo(firstId));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void DeletingFallback_DoesNotDisconnectHealthyPortSharingAnUnconnectedMemberId()
    {
        GraphModel graph = RestoreConnectedGraph(false);
        var fallback = (FallbackGraphNode)graph.Nodes[1];
        Guid unusedId = fallback.Json!["Items"]!.AsArray().OfType<JsonObject>()
            .Single(item => item["Name"]!.GetValue<string>() == "Maximum")["Id"]!.GetValue<Guid>();
        var source = (RandomSingleNode)graph.Nodes[0];
        source.Value.Id = unusedId;
        Connection surviving = graph.AllConnections[2];
        using var history = new HistoryHarness(graph);
        new NodeGraphMutationService(history.History).RemoveNode(graph, fallback);
        Assert.That(graph.AllConnections, Is.EqualTo(new[] { surviving }));
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(surviving.Output.Value, Is.SameAs(source.Value));
        Assert.That(graph.AllConnections, Has.Count.EqualTo(3));
    }

    [Test]
    public void InvalidMetadata_IsRetainedWithoutPreventingRecovery()
    {
        var payload = new JsonObject
        {
            ["$type"] = MissingType,
            ["Id"] = "invalid",
            ["Name"] = new JsonArray(1),
            ["Position"] = "NaN,Infinity",
            ["IsExpanded"] = "invalid",
            ["Items"] = new JsonArray(new JsonObject { ["Id"] = "invalid", ["Opaque"] = true })
        };
        var fallback = (GraphNode)CoreSerializer.DeserializeFromJsonObject(payload, typeof(GraphNode));
        var graph = new GraphModel();
        graph.Nodes.Add(fallback);
        Assert.That(fallback.Position, Is.EqualTo((0d, 0d)));
        Assert.That(JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(graph)["Nodes"]![0], payload), Is.True);
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
            Assert.That(restored.AllConnections.Single().Output.Value?.FindHierarchicalParent<GraphNode>(),
                Is.SameAs(restored.Nodes[0]));
        });

        JsonObject saved = CoreSerializer.SerializeToJsonObject(restored);
        Assert.That(JsonNode.DeepEquals(saved[nameof(GraphModel.AllConnections)], originalConnections), Is.True);
        using var snapshot = new GraphSnapshot();
        Assert.DoesNotThrow(() =>
        {
            snapshot.Build(restored, CompositionContext.Default);
            snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
        });

        Assert.That(restored.AllConnections.Single().Status, Is.EqualTo(ConnectionStatus.Error));
        saved = CoreSerializer.SerializeToJsonObject(restored);
        Assert.That(JsonNode.DeepEquals(saved[nameof(GraphModel.Nodes)]![0], originalPayload), Is.True);
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

public partial class PartiallyUnreadableGraphNode : RandomSingleNode
{
    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        throw new InvalidOperationException("Node payload could not be read.");
    }
}

public partial class FallbackTestListNode : GraphNode
{
    public FallbackTestListNode() => Input = AddListInput<float>("Inputs");

    public ListInputPort<float> Input { get; }
}
