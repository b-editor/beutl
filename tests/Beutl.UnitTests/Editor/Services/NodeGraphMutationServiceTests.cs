using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class NodeGraphMutationServiceTests
{
    private GraphGroup _graph = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private NodeGraphMutationService _service = null!;

    [SetUp]
    public void Setup()
    {
        _graph = new GraphGroup();
        var sequence = new OperationSequenceGenerator();
        _history = new HistoryManager(_graph, sequence);
        _observer = new CoreObjectOperationObserver(null, _graph, sequence);
        _history.Subscribe(_observer);
        _service = new NodeGraphMutationService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _observer.Dispose();
        _history.Dispose();
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

        // GraphGroup allows only one GroupInput. The second add must
        // silently reject — historically this guard was duplicated at
        // every call site in NodeGraphViewModel.AddNodePort.
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
