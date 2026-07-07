using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementDuplicateServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementDuplicateService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_dup", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(60));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementDuplicateService(_history);
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
        Assert.Throws<ArgumentNullException>(() => new ElementDuplicateService(null!));
    }

    [Test]
    public void WouldOverlap_NoOverlap_ReturnsFalse()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        bool overlap = _service.WouldOverlap([element], TimeSpan.FromSeconds(5), 0);

        Assert.That(overlap, Is.False);
    }

    [Test]
    public void WouldOverlap_OnSource_ReturnsTrue()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        bool overlap = _service.WouldOverlap([element], TimeSpan.FromSeconds(1), 0);

        Assert.That(overlap, Is.True);
    }

    [Test]
    public void DuplicateAtPosition_NonOverlapping_CommitsOneEntry()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;
        int childrenBefore = _scene.Children.Count;

        bool success = _service.DuplicateAtPosition(_scene, [element], TimeSpan.FromSeconds(10), 0);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void DuplicateAtPosition_EmptyList_ReturnsFalse()
    {
        int before = _history.UndoCount;

        bool success = _service.DuplicateAtPosition(_scene, [], TimeSpan.FromSeconds(10), 0);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_ClickInEmptySlot_PlacesAtClick()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [element], TimeSpan.FromSeconds(15), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.ScrollToRange.Start, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(outcome.ScrollToZIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_ClickOnExisting_FindsOpenSlot()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [element], element.Start, 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            // Spiral search should not land on the existing element.
            Assert.That(outcome.ScrollToRange.Start, Is.Not.EqualTo(element.Start).Or
                .Property(nameof(outcome.ScrollToZIndex)).Not.EqualTo(element.ZIndex));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_EmptySources_ReturnsFailed()
    {
        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [], TimeSpan.FromSeconds(5), 0);

        Assert.That(outcome.Success, Is.False);
    }

    [Test]
    public void DuplicateAtClickedPosition_LockedSource_IsExcluded()
    {
        Element locked = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        locked.IsLocked = true;
        Element free = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        int childrenBefore = _scene.Children.Count;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [locked, free], TimeSpan.FromSeconds(20), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 1));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_ClickedOnLockedLayer_PlacesElsewhere()
    {
        Element source = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 0);
        _scene.Layers.Add(new TimelineLayer { ZIndex = 3, IsLocked = true });

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [source], TimeSpan.FromSeconds(20), 3);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.ScrollToZIndex, Is.Not.EqualTo(3));
            Assert.That(_scene.Children.Any(c => c != source && c.ZIndex == 3), Is.False);
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_ClipboardSourceOnLockedLayer_IsNotFiltered()
    {
        // A deserialized clipboard element is not a child of the scene, so the
        // current scene's layer locks must not apply to its source ZIndex.
        _scene.Layers.Add(new TimelineLayer { ZIndex = 2, IsLocked = true });
        var clipboardElement = new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2),
            ZIndex = 2,
            Uri = new Uri(Path.Combine(_harness.BasePath, $"{Guid.NewGuid():N}.layer")),
        };
        int childrenBefore = _scene.Children.Count;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [clipboardElement], TimeSpan.FromSeconds(20), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 1));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_AllSourcesLocked_ReturnsFailed()
    {
        Element locked = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 3);
        _scene.Layers.Add(new TimelineLayer { ZIndex = 3, IsLocked = true });
        int childrenBefore = _scene.Children.Count;
        int before = _history.UndoCount;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [locked], TimeSpan.FromSeconds(20), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.False);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }
}
