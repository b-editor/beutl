using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementMoveServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementDuplicateService _duplicateService = null!;
    private ElementMoveService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_move", start: MediaTime.Zero, duration: MediaTime.FromSeconds(30));
        _scene = _harness.Scene;
        _history = _harness.History;
        _duplicateService = new ElementDuplicateService(_history);
        _service = new ElementMoveService(_history, _duplicateService);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(MediaTime start, MediaTime length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

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
    public void Move_ZeroDelta_ReturnsNone_NoCommit()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.Move(_scene, [element], MediaTime.Zero, 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Move_NonZeroDelta_AppliesAndCommitsOnce()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;
        MediaTime originalStart = element.Start;

        ElementMoveOutcome outcome = _service.Move(_scene, [element], MediaTime.FromSeconds(2), 1);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.Moved));
            Assert.That(element.Start, Is.EqualTo(originalStart + MediaTime.FromSeconds(2)));
            Assert.That(element.ZIndex, Is.EqualTo(1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Move_MultipleElements_OneHistoryEntry()
    {
        Element e1 = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), 0);
        Element e2 = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2), 1);
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.Move(_scene, [e1, e2], MediaTime.FromSeconds(1), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.Moved));
            Assert.That(e1.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(e2.Start, Is.EqualTo(MediaTime.FromSeconds(6)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void DuplicateOrMove_OverlappingSources_ReturnsOverlap_NoCommit()
    {
        // Duplicating with zero delta lands the copy on top of self.
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.DuplicateOrMove(_scene, [element], MediaTime.Zero, 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.DuplicateOverlapsSource));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Move_EmptyElements_ReturnsNone()
    {
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.Move(_scene, [], MediaTime.FromSeconds(2), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Move_NullArguments_Throw()
    {
        Element placeholder = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _service.Move(null!, [placeholder], MediaTime.Zero, 0));
            Assert.Throws<ArgumentNullException>(() => _service.Move(_scene, null!, MediaTime.Zero, 0));
            Assert.Throws<ArgumentNullException>(() => _service.DuplicateOrMove(null!, [placeholder], MediaTime.Zero, 0));
            Assert.Throws<ArgumentNullException>(() => _service.DuplicateOrMove(_scene, null!, MediaTime.Zero, 0));
        });
    }

    [Test]
    public void Move_OntoLockedDestinationLayer_IsRefused()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        _scene.Layers.Add(new TimelineLayer { ZIndex = 2, IsLocked = true });
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.Move(_scene, [element], MediaTime.Zero, 2);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(element.ZIndex, Is.EqualTo(0));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void DuplicateOrMove_OntoLockedDestinationLayer_IsRefused()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        _scene.Layers.Add(new TimelineLayer { ZIndex = 2, IsLocked = true });
        int beforeChildren = _scene.Children.Count;
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.DuplicateOrMove(_scene, [element], MediaTime.Zero, 2);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(_scene.Children.Count, Is.EqualTo(beforeChildren));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Move_LockedSourceElement_IsFiltered()
    {
        Element locked = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        locked.IsLocked = true;
        Element free = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));

        ElementMoveOutcome outcome = _service.Move(_scene, [locked, free], MediaTime.FromSeconds(1), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.Moved));
            Assert.That(locked.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(free.Start, Is.EqualTo(MediaTime.FromSeconds(6)));
        });
    }

    [Test]
    public void Move_AllSourcesLocked_ReturnsNone_NoCommit()
    {
        Element locked = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        locked.IsLocked = true;
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.Move(_scene, [locked], MediaTime.FromSeconds(1), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(locked.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void DuplicateOrMove_AllSourcesLocked_ReturnsNone_NoCommit()
    {
        Element locked = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        locked.IsLocked = true;
        int beforeChildren = _scene.Children.Count;
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.DuplicateOrMove(_scene, [locked], MediaTime.FromSeconds(10), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(_scene.Children.Count, Is.EqualTo(beforeChildren));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void DuplicateOrMove_LockedSource_IsExcludedFromDuplicate()
    {
        Element locked = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        locked.IsLocked = true;
        Element free = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        int beforeChildren = _scene.Children.Count;

        ElementMoveOutcome outcome = _service.DuplicateOrMove(_scene, [locked, free], MediaTime.FromSeconds(10), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.Duplicated));
            Assert.That(_scene.Children.Count, Is.EqualTo(beforeChildren + 1));
            Assert.That(locked.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }
}
