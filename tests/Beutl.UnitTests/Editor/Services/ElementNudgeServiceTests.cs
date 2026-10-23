using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementNudgeServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementNudgeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_nudge", start: MediaTime.Zero, duration: MediaTime.FromSeconds(60));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementNudgeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
        _harness.Dispose();
    }

    // Committed, so the counts below see only the nudge's own entry.
    private Element AddElement(MediaTime start, MediaTime length, int zIndex = 0)
    {
        Element element = _harness.AddElement(start, length, zIndex);
        _history.Commit();
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
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        MediaTime originalStart = element.Start.Value;
        int before = _history.UndoCount;

        _service.Nudge(_scene, [element], 1);
        Assert.That(_service.IsPending, Is.True);
        _service.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(element.Start.Value, Is.GreaterThan(originalStart));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
            Assert.That(_service.IsPending, Is.False);
        });
    }

    [Test]
    public void Nudge_MultipleBeforeFlush_CommitsOnce()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
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
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        MediaTime originalStart = element.Start.Value;
        int before = _history.UndoCount;

        _service.Nudge(_scene, [element], 0);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start.Value, Is.EqualTo(originalStart));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Nudge_PullsAnchorOntoGrid_BeforeShifting()
    {
        // Off-grid start (1.555s): the first nudge rounds it to the 30fps grid, then adds 1 frame.
        Element element = AddElement(MediaTime.FromSeconds(1.555), MediaTime.FromSeconds(2));

        _service.Nudge(_scene, [element], 1);

        Assert.That(
            element.Start.Value,
            Is.EqualTo(MediaTime.FromSeconds(1.555).RoundToFrame(new Rational(30), MidpointRounding.AwayFromZero)
                + MediaTime.FromFrames(1, new Rational(30))));
    }

    [Test]
    public void Nudge_NegativePastZero_DoesNothing()
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Nudge(_scene, [element], -5);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start.Value, Is.EqualTo(MediaTime.Zero));
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
    public void Nudge_KeepsItsOwnEntry_ApartFromAnEditMadeDuringTheWindow()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element other = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2), zIndex: 1);
        int before = _history.UndoCount;

        _service.Nudge(_scene, [element], 1);
        other.IsEnabled.Value = false;
        _service.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
            Assert.That(_history.PeekUndo()!.Name, Is.EqualTo("CommandNames.MoveElement"));
            Assert.That(_history.HasPendingOperations, Is.True, "The other edit is not in the nudge's entry.");
        });
    }

    [Test]
    public void Nudge_WaitsOutAnotherOpenEditSession()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        MediaTime originalStart = element.Start.Value;
        using EditSession session = _history.BeginEdit("Drag");

        _service.Nudge(_scene, [element], 1);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start.Value, Is.EqualTo(originalStart));
            Assert.That(_service.IsPending, Is.False);
        });
    }

    [Test]
    public void Nudge_ThenAMoveCommittedInTheWindow_UndoesInOrder()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        MediaTime original = element.Start.Value;

        _service.Nudge(_scene, [element], 1);
        MediaTime nudged = element.Start.Value;
        element.Start.Value = MediaTime.FromSeconds(5);
        _history.Commit("Move");
        _service.Flush();

        Assert.That(_history.Undo(), Is.True);
        Assert.That(element.Start.Value, Is.EqualTo(nudged));
        Assert.That(_history.Undo(), Is.True);
        Assert.That(element.Start.Value, Is.EqualTo(original));
    }

    [Test]
    public void Undo_DuringTheNudgeWindow_RevertsTheNudgeBeforeAQueuedEditRuns()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element other = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2), zIndex: 1);
        MediaTime original = element.Start.Value;
        using IDisposable flush = _history.BeforeMutation.Subscribe(_ => _service.Flush());

        _service.Nudge(_scene, [element], 1);
        Task queued = _history.RunAfterEditAsync(() => _history.ExecuteInTransaction(() => other.IsEnabled.Value = false, "Agent"));
        Assert.That(_history.Undo(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start.Value, Is.EqualTo(original));
            Assert.That(queued.IsCompletedSuccessfully, Is.True);
            Assert.That(other.IsEnabled.Value, Is.False);
            Assert.That(_history.PeekUndo()!.DisplayName, Is.EqualTo("Agent"));
        });
    }

    [Test]
    public void Nudge_ThatThrows_StillClosesItsSession()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        static void Throw(object? sender, PropertyValueChangedEventArgs<MediaTime> e)
            => throw new InvalidOperationException("move failed");
        element.Start.ValueChanged += Throw;

        Assert.Throws<InvalidOperationException>(() => _service.Nudge(_scene, [element], 1));
        element.Start.ValueChanged -= Throw;
        _service.Flush();

        Assert.That(_history.IsEditing, Is.False);
    }

    [Test]
    public void Dispose_FlushesPendingCommit()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Nudge(_scene, [element], 1);
        _service.Dispose();

        Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
    }
}
