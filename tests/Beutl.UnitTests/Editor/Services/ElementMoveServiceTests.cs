using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementMoveServiceTests
{
    private string _basePath = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private ElementDuplicateService _duplicateService = null!;
    private ElementMoveService _service = null!;

    [SetUp]
    public void Setup()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_move_{Guid.NewGuid():N}");
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
        _duplicateService = new ElementDuplicateService(_history);
        _service = new ElementMoveService(_history, _duplicateService);
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
        Assert.Throws<ArgumentNullException>(() => new ElementMoveService(null!, _duplicateService));
    }

    [Test]
    public void Constructor_NullDuplicateService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementMoveService(_history, null!));
    }

    [Test]
    public void Commit_ZeroDelta_ReturnsNone_NoCommit()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        using IElementMoveDragSession session = _service.BeginMove(_scene, [element], element, duplicateMode: false);
        ElementMoveOutcome outcome = session.Commit(TimeSpan.Zero, 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Commit_MoveDelta_AppliesAndCommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;
        TimeSpan originalStart = element.Start;

        using IElementMoveDragSession session = _service.BeginMove(_scene, [element], element, duplicateMode: false);
        ElementMoveOutcome outcome = session.Commit(TimeSpan.FromSeconds(2), 1);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.Moved));
            Assert.That(element.Start, Is.EqualTo(originalStart + TimeSpan.FromSeconds(2)));
            Assert.That(element.ZIndex, Is.EqualTo(1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Commit_MultipleElements_OneHistoryEntry()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), 1);
        int before = _history.UndoCount;

        using IElementMoveDragSession session = _service.BeginMove(_scene, [e1, e2], e1, duplicateMode: false);
        ElementMoveOutcome outcome = session.Commit(TimeSpan.FromSeconds(1), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.Moved));
            Assert.That(e1.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(e2.Start, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Commit_DuplicateMode_OverlappingSources_ReturnsOverlap_NoCommit()
    {
        // Two adjacent elements; duplicating with zero delta lands the copy on top of self.
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        using IElementMoveDragSession session = _service.BeginMove(_scene, [element], element, duplicateMode: true);
        ElementMoveOutcome outcome = session.Commit(TimeSpan.Zero, 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.DuplicateOverlapsSource));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Commit_AfterCommit_IsNoOp()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        using IElementMoveDragSession session = _service.BeginMove(_scene, [element], element, duplicateMode: false);
        session.Commit(TimeSpan.FromSeconds(2), 0);
        int afterFirst = _history.UndoCount;
        TimeSpan committedStart = element.Start;

        ElementMoveOutcome outcome = session.Commit(TimeSpan.FromSeconds(5), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(element.Start, Is.EqualTo(committedStart));
            Assert.That(_history.UndoCount, Is.EqualTo(afterFirst));
        });
    }

    [Test]
    public void Commit_EmptyElements_ReturnsNone()
    {
        Element placeholder = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        using IElementMoveDragSession session = _service.BeginMove(_scene, [], placeholder, duplicateMode: false);
        ElementMoveOutcome outcome = session.Commit(TimeSpan.FromSeconds(2), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }
}
