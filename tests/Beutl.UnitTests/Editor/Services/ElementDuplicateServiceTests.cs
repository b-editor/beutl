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
        _harness = new SceneHistoryHarness("beutl_dup", start: MediaTime.Zero, duration: MediaTime.FromSeconds(60));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementDuplicateService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(MediaTime start, MediaTime length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    private void LockRowsAbove(int zIndex)
    {
        // Cover every row the bounded spiral can reach so the search has no free slot.
        for (int z = zIndex + 1; z <= zIndex + 512; z++)
        {
            _scene.Layers.Add(new TimelineLayer { ZIndex = { Value = z }, IsLocked = { Value = true } });
        }
    }

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementDuplicateService(null!));
    }

    [Test]
    public void DuplicateAtPosition_NonOverlapping_CommitsOneEntry()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;
        int childrenBefore = _scene.Children.Count;

        bool success = _service.DuplicateAtPosition(_scene, [element], MediaTime.FromSeconds(10), 0);

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

        bool success = _service.DuplicateAtPosition(_scene, [], MediaTime.FromSeconds(10), 0);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_ClickInEmptySlot_PlacesAtClick()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [element], MediaTime.FromSeconds(15), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.ScrollToRange.Start, Is.EqualTo(MediaTime.FromSeconds(15)));
            Assert.That(outcome.ScrollToZIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_ClickOnExisting_FindsOpenSlot()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [element], element.Start.Value, 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            // Spiral search should not land on the existing element.
            Assert.That((outcome.ScrollToRange.Start, outcome.ScrollToZIndex),
                Is.Not.EqualTo((element.Start.Value, element.ZIndex.Value)));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_KeepsDuplicatesThatOverlapOnOneLayer()
    {
        Element outgoing = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2.5));
        Element incoming = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2));

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [outgoing, incoming], MediaTime.FromSeconds(20), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(
                _scene.Children.Except([outgoing, incoming]).Select(e => (e.Start.Value, e.Length.Value, e.ZIndex.Value)),
                Is.EquivalentTo(new[]
                {
                    (MediaTime.FromSeconds(20), MediaTime.FromSeconds(2.5), 0),
                    (MediaTime.FromSeconds(22), MediaTime.FromSeconds(2), 0),
                }));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_EmptySources_ReturnsFailed()
    {
        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [], MediaTime.FromSeconds(5), 0);

        Assert.That(outcome.Success, Is.False);
    }

    [Test]
    public void DuplicateAtClickedPosition_LockedSource_IsExcluded()
    {
        Element locked = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        locked.IsLocked.Value = true;
        Element free = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        int childrenBefore = _scene.Children.Count;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [locked, free], MediaTime.FromSeconds(20), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 1));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_ClickedOnLockedLayer_PlacesElsewhere()
    {
        Element source = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        _scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 3 }, IsLocked = { Value = true } });

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [source], MediaTime.FromSeconds(20), 3);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.ScrollToZIndex, Is.Not.EqualTo(3));
            Assert.That(_scene.Children.Any(c => c != source && c.ZIndex.Value == 3), Is.False);
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_SparseSelection_IgnoresLockedGapRow()
    {
        // Sources on layers 0 and 2; the locked layer 1 sits in the gap and no
        // duplicate lands on it, so the placement must succeed.
        Element bottom = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        Element top = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 2);
        _scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 }, IsLocked = { Value = true } });
        int childrenBefore = _scene.Children.Count;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [bottom, top], MediaTime.FromSeconds(20), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 2));
            Assert.That(_scene.Children.Any(c => c != bottom && c != top && c.ZIndex.Value == 1), Is.False);
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_SparseSelection_IgnoresOverlapOnGapRow()
    {
        // Sources on layers 0 and 2; a clip sits on the gap layer 1 at the clicked
        // time. No duplicate lands on layer 1, so it must not block the placement —
        // the duplicates keep the clicked start/zIndex instead of being nudged away.
        Element bottom = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        Element top = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 2);
        AddElement(MediaTime.FromSeconds(20), MediaTime.FromSeconds(2), zIndex: 1);
        int childrenBefore = _scene.Children.Count;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [bottom, top], MediaTime.FromSeconds(20), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 2));
            Assert.That(outcome.ScrollToRange.Start, Is.EqualTo(MediaTime.FromSeconds(20)));
            Assert.That(outcome.ScrollToZIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_ClipboardSourceOnLockedLayer_IsNotFiltered()
    {
        // A deserialized clipboard element is not a child of the scene, so the
        // current scene's layer locks must not apply to its source ZIndex.
        _scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 2 }, IsLocked = { Value = true } });
        var clipboardElement = new Element
        {
            Start = { Value = MediaTime.FromSeconds(1) },
            Length = { Value = MediaTime.FromSeconds(2) },
            ZIndex = { Value = 2 },
            Uri = new Uri(Path.Combine(_harness.BasePath, $"{Guid.NewGuid():N}.layer")),
        };
        int childrenBefore = _scene.Children.Count;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [clipboardElement], MediaTime.FromSeconds(20), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 1));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_AllSourcesLocked_ReturnsFailed()
    {
        Element locked = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 3);
        _scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 3 }, IsLocked = { Value = true } });
        int childrenBefore = _scene.Children.Count;
        int before = _history.UndoCount;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [locked], MediaTime.FromSeconds(20), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.False);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_NoFreeSlot_ReturnsFailed()
    {
        Element source = AddElement(MediaTime.Zero, MediaTime.FromSeconds(60));
        LockRowsAbove(0);
        int childrenBefore = _scene.Children.Count;
        int before = _history.UndoCount;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [source], MediaTime.Zero, 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.False);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    private Element[] Copies(params Element[] originals)
        => [.. _scene.Children.Except(originals).OrderBy(element => element.ZIndex.Value)];

    [Test]
    public void DuplicateAtClickedPosition_MatteWithItsSource_NamesTheCopyOfTheSource()
    {
        Element source = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 3);
        Element target = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 4);
        target.MatteSourceKind.Value = MatteSourceKind.Layer;
        target.MatteSourceZIndex.Value = 3;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [source, target], MediaTime.FromSeconds(1), 10);

        Element[] copies = Copies(source, target);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(copies.Select(copy => copy.ZIndex.Value), Is.EqualTo(new[] { 10, 11 }));
            Assert.That(copies[1].MatteSourceKind.Value, Is.EqualTo(MatteSourceKind.Layer));
            Assert.That(copies[1].MatteSourceZIndex.Value, Is.EqualTo(10));
            Assert.That(target.MatteSourceZIndex.Value, Is.EqualTo(3), "The original keeps its source.");
        });
    }

    [Test]
    public void DuplicateAtPosition_SourceMovedOffItsLayer_MatteFollowsIt()
    {
        Element source = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 3);
        Element target = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 4);
        Element blocker = AddElement(MediaTime.Zero, MediaTime.FromSeconds(20), zIndex: 10);
        target.MatteSourceKind.Value = MatteSourceKind.Layer;
        target.MatteSourceZIndex.Value = 3;
        _history.Commit();

        bool success = _service.DuplicateAtPosition(_scene, [source, target], MediaTime.FromSeconds(1), 10);

        Element[] copies = Copies(source, target, blocker);
        Element targetCopy = copies.Single(copy => copy.MatteSourceKind.Value == MatteSourceKind.Layer);
        Element sourceCopy = copies.Single(copy => copy != targetCopy);
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(sourceCopy.ZIndex.Value, Is.Not.EqualTo(10), "The blocker holds the layer the copy was aimed at.");
            Assert.That(targetCopy.MatteSourceZIndex.Value, Is.EqualTo(sourceCopy.ZIndex.Value));
        });

        _history.Undo();
        Assert.That(_scene.Children, Is.EquivalentTo(new[] { source, target, blocker }));

        _history.Redo();
        Assert.That(targetCopy.MatteSourceZIndex.Value, Is.EqualTo(sourceCopy.ZIndex.Value), "After a redo.");
    }

    [Test]
    public void DuplicateAtClickedPosition_MatteWithoutItsSource_KeepsNamingThatLayer()
    {
        AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 3);
        Element target = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 4);
        Element other = _scene.Children[0];
        target.MatteSourceKind.Value = MatteSourceKind.Layer;
        target.MatteSourceZIndex.Value = 3;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [target], MediaTime.FromSeconds(1), 10);

        Element[] copies = Copies(other, target);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(copies.Single().ZIndex.Value, Is.EqualTo(10));
            Assert.That(copies.Single().MatteSourceZIndex.Value, Is.EqualTo(3), "That layer still holds the source.");
        });
    }

    [Test]
    public void DuplicateAtClickedPosition_OtherClipOnTheSourceLayer_KeepsNamingThatLayer()
    {
        Element source = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 3);
        Element laterOnThatLayer = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1), zIndex: 3);
        Element target = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 4);
        target.MatteSourceKind.Value = MatteSourceKind.Layer;
        target.MatteSourceZIndex.Value = 3;

        DuplicateOutcome outcome = _service.DuplicateAtClickedPosition(
            _scene, [laterOnThatLayer, target], MediaTime.FromSeconds(1), 10);

        Element targetCopy = Copies(source, laterOnThatLayer, target)
            .Single(copy => copy.MatteSourceKind.Value == MatteSourceKind.Layer);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(targetCopy.MatteSourceZIndex.Value, Is.EqualTo(3), "The copied clip is not there at the matte's time.");
        });
    }

    [Test]
    public void DuplicateAtPosition_RelativeMatte_NamesNoLayer()
    {
        Element source = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 3);
        Element target = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 4);
        target.MatteSourceKind.Value = MatteSourceKind.Above;

        bool success = _service.DuplicateAtPosition(_scene, [source, target], MediaTime.FromSeconds(1), 10);

        Element[] copies = Copies(source, target);
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(copies[1].MatteSourceKind.Value, Is.EqualTo(MatteSourceKind.Above));
            Assert.That(copies[1].MatteSourceZIndex.Value, Is.Null);
        });
    }
}
