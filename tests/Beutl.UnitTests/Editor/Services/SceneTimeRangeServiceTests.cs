using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class SceneTimeRangeServiceTests
{
    private HistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private SceneTimeRangeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _scene = new Scene(100, 100, string.Empty)
        {
            Start = MediaTime.FromSeconds(1),
            Duration = MediaTime.FromSeconds(5),
        };
        _harness = new HistoryHarness(_scene);
        _history = _harness.History;
        _service = new SceneTimeRangeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SceneTimeRangeService(null!));
    }

    [Test]
    public void SetStart_NegativeValue_ClampsToZero()
    {
        _service.SetStart(_scene, MediaTime.FromSeconds(-3));

        Assert.That(_scene.Start, Is.EqualTo(MediaTime.Zero));
    }

    [Test]
    public void SetStart_KeepsSceneEnd_WhenInsideRange()
    {
        MediaTime originalEnd = _scene.Start + _scene.Duration;

        _service.SetStart(_scene, MediaTime.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_scene.Start + _scene.Duration, Is.EqualTo(originalEnd));
        });
    }

    [Test]
    public void SetStart_CommitsExactlyOneHistoryEntry()
    {
        int before = _history.UndoCount;

        _service.SetStart(_scene, MediaTime.FromSeconds(2));

        Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
    }

    [Test]
    public void SetEnd_BeforeSceneStart_ShiftsSceneBack()
    {
        // Scene Start=1s, Duration=5s => End=6s. Setting End to 0.5s should pull start back.
        _service.SetEnd(_scene, MediaTime.FromSeconds(0.5));

        Assert.That(_scene.Start, Is.LessThan(MediaTime.FromSeconds(1)));
    }

    [Test]
    public void SetEnd_ExtendsDuration()
    {
        _service.SetEnd(_scene, MediaTime.FromSeconds(10));

        Assert.That(_scene.Duration, Is.EqualTo(MediaTime.FromSeconds(9)));
    }

    [Test]
    public void SetEnd_CommitsExactlyOneHistoryEntry()
    {
        int before = _history.UndoCount;

        _service.SetEnd(_scene, MediaTime.FromSeconds(10));

        Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
    }

    [Test]
    public void UpdateEndDrag_Alone_DoesNotCommit()
    {
        int before = _history.UndoCount;

        _service.UpdateEndDrag(_scene, MediaTime.FromSeconds(10));

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void UpdateEndDrag_InACompletedEditSession_RecordsOneEntry()
    {
        int before = _history.UndoCount;

        EditSession session = _history.BeginEdit(CommandNames.ChangeSceneDuration);
        using (session.Enter())
        {
            _service.UpdateEndDrag(_scene, MediaTime.FromSeconds(8));
            _service.UpdateEndDrag(_scene, MediaTime.FromSeconds(10));
        }
        session.Complete();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Duration, Is.EqualTo(MediaTime.FromSeconds(9)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void UpdateStartDrag_PreservesInitialSceneEnd_AcrossPointerFrames()
    {
        MediaTime initialStart = _scene.Start;
        MediaTime initialDuration = _scene.Duration;
        MediaTime initialEnd = initialStart + initialDuration;

        _service.UpdateStartDrag(_scene, MediaTime.FromSeconds(2), initialStart, initialDuration);
        _service.UpdateStartDrag(_scene, MediaTime.FromSeconds(3), initialStart, initialDuration);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
            // End must stay pinned: without this, each drag frame compounds the previous one.
            Assert.That(_scene.Start + _scene.Duration, Is.EqualTo(initialEnd));
        });
    }

    [Test]
    public void UpdateStartDrag_InACancelledEditSession_RestoresInitialValues()
    {
        MediaTime initialStart = _scene.Start;
        MediaTime initialDuration = _scene.Duration;
        int before = _history.UndoCount;

        EditSession session = _history.BeginEdit(CommandNames.ChangeSceneStart);
        using (session.Enter())
        {
            _service.UpdateStartDrag(_scene, MediaTime.FromSeconds(4), initialStart, initialDuration);
        }
        session.Cancel();

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
        // Moving the end before the start keeps the start and rounds the duration up to one frame.
        MediaTime initialStart = _scene.Start;

        _service.UpdateEndDrag(_scene, MediaTime.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Start, Is.EqualTo(initialStart));
            // duration clamped to one frame (default rate = 30 fps)
            Assert.That(_scene.Duration, Is.EqualTo(MediaTime.FromSeconds(1d / 30)));
        });
    }

    [Test]
    public void UpdateStartDrag_PointerAfterInitialEnd_ClampsToOneFrameBeforeEnd()
    {
        // Moving the start past the end does not extend the end; the start stops one frame before it.
        MediaTime initialStart = _scene.Start;
        MediaTime initialDuration = _scene.Duration;
        MediaTime initialEnd = initialStart + initialDuration;
        MediaTime frame = MediaTime.FromSeconds(1d / 30);

        _service.UpdateStartDrag(_scene, MediaTime.FromSeconds(100), initialStart, initialDuration);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Start, Is.EqualTo(initialEnd - frame));
            Assert.That(_scene.Start + _scene.Duration, Is.EqualTo(initialEnd));
        });
    }
}
