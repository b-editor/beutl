using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class NodeGraphMutationServiceTests
{
    private HistoryHarness _harness = null!;
    private GraphGroup _graph = null!;
    private HistoryManager _history = null!;
    private NodeGraphMutationService _service = null!;

    [SetUp]
    public void Setup()
    {
        _graph = new GraphGroup();
        _harness = new HistoryHarness(_graph);
        _history = _harness.History;
        _service = new NodeGraphMutationService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NodeGraphMutationService(null!));
    }

    [Test]
    public void AddNode_FirstGroupInput_Added()
    {
        int beforeUndo = _history.UndoCount;
        int beforeNodes = _graph.Nodes.Count;

        bool added = _service.AddNode(_graph, new GroupInput(), x: 100, y: 50);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.True);
            Assert.That(_graph.Nodes.Count, Is.EqualTo(beforeNodes + 1));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });
    }

    [Test]
    public void AddNode_DuplicateGroupInput_Rejected_NoCommit()
    {
        _service.AddNode(_graph, new GroupInput(), 0, 0);
        int beforeUndo = _history.UndoCount;
        int beforeNodes = _graph.Nodes.Count;

        // GraphGroup allows only one GroupInput: the second add must reject (guard centralized here).
        bool added = _service.AddNode(_graph, new GroupInput(), 50, 50);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.False);
            Assert.That(_graph.Nodes.Count, Is.EqualTo(beforeNodes));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo));
        });
    }

    [Test]
    public void AddNode_DuplicateGroupOutput_Rejected()
    {
        _service.AddNode(_graph, new GroupOutput(), 0, 0);

        bool added = _service.AddNode(_graph, new GroupOutput(), 0, 0);

        Assert.That(added, Is.False);
    }

    [Test]
    public void AddNodeAndConnect_AddsAndConnectsInOneUndoStep()
    {
        var source = new FilterEffectInputNode();
        _graph.Nodes.Add(source);
        _history.Commit("Seed");
        int beforeUndo = _history.UndoCount;
        var output = new OutputNode();

        bool added = _service.AddNodeAndConnect(_graph, output, 120, 80, source.Output, output.InputPort);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.True);
            Assert.That(output.Position, Is.EqualTo((120d, 80d)));
            Assert.That(_graph.Nodes, Does.Contain(output));
            Assert.That(_graph.AllConnections, Has.Count.EqualTo(1));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });

        _history.Undo();
        Assert.That(_graph.Nodes, Does.Not.Contain(output));
        Assert.That(_graph.AllConnections, Is.Empty);
    }

    [Test]
    public void AddNodeAndConnect_MaterializesDynamicPortInOneUndoStep()
    {
        var output = new OutputNode();
        _graph.Nodes.Add(output);
        _history.Commit("Seed");
        int beforeUndo = _history.UndoCount;
        var input = new GroupInput();

        bool added = _service.AddNodeAndConnect(_graph, input, 120, 80, output.InputPort, null);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.True);
            Assert.That(input.Items, Has.Count.EqualTo(1));
            Assert.That(_graph.AllConnections, Has.Count.EqualTo(1));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });

        _history.Undo();
        Assert.That(_graph.Nodes, Does.Not.Contain(input));
        Assert.That(_graph.AllConnections, Is.Empty);
    }

    [Test]
    public void AddNodeAndConnect_FromExistingDynamicOutput_ConnectsNewGroupOutputInOneUndoStep()
    {
        var sink = new OutputNode();
        var groupInput = new GroupInput();
        _graph.Nodes.Add(sink);
        _graph.Nodes.Add(groupInput);
        Assert.That(groupInput.AddNodePort(sink.InputPort, out _), Is.True);
        _history.Commit("Seed");
        int beforeUndo = _history.UndoCount;
        var sourcePort = (IOutputPort)groupInput.Items.Single();
        var groupOutput = new GroupOutput();

        bool added = _service.AddNodeAndConnect(_graph, groupOutput, 120, 80, sourcePort, null);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.True);
            Assert.That(groupOutput.Items, Has.Count.EqualTo(1));
            Assert.That(_graph.AllConnections, Has.Count.EqualTo(2));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });

        _history.Undo();
        Assert.That(_graph.Nodes, Does.Not.Contain(groupOutput));
        Assert.That(_graph.AllConnections, Has.Count.EqualTo(1));
    }

    [Test]
    public void AddNodeAndConnect_RejectedConnectionRollsBackNode()
    {
        var source = new FilterEffectInputNode();
        _graph.Nodes.Add(source);
        _history.Commit("Seed");
        int beforeUndo = _history.UndoCount;

        bool added = _service.AddNodeAndConnect(_graph, new OutputNode(), 120, 80, source.Output, null);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.False);
            Assert.That(_graph.Nodes, Has.Count.EqualTo(1));
            Assert.That(_graph.AllConnections, Is.Empty);
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo));
        });
    }

    [Test]
    public void TryConnect_OccupiedSingleInput_DoesNotAddPartialConnection()
    {
        var first = new FilterEffectInputNode();
        var second = new FilterEffectInputNode();
        var target = new OutputNode();
        _graph.Nodes.Add(first);
        _graph.Nodes.Add(second);
        _graph.Nodes.Add(target);
        _history.Commit("Seed");
        Assert.That(_service.TryConnect(_graph, first, first.Output, target, target.InputPort),
            Is.EqualTo(NodeConnectOutcome.Connected));
        int beforeUndo = _history.UndoCount;

        NodeConnectOutcome outcome = _service.TryConnect(_graph, second, second.Output, target, target.InputPort);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(NodeConnectOutcome.None));
            Assert.That(_graph.AllConnections, Has.Count.EqualTo(1));
            Assert.That(second.Output.Connections, Is.Empty);
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo));
        });
    }

    [Test]
    public void TryConnect_DynamicOutputToOccupiedInput_DoesNotAddPartialPort()
    {
        var source = new FilterEffectInputNode();
        var dynamicSource = new GroupInput();
        var target = new OutputNode();
        _graph.Nodes.Add(source);
        _graph.Nodes.Add(dynamicSource);
        _graph.Nodes.Add(target);
        _history.Commit("Seed");
        Assert.That(_service.TryConnect(_graph, source, source.Output, target, target.InputPort),
            Is.EqualTo(NodeConnectOutcome.Connected));
        int beforeUndo = _history.UndoCount;

        NodeConnectOutcome outcome = _service.TryConnect(_graph, dynamicSource, null, target, target.InputPort);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(NodeConnectOutcome.None));
            Assert.That(dynamicSource.Items, Is.Empty);
            Assert.That(_graph.AllConnections, Has.Count.EqualTo(1));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo));
        });
    }

    [Test]
    public void RenameNode_SameName_NoCommit()
    {
        var node = new GroupInput { Name = "Source" };
        _service.AddNode(_graph, node, 0, 0);
        int before = _history.UndoCount;

        bool changed = _service.RenameNode(node, "Source");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(node.Name, Is.EqualTo("Source"));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void RenameNode_DifferentName_CommitsOnce()
    {
        var node = new GroupInput { Name = "Source" };
        _service.AddNode(_graph, node, 0, 0);
        int before = _history.UndoCount;

        bool changed = _service.RenameNode(node, "Renamed");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(node.Name, Is.EqualTo("Renamed"));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void MoveNodes_EmptyList_NoCommit()
    {
        int before = _history.UndoCount;

        _service.MoveNodes([]);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void MoveNodes_MultipleNodes_OneHistoryEntry()
    {
        var a = new GroupInput();
        var b = new GroupOutput();
        _service.AddNode(_graph, a, 0, 0);
        _service.AddNode(_graph, b, 0, 0);
        int before = _history.UndoCount;

        _service.MoveNodes([
            (a, 10, 20),
            (b, 100, 200),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(a.Position, Is.EqualTo((10d, 20d)));
            Assert.That(b.Position, Is.EqualTo((100d, 200d)));
            // All the moves collapse into one history entry.
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void RemoveNode_OnDisconnectedNode_RemovesAndCommits()
    {
        var node = new GroupInput();
        _service.AddNode(_graph, node, 0, 0);
        int beforeUndo = _history.UndoCount;
        int beforeNodes = _graph.Nodes.Count;

        _service.RemoveNode(_graph, node);

        Assert.Multiple(() =>
        {
            Assert.That(_graph.Nodes.Count, Is.EqualTo(beforeNodes - 1));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });
    }

    // A missing plugin's node has no ports, so the connections that name the ports it was saved with go with it, as a
    // node's connections do. Undo brings both back.
    [Test]
    public void RemoveNode_OnAMissingPluginsNode_RemovesItsConnectionsAndUndoBringsThemBack()
    {
        var source = new RandomSingleNode();
        var output = new OutputNode();
        var original = new GraphModel();
        original.Nodes.AddRange([source, output]);
        original.Connect(output.InputPort, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(original);
        json["Nodes"]![0]!["$type"] = "[Missing.Plugin]Missing:Node";
        var graph = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        using var harness = new HistoryHarness(graph);
        var service = new NodeGraphMutationService(harness.History);
        var fallback = (FallbackGraphNode)graph.Nodes[0];
        var healthy = (OutputNode)graph.Nodes[1];
        Connection connection = graph.AllConnections.Single();

        service.RemoveNode(graph, fallback);
        bool removedConnection = graph.AllConnections.Count == 0 && healthy.InputPort.Connection.IsNull;
        bool undone = harness.History.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(removedConnection, Is.True);
            Assert.That(undone, Is.True);
            Assert.That(graph.Nodes, Does.Contain(fallback));
            Assert.That(graph.AllConnections.Single(), Is.SameAs(connection));
            Assert.That(healthy.InputPort.Connection.Id, Is.EqualTo(connection.Id));
        });
    }

    public enum Mutation
    {
        AddNode, AddNodeAndConnect, RemoveNode, MoveNodes, RenameNode, RemovePort,
        MoveConnectionSlot, RenamePort, TryConnect, TryDisconnect, DisconnectAll, DisconnectConnection,
    }

    public enum ClipState { Editable, LockedClip, LockedLayer, Protected }

    // Saving a protected clip would not write the edit, so it is refused there as on a locked clip.
    [Test]
    public void Mutations_OnALockedOrProtectedClip_ChangeNothingAndCommitNothing(
        [Values] Mutation mutation, [Values] ClipState state)
    {
        using var harness = new SceneHistoryHarness("beutl_node_graph_lock");
        Element element = harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(1));
        var drawable = new NodeGraphDrawable();
        element.AddObject(drawable);
        GraphModel graph = drawable.Model.Value!;
        var first = new ExpressionNode();
        var second = new ExpressionNode();
        var free = new ExpressionNode();
        var list = new ExpressionNode();
        var output = new OutputNode();
        var group = new GroupInput();
        graph.Nodes.AddRange([first, second, free, list, output, group]);
        graph.Connect(list.InputPort, first.Output);
        graph.Connect(list.InputPort, second.Output);
        Assert.That(group.AddNodePort(output.InputPort, out Connection? groupConnection), Is.True);
        var groupPort = (INodePort)group.Items.Single();
        switch (state)
        {
            case ClipState.LockedClip:
                element.IsLocked = true;
                break;
            case ClipState.LockedLayer:
                harness.AddLayer(element.ZIndex).IsLocked = true;
                break;
            case ClipState.Protected:
                // Something else in the clip could not be loaded, so removing a node repairs nothing either.
                element.AddObject(RecoveryBlockers.CreateUnmigratedObject());
                element.SuppressedStorageSource = new SuppressedStorageSource([], element.Uri!);
                break;
        }

        harness.History.Commit("Setup");
        string before = CoreSerializer.SerializeToJsonObject(graph).ToJsonString();
        int undoCount = harness.History.UndoCount;
        var service = new NodeGraphMutationService(harness.History);
        var added = new OutputNode();

        switch (mutation)
        {
            case Mutation.AddNode: service.AddNode(graph, new ExpressionNode(), 10, 10); break;
            case Mutation.AddNodeAndConnect: service.AddNodeAndConnect(graph, added, 10, 10, free.Output, added.InputPort); break;
            case Mutation.RemoveNode: service.RemoveNode(graph, first); break;
            case Mutation.MoveNodes: service.MoveNodes([(first, 50, 60)]); break;
            case Mutation.RenameNode: service.RenameNode(first, "Renamed"); break;
            case Mutation.RemovePort: service.RemovePort(group, groupPort); break;
            case Mutation.MoveConnectionSlot: service.MoveConnectionSlot(list.InputPort, 0, 1); break;
            case Mutation.RenamePort: service.RenamePort(groupPort, "Renamed"); break;
            case Mutation.TryConnect: service.TryConnect(graph, free, free.Output, list, list.InputPort); break;
            case Mutation.TryDisconnect: service.TryDisconnect(graph, first.Output, list.InputPort); break;
            case Mutation.DisconnectAll: service.DisconnectAll(graph, list.InputPort); break;
            case Mutation.DisconnectConnection: service.DisconnectConnection(graph, groupConnection!); break;
        }

        string after = CoreSerializer.SerializeToJsonObject(graph).ToJsonString();
        bool editable = state == ClipState.Editable;
        Assert.Multiple(() =>
        {
            Assert.That(after == before, Is.EqualTo(!editable), editable ? "The edit applies to an editable clip." : "The graph must not change.");
            Assert.That(harness.History.UndoCount, Is.EqualTo(undoCount + (editable ? 1 : 0)));
        });
    }

    // A keyframe whose easing could not be read is the one thing in a node that protects its clip, so removing that
    // node repairs the clip; any other node stays as it is while the clip is protected.
    [Test]
    public void RemoveNode_OnAProtectedClip_RemovesOnlyTheNodeHoldingWhatCouldNotBeLoadedAndCompletesTheRepair()
    {
        using var harness = new SceneHistoryHarness("beutl_node_graph_repair");
        Element element = harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(1));
        var drawable = new NodeGraphDrawable();
        element.AddObject(drawable);
        GraphModel graph = drawable.Model.Value!;
        var healthy = new ExpressionNode();
        RectGeometryNode lossy = CreateNodeWithUnreadableEasing();
        graph.Nodes.AddRange([healthy, lossy]);
        element.SuppressedStorageSource = new SuppressedStorageSource([], element.Uri!);
        harness.History.Commit("Setup");
        int undoCount = harness.History.UndoCount;
        var service = new NodeGraphMutationService(harness.History);
        Assert.That(SceneRecovery.HoldsRecoveryBlocker(lossy), Is.True);

        service.RemoveNode(graph, healthy);
        Assert.That(graph.Nodes, Does.Contain(healthy), "Removing a node that holds nothing unreadable repairs nothing.");

        service.RemoveNode(graph, lossy);
        Assert.Multiple(() =>
        {
            Assert.That(graph.Nodes, Does.Not.Contain(lossy));
            Assert.That(element.SuppressedStorageSource, Is.Null, "The repair completes.");
            Assert.That(ElementEditability.IsEditable(element), Is.True);
            Assert.That(harness.History.UndoCount, Is.EqualTo(undoCount + 1));
        });

        Assert.That(harness.History.Undo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(graph.Nodes, Does.Contain(lossy));
            Assert.That(element.SuppressedStorageSource, Is.Not.Null, "Undo puts the protection back.");
        });
    }

    // Reads a node back from JSON whose width keyframe names an easing type that is not loaded.
    private static RectGeometryNode CreateNodeWithUnreadableEasing()
    {
        var node = new RectGeometryNode();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = MediaTime.Zero, Value = 32 });
        node.Object.Width.SetKeyFrameAnimation(animation);
        JsonObject json = CoreSerializer.SerializeToJsonObject(node);
        json["Object"]!["Animations"]![nameof(RectGeometry.Width)]![nameof(IKeyFrameAnimation.KeyFrames)]![0]![nameof(KeyFrame.Easing)] =
            RecoveryBlockers.MissingEasingType;
        return (RectGeometryNode)CoreSerializer.DeserializeFromJsonObject(json, typeof(RectGeometryNode));
    }
}
