using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class SceneTimeRangeServiceTests
{
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private SceneTimeRangeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _scene = new Scene(100, 100, string.Empty)
        {
            Start = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(5),
        };
        var sequence = new OperationSequenceGenerator();
        _history = new HistoryManager(_scene, sequence);
        _observer = new CoreObjectOperationObserver(null, _scene, sequence);
        _history.Subscribe(_observer);
        _service = new SceneTimeRangeService(_history);
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
        Assert.Throws<ArgumentNullException>(() => new SceneTimeRangeService(null!));
    }

    [Test]
    public void SetStart_NegativeValue_ClampsToZero()
    {
        _service.SetStart(_scene, TimeSpan.FromSeconds(-3));

        Assert.That(_scene.Start, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void SetStart_KeepsSceneEnd_WhenInsideRange()
    {
        TimeSpan originalEnd = _scene.Start + _scene.Duration;

        _service.SetStart(_scene, TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_scene.Start + _scene.Duration, Is.EqualTo(originalEnd));
        });
    }

    [Test]
    public void SetStart_CommitsExactlyOneHistoryEntry()
    {
        int before = _history.UndoCount;

        _service.SetStart(_scene, TimeSpan.FromSeconds(2));

        Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
    }

    [Test]
    public void SetEnd_BeforeSceneStart_ShiftsSceneBack()
    {
        // Scene Start=1s, Duration=5s => End=6s. Setting End to 0.5s should pull start back.
        _service.SetEnd(_scene, TimeSpan.FromMilliseconds(500));

        Assert.That(_scene.Start, Is.LessThan(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void SetEnd_ExtendsDuration()
    {
        _service.SetEnd(_scene, TimeSpan.FromSeconds(10));

        Assert.That(_scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(9)));
    }

    [Test]
    public void SetEnd_CommitsExactlyOneHistoryEntry()
    {
        int before = _history.UndoCount;

        _service.SetEnd(_scene, TimeSpan.FromSeconds(10));

        Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
    }

    [Test]
    public void DragEnd_UpdateThenCommit_RecordsOneEntry()
    {
        int before = _history.UndoCount;
        using ISceneTimeRangeDragSession session = _service.BeginDragEnd(_scene);

        session.Update(TimeSpan.FromSeconds(8));
        session.Update(TimeSpan.FromSeconds(10));
        session.Commit();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(9)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void DragEnd_Cancel_RestoresInitialDuration()
    {
        TimeSpan initialDuration = _scene.Duration;
        TimeSpan initialStart = _scene.Start;
        int before = _history.UndoCount;
        using ISceneTimeRangeDragSession session = _service.BeginDragEnd(_scene);

        session.Update(TimeSpan.FromSeconds(20));
        session.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Duration, Is.EqualTo(initialDuration));
            Assert.That(_scene.Start, Is.EqualTo(initialStart));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void DragStart_UpdateThenCommit_PreservesSceneEnd()
    {
        TimeSpan initialEnd = _scene.Start + _scene.Duration;
        int before = _history.UndoCount;
        using ISceneTimeRangeDragSession session = _service.BeginDragStart(_scene);

        session.Update(TimeSpan.FromSeconds(3));
        session.Commit();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(_scene.Start + _scene.Duration, Is.EqualTo(initialEnd));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void DragStart_Cancel_RestoresInitialStartAndDuration()
    {
        TimeSpan initialStart = _scene.Start;
        TimeSpan initialDuration = _scene.Duration;
        int before = _history.UndoCount;
        using ISceneTimeRangeDragSession session = _service.BeginDragStart(_scene);

        session.Update(TimeSpan.FromSeconds(4));
        session.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Start, Is.EqualTo(initialStart));
            Assert.That(_scene.Duration, Is.EqualTo(initialDuration));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void DragSession_Update_AfterCommit_IsNoOp()
    {
        using ISceneTimeRangeDragSession session = _service.BeginDragEnd(_scene);
        session.Update(TimeSpan.FromSeconds(8));
        session.Commit();
        TimeSpan committedDuration = _scene.Duration;

        session.Update(TimeSpan.FromSeconds(20));

        Assert.That(_scene.Duration, Is.EqualTo(committedDuration));
    }
}
