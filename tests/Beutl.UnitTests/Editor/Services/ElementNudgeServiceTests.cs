using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementNudgeServiceTests
{
    private string _basePath = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private ElementNudgeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_nudge_{Guid.NewGuid():N}");
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
        _service = new ElementNudgeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
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
        Assert.Throws<ArgumentNullException>(() => new ElementNudgeService(null!));
    }

    [Test]
    public void Nudge_ShiftsAndFlushCommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        TimeSpan originalStart = element.Start;
        int before = _history.UndoCount;

        _service.Nudge(_scene, [element], 1);
        _service.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.GreaterThan(originalStart));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Nudge_MultipleBeforeFlush_CommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Nudge(_scene, [element], 1);
        _service.Nudge(_scene, [element], 1);
        _service.Nudge(_scene, [element], 1);
        _service.Flush();

        Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
    }

    [Test]
    public void Nudge_ZeroFrames_NoChange()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        TimeSpan originalStart = element.Start;
        int before = _history.UndoCount;

        _service.Nudge(_scene, [element], 0);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(originalStart));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Nudge_PullsAnchorOntoGrid_BeforeShifting()
    {
        // Off-grid start (1.555s): first nudge rounds to grid (1533ms @ 30fps) then adds 1 frame.
        Element element = AddElement(TimeSpan.FromMilliseconds(1555), TimeSpan.FromSeconds(2));
        TimeSpan originalStart = element.Start;

        _service.Nudge(_scene, [element], 1);

        Assert.That(element.Start, Is.Not.EqualTo(originalStart));
    }

    [Test]
    public void Nudge_NegativePastZero_DoesNothing()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Nudge(_scene, [element], -5);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.Zero));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Flush_WhenNothingPending_IsNoOp()
    {
        int before = _history.UndoCount;

        _service.Flush();

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void Dispose_FlushesPendingCommit()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Nudge(_scene, [element], 1);
        _service.Dispose();

        Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
    }
}
