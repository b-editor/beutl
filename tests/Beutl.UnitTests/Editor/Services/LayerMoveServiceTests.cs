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

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LayerMoveService(null!));
    }

    [Test]
    public void PlanMove_SameLayer_ReturnsNoop()
    {
        Element element = AddElement(2);

        LayerMovePlan plan = _service.PlanMove(_scene, 2, 2, [element]);

        Assert.That(plan.IsNoop, Is.True);
    }

    [Test]
    public void PlanMove_DownToHigherLayer_IncludesElementsBetween()
    {
        Element l0 = AddElement(0);
        Element l1 = AddElement(1);
        Element l2 = AddElement(2);
        Element l3 = AddElement(3);

        LayerMovePlan plan = _service.PlanMove(_scene, 1, 3, [l1]);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ShiftedElements, Does.Contain(l2));
            Assert.That(plan.ShiftedElements, Does.Contain(l3));
            Assert.That(plan.ShiftedElements, Does.Not.Contain(l0));
            Assert.That(plan.ShiftedElements, Does.Not.Contain(l1));
        });
    }

    [Test]
    public void PlanMove_UpToLowerLayer_IncludesElementsBetween()
    {
        Element l0 = AddElement(0);
        Element l1 = AddElement(1);
        Element l2 = AddElement(2);
        Element l3 = AddElement(3);

        LayerMovePlan plan = _service.PlanMove(_scene, 3, 1, [l3]);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ShiftedElements, Does.Contain(l1));
            Assert.That(plan.ShiftedElements, Does.Contain(l2));
            Assert.That(plan.ShiftedElements, Does.Not.Contain(l0));
            Assert.That(plan.ShiftedElements, Does.Not.Contain(l3));
        });
    }

    [Test]
    public void CommitMove_AppliesZIndexShiftsAndCommits()
    {
        Element l0 = AddElement(0);
        Element l1 = AddElement(1);
        Element l2 = AddElement(2);
        int before = _history.UndoCount;

        LayerMovePlan plan = _service.PlanMove(_scene, 0, 2, [l0]);
        _service.CommitMove(plan);

        Assert.Multiple(() =>
        {
            Assert.That(l0.ZIndex, Is.EqualTo(2));
            Assert.That(l1.ZIndex, Is.EqualTo(0));
            Assert.That(l2.ZIndex, Is.EqualTo(1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void CommitMove_Noop_DoesNotCommit()
    {
        int before = _history.UndoCount;

        _service.CommitMove(LayerMovePlan.Noop);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }
}
