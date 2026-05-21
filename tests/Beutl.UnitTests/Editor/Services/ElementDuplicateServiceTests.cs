using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementDuplicateServiceTests
{
    private string _basePath = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private ElementDuplicateService _service = null!;

    [SetUp]
    public void Setup()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_dup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_basePath);

        _scene = new Scene(100, 100, string.Empty)
        {
            Uri = new Uri(Path.Combine(_basePath, "test.scene")),
            Start = TimeSpan.Zero,
            Duration = TimeSpan.FromSeconds(60),
        };
        var sequence = new OperationSequenceGenerator();
        _history = new HistoryManager(_scene, sequence);
        _observer = new CoreObjectOperationObserver(null, _scene, sequence);
        _history.Subscribe(_observer);
        _service = new ElementDuplicateService(_history);
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
}
