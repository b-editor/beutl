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
    public void UpdateEndDrag_Alone_DoesNotCommit()
    {
        int before = _history.UndoCount;

        _service.UpdateEndDrag(_scene, TimeSpan.FromSeconds(10));

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void UpdateEndDrag_ThenCommitEndChange_RecordsOneEntry()
    {
        int before = _history.UndoCount;

        _service.UpdateEndDrag(_scene, TimeSpan.FromSeconds(8));
        _service.UpdateEndDrag(_scene, TimeSpan.FromSeconds(10));
        _service.CommitEndChange();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(9)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void UpdateStartDrag_PreservesInitialSceneEnd_AcrossPointerFrames()
    {
        TimeSpan initialStart = _scene.Start;
        TimeSpan initialDuration = _scene.Duration;
        TimeSpan initialEnd = initialStart + initialDuration;

        _service.UpdateStartDrag(_scene, TimeSpan.FromSeconds(2), initialStart, initialDuration);
        _service.UpdateStartDrag(_scene, TimeSpan.FromSeconds(3), initialStart, initialDuration);
        _service.CommitStartChange();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            // The Start/Duration delta must always cancel back to the original
            // end — without this guard, each drag frame would compound the
            // previous frame's mutation.
            Assert.That(_scene.Start + _scene.Duration, Is.EqualTo(initialEnd));
        });
    }

    [Test]
    public void UpdateStartDrag_ThenRollback_RestoresInitialValues()
    {
        TimeSpan initialStart = _scene.Start;
        TimeSpan initialDuration = _scene.Duration;
        int before = _history.UndoCount;

        _service.UpdateStartDrag(_scene, TimeSpan.FromSeconds(4), initialStart, initialDuration);
        // The caller (View) cancels by re-driving UpdateStartDrag with the initial
        // values — there is no Cancel method.
        _service.UpdateStartDrag(_scene, initialStart, initialStart, initialDuration);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Start, Is.EqualTo(initialStart));
            Assert.That(_scene.Duration, Is.EqualTo(initialDuration));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void UpdateEndDrag_PointerBeforeStart_KeepsStartPinned()
    {
        // Dragging the end marker left past Scene.Start must not pull
        // Start backward — only clamp duration to >= 1 frame. The previous
        // implementation re-used the one-shot SetEnd logic, which would
        // jerk the entire scene's absolute time range backward as soon as
        // the pointer crossed Start (Codex review #r3278970042).
        TimeSpan initialStart = _scene.Start;

        _service.UpdateEndDrag(_scene, TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Start, Is.EqualTo(initialStart));
            // duration clamped to one frame (default rate = 30 fps)
            Assert.That(_scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(1d / 30)));
        });
    }

    [Test]
    public void UpdateStartDrag_PointerAfterInitialEnd_ClampsToOneFrameBeforeEnd()
    {
        // Dragging the start marker past the initial end must clamp it
        // to (initialEnd - 1 frame), NOT shift the end forward. The
        // one-shot SetStart path does shift the end forward, but the drag
        // loop should never re-extend the scene's absolute time range
        // (same family of regression as the end-marker case).
        TimeSpan initialStart = _scene.Start;
        TimeSpan initialDuration = _scene.Duration;
        TimeSpan initialEnd = initialStart + initialDuration;
        TimeSpan frame = TimeSpan.FromSeconds(1d / 30);

        _service.UpdateStartDrag(_scene, TimeSpan.FromSeconds(100), initialStart, initialDuration);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Start, Is.EqualTo(initialEnd - frame));
            Assert.That(_scene.Start + _scene.Duration, Is.EqualTo(initialEnd));
        });
    }
}
