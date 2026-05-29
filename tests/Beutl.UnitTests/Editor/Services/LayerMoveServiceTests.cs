using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class LayerMoveServiceTests
{
    private string _basePath = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private LayerMoveService _service = null!;

    [SetUp]
    public void Setup()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_layer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_basePath);

        _scene = new Scene(100, 100, string.Empty)
        {
            Uri = new Uri(Path.Combine(_basePath, "test.scene")),
        };
        var sequence = new OperationSequenceGenerator();
        _history = new HistoryManager(_scene, sequence);
        _observer = new CoreObjectOperationObserver(null, _scene, sequence);
        _history.Subscribe(_observer);
        _service = new LayerMoveService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _observer.Dispose();
        _history.Dispose();
        if (Directory.Exists(_basePath)) Directory.Delete(_basePath, recursive: true);
    }

    private Element AddElement(int zIndex)
    {
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(zIndex),
            Length = TimeSpan.FromSeconds(1),
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(_basePath, $"{Guid.NewGuid():N}.layer")),
        };
        _scene.Children.Add(element);
        return element;
    }

    private TimelineLayer AddLayer(int zIndex)
    {
        var layer = new TimelineLayer { ZIndex = zIndex };
        _scene.Layers.Add(layer);
        return layer;
    }

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LayerMoveService(null!));
    }

    [Test]
    public void ApplyMove_SameLayer_ReturnsNoop_NoCommit()
    {
        Element element = AddElement(2);
        int before = _history.UndoCount;

        LayerMovePlan plan = _service.ApplyMove(_scene, 2, 2, [element]);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsNoop, Is.True);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void ApplyMove_DownToHigherLayer_AppliesShiftAndCommits()
    {
        Element l0 = AddElement(0);
        Element l1 = AddElement(1);
        Element l2 = AddElement(2);
        Element l3 = AddElement(3);
        int before = _history.UndoCount;

        LayerMovePlan plan = _service.ApplyMove(_scene, 1, 3, [l1]);

        Assert.Multiple(() =>
        {
            // l1 moves to layer 3; l2 and l3 shift down by 1.
            Assert.That(l0.ZIndex, Is.EqualTo(0));
            Assert.That(l1.ZIndex, Is.EqualTo(3));
            Assert.That(l2.ZIndex, Is.EqualTo(1));
            Assert.That(l3.ZIndex, Is.EqualTo(2));
            Assert.That(plan.ShiftedElements, Does.Contain(l2));
            Assert.That(plan.ShiftedElements, Does.Contain(l3));
            Assert.That(plan.ShiftedElements, Does.Not.Contain(l0));
            Assert.That(plan.ShiftedElements, Does.Not.Contain(l1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void ApplyMove_UpToLowerLayer_AppliesShiftAndCommits()
    {
        Element l0 = AddElement(0);
        Element l1 = AddElement(1);
        Element l2 = AddElement(2);
        Element l3 = AddElement(3);
        int before = _history.UndoCount;

        LayerMovePlan plan = _service.ApplyMove(_scene, 3, 1, [l3]);

        Assert.Multiple(() =>
        {
            // l3 moves to layer 1; l1 and l2 shift up by 1.
            Assert.That(l0.ZIndex, Is.EqualTo(0));
            Assert.That(l1.ZIndex, Is.EqualTo(2));
            Assert.That(l2.ZIndex, Is.EqualTo(3));
            Assert.That(l3.ZIndex, Is.EqualTo(1));
            Assert.That(plan.ShiftedElements, Does.Contain(l1));
            Assert.That(plan.ShiftedElements, Does.Contain(l2));
            Assert.That(plan.ShiftedElements, Does.Not.Contain(l0));
            Assert.That(plan.ShiftedElements, Does.Not.Contain(l3));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void ApplyMove_AfterUndo_RestoresOriginalZIndexes()
    {
        Element l0 = AddElement(0);
        Element l1 = AddElement(1);
        Element l2 = AddElement(2);

        _service.ApplyMove(_scene, 0, 2, [l0]);
        _history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(l0.ZIndex, Is.EqualTo(0));
            Assert.That(l1.ZIndex, Is.EqualTo(1));
            Assert.That(l2.ZIndex, Is.EqualTo(2));
        });
    }

    [Test]
    public void ApplyMove_ShiftsTimelineLayerModels_InSingleCommit()
    {
        Element e1 = AddElement(1);
        AddElement(2);
        AddElement(3);
        TimelineLayer layer1 = AddLayer(1);
        TimelineLayer layer2 = AddLayer(2);
        TimelineLayer layer3 = AddLayer(3);
        int before = _history.UndoCount;

        _service.ApplyMove(_scene, 1, 3, [e1]);

        Assert.Multiple(() =>
        {
            // The header models track their elements: layer1 -> 3, layer2/3 shift down.
            Assert.That(layer1.ZIndex, Is.EqualTo(3));
            Assert.That(layer2.ZIndex, Is.EqualTo(1));
            Assert.That(layer3.ZIndex, Is.EqualTo(2));
            // TimelineLayer writes share the Element transaction: still one entry.
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void ApplyMove_AfterUndo_RestoresTimelineLayerZIndexes()
    {
        // Regression: TimelineLayer.ZIndex is a recorded property. If its writes
        // landed outside the committed MoveLayer transaction, this Undo would
        // revert the Element.ZIndex but leave the header models desynced.
        Element e1 = AddElement(1);
        AddElement(2);
        AddElement(3);
        TimelineLayer layer1 = AddLayer(1);
        TimelineLayer layer2 = AddLayer(2);
        TimelineLayer layer3 = AddLayer(3);

        _service.ApplyMove(_scene, 1, 3, [e1]);
        _history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(layer1.ZIndex, Is.EqualTo(1));
            Assert.That(layer2.ZIndex, Is.EqualTo(2));
            Assert.That(layer3.ZIndex, Is.EqualTo(3));
            Assert.That(e1.ZIndex, Is.EqualTo(1));
        });
    }
}
