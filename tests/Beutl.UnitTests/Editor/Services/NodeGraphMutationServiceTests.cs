using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
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
            // Three coordinate writes collapse into one MoveNode entry.
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
}
