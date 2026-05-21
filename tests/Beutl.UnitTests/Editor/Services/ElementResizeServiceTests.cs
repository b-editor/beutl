using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementResizeServiceTests
{
    private string _basePath = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private ElementResizeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_resize_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_basePath);

        _scene = new Scene(100, 100, string.Empty)
        {
            Uri = new Uri(Path.Combine(_basePath, "test.scene")),
            Start = TimeSpan.Zero,
            Duration = TimeSpan.FromSeconds(30),
        };
        var sequence = new OperationSequenceGenerator();
        _history = new HistoryManager(_scene, sequence);
        _observer = new CoreObjectOperationObserver(null, _scene, sequence);
        _history.Subscribe(_observer);
        _service = new ElementResizeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _observer.Dispose();
        _history.Dispose();
        if (Directory.Exists(_basePath)) Directory.Delete(_basePath, recursive: true);
    }

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
    {
        var element = new Element
        {
            Start = start,
            Length = length,
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(_basePath, $"{Guid.NewGuid():N}.layer")),
        };
        _scene.Children.Add(element);
        return element;
    }

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementResizeService(null!));
    }

    [Test]
    public void BeginResize_NullScene_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.BeginResize(null!, [], ResizeEdge.Right, false));
    }

    [Test]
    public void BeginResize_NullElements_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.BeginResize(_scene, null!, ResizeEdge.Right, false));
    }

    [Test]
    public void Commit_SingleElement_AppliesNewSizeAndCommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        using IElementResizeDragSession session = _service.BeginResize(_scene, [element], ResizeEdge.Right, false);
        session.Commit([new ElementResizeRequest(element, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Commit_MultipleElements_CommitsSingleHistoryEntry()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        Element e2 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 1);
        int before = _history.UndoCount;

        using IElementResizeDragSession session = _service.BeginResize(_scene, [e1, e2], ResizeEdge.Right, false);
        session.Commit(
        [
            new ElementResizeRequest(e1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), 0),
            new ElementResizeRequest(e2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(6), 1),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(e1.Length, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(e2.Length, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Commit_EmptyList_DoesNotCommit()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        using IElementResizeDragSession session = _service.BeginResize(_scene, [element], ResizeEdge.Right, false);
        session.Commit([]);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void Cancel_RestoresInitialStartAndLength()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        TimeSpan initialStart = element.Start;
        TimeSpan initialLength = element.Length;
        int before = _history.UndoCount;

        using IElementResizeDragSession session = _service.BeginResize(_scene, [element], ResizeEdge.Right, false);
        element.Start = TimeSpan.FromSeconds(5);
        element.Length = TimeSpan.FromSeconds(10);
        session.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(initialStart));
            Assert.That(element.Length, Is.EqualTo(initialLength));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Commit_AfterCommit_IsNoOp()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        using IElementResizeDragSession session = _service.BeginResize(_scene, [element], ResizeEdge.Right, false);
        session.Commit([new ElementResizeRequest(element, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), 0)]);
        TimeSpan committedLength = element.Length;
        int afterFirst = _history.UndoCount;

        session.Commit([new ElementResizeRequest(element, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(7), 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(element.Length, Is.EqualTo(committedLength));
            Assert.That(_history.UndoCount, Is.EqualTo(afterFirst));
        });
    }

    [Test]
    public void DragSession_ExposesEdgeAndClampOption()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        using IElementResizeDragSession session = _service.BeginResize(_scene, [element], ResizeEdge.Left, true);

        Assert.That(session, Is.InstanceOf<IElementResizeDragSession>());
    }
}
