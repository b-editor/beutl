using Beutl.Audio;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementSilenceSplitServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementSilenceSplitService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_life", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(60));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementSilenceSplitService(_history, new ElementStructureService(_history));
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    [Test]
    public void Constructor_NullArgs_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => new ElementSilenceSplitService(null!, new ElementStructureService(_history)));
            Assert.Throws<ArgumentNullException>(() => new ElementSilenceSplitService(_history, null!));
        });
    }

    [Test]
    public void SplitBySilence_SplitOnly_SplitsAtEachBoundaryAndCommitsOnce()
    {
        // Element [0, 30s]; silence regions [5,8] and [15,18] -> 4 boundaries -> 4 splits -> 5 pieces.
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(30));
        var regions = new[]
        {
            new SilenceRegion(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8)),
            new SilenceRegion(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(18)),
        };
        int before = _history.UndoCount;
        int childrenBefore = _scene.Children.Count;

        SilenceSplitOutcome outcome = _service.SplitBySilence(_scene, [element], regions, SilenceSplitMode.SplitOnly);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.SplitCount, Is.EqualTo(4));
            Assert.That(outcome.DeletedCount, Is.EqualTo(0));
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 4));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void SplitBySilence_SplitAndDelete_RemovesSilencePiecesAndCommitsOnce()
    {
        // Element [0, 30s]; regions [5,8] and [15,18]. After splitting, the two pieces that cover
        // the silence regions are deleted -> 4 splits, 2 deleted, net +2 children.
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(30));
        var regions = new[]
        {
            new SilenceRegion(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8)),
            new SilenceRegion(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(18)),
        };
        int before = _history.UndoCount;
        int childrenBefore = _scene.Children.Count;

        SilenceSplitOutcome outcome = _service.SplitBySilence(_scene, [element], regions, SilenceSplitMode.SplitAndDeleteSilence);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.SplitCount, Is.EqualTo(4));
            Assert.That(outcome.DeletedCount, Is.EqualTo(2));
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 4 - 2));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void SplitBySilence_RegionOverlappingElementStart_SplitsOnlyAtInnerBoundary()
    {
        // Element [5, 20s]; region [3, 8]. Only the boundary at 8s falls strictly inside, so one
        // split produces [5,8] (silence) and [8,20]. SplitAndDelete removes the [5,8] piece.
        Element element = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        var regions = new[]
        {
            new SilenceRegion(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8)),
        };
        int before = _history.UndoCount;

        SilenceSplitOutcome outcome = _service.SplitBySilence(_scene, [element], regions, SilenceSplitMode.SplitAndDeleteSilence);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.SplitCount, Is.EqualTo(1));
            Assert.That(outcome.DeletedCount, Is.EqualTo(1));
            Assert.That(_scene.Children, Has.Count.EqualTo(1));
            Element survivor = _scene.Children[0];
            Assert.That(survivor.Start, Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(survivor.Length, Is.EqualTo(TimeSpan.FromSeconds(12)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void SplitBySilence_WholeElementSilent_SplitAndDelete_DeletesElement()
    {
        // Element [0, 5s]; region [0, 5s] covers it entirely. No boundary is strictly inside, so
        // no split happens, but the whole element is a silence piece and is deleted in one commit.
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var regions = new[]
        {
            new SilenceRegion(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        int before = _history.UndoCount;

        SilenceSplitOutcome outcome = _service.SplitBySilence(_scene, [element], regions, SilenceSplitMode.SplitAndDeleteSilence);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.SplitCount, Is.EqualTo(0));
            Assert.That(outcome.DeletedCount, Is.EqualTo(1));
            Assert.That(_scene.Children, Is.Empty);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void SplitBySilence_RegionsOutsideElement_DoesNotCommit()
    {
        // Element [0, 10s]; region [100, 103] does not overlap -> no boundaries, nothing to delete.
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var regions = new[]
        {
            new SilenceRegion(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(103)),
        };
        int before = _history.UndoCount;

        SilenceSplitOutcome outcome = _service.SplitBySilence(_scene, [element], regions, SilenceSplitMode.SplitAndDeleteSilence);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(SilenceSplitOutcome.None));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void SplitBySilence_EmptyRegions_DoesNotCommit()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        int before = _history.UndoCount;

        SilenceSplitOutcome outcome = _service.SplitBySilence(_scene, [element], [], SilenceSplitMode.SplitOnly);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(SilenceSplitOutcome.None));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void SplitBySilence_NullArgs_Throw()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _service.SplitBySilence(null!, [element], [], SilenceSplitMode.SplitOnly));
            Assert.Throws<ArgumentNullException>(() => _service.SplitBySilence(_scene, null!, [], SilenceSplitMode.SplitOnly));
            Assert.Throws<ArgumentNullException>(() => _service.SplitBySilence(_scene, [element], null!, SilenceSplitMode.SplitOnly));
        });
    }
}
