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
        _harness = new SceneHistoryHarness("beutl_move", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(30));
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

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
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
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.Move(_scene, [element], TimeSpan.Zero, 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Move_NonZeroDelta_AppliesAndCommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;
        TimeSpan originalStart = element.Start;

        ElementMoveOutcome outcome = _service.Move(_scene, [element], TimeSpan.FromSeconds(2), 1);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.Moved));
            Assert.That(element.Start, Is.EqualTo(originalStart + TimeSpan.FromSeconds(2)));
            Assert.That(element.ZIndex, Is.EqualTo(1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Move_MultipleElements_OneHistoryEntry()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), 1);
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.Move(_scene, [e1, e2], TimeSpan.FromSeconds(1), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.Moved));
            Assert.That(e1.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(e2.Start, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void DuplicateOrMove_OverlappingSources_ReturnsOverlap_NoCommit()
    {
        // Duplicating with zero delta lands the copy on top of self.
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.DuplicateOrMove(_scene, [element], TimeSpan.Zero, 0);

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

        ElementMoveOutcome outcome = _service.Move(_scene, [], TimeSpan.FromSeconds(2), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Move_NullArguments_Throw()
    {
        Element placeholder = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _service.Move(null!, [placeholder], TimeSpan.Zero, 0));
            Assert.Throws<ArgumentNullException>(() => _service.Move(_scene, null!, TimeSpan.Zero, 0));
            Assert.Throws<ArgumentNullException>(() => _service.DuplicateOrMove(null!, [placeholder], TimeSpan.Zero, 0));
            Assert.Throws<ArgumentNullException>(() => _service.DuplicateOrMove(_scene, null!, TimeSpan.Zero, 0));
        });
    }

    [Test]
    public void Move_OntoLockedDestinationLayer_IsRefused()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 0);
        _scene.Layers.Add(new TimelineLayer { ZIndex = 2, IsLocked = true });
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.Move(_scene, [element], TimeSpan.Zero, 2);

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
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 0);
        _scene.Layers.Add(new TimelineLayer { ZIndex = 2, IsLocked = true });
        int beforeChildren = _scene.Children.Count;
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = _service.DuplicateOrMove(_scene, [element], TimeSpan.Zero, 2);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.None));
            Assert.That(_scene.Children.Count, Is.EqualTo(beforeChildren));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }
}
