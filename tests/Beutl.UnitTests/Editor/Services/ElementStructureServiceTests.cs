using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementStructureServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementStructureService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_life", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(60));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementStructureService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementStructureService(null!));
    }

    [Test]
    public void Exclude_RemovesAllAndCommitsOnce()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Exclude(_scene, [e1, e2]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(e1));
            Assert.That(_scene.Children, Does.Not.Contain(e2));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Delete_RemovesAllAndCommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Delete(_scene, [element]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(element));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Delete_PrunesDeletedIdsFromGroups_AndDisbandsShrunkGroup()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        _service.Group(_scene, [e1.Id, e2.Id]);

        _service.Delete(_scene, [e1]);

        Assert.That(_scene.Groups.Any(g => g.Contains(e1.Id)), Is.False);
        Assert.That(_scene.Groups, Is.Empty);
    }

    [Test]
    public void Delete_KeepsGroupWithRemainingMembers()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        Element e3 = AddElement(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(2));
        _service.Group(_scene, [e1.Id, e2.Id, e3.Id]);

        _service.Delete(_scene, [e1]);

        Assert.That(_scene.Groups, Has.Count.EqualTo(1));
        Assert.That(_scene.Groups[0], Is.EquivalentTo(new[] { e2.Id, e3.Id }));
    }

    [Test]
    public void Exclude_PrunesDeletedIdsFromGroups()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        _service.Group(_scene, [e1.Id, e2.Id]);

        _service.Exclude(_scene, [e1]);

        Assert.That(_scene.Groups.Any(g => g.Contains(e1.Id)), Is.False);
        Assert.That(_scene.Groups, Is.Empty);
    }

    [Test]
    public void Exclude_EmptyList_NoCommit()
    {
        int before = _history.UndoCount;

        _service.Exclude(_scene, []);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void Split_ShortBothSides_DoesNotCommit()
    {
        // Splitting a ~1-frame element leaves both halves under minDuration, so it must bail.
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20));
        int before = _history.UndoCount;

        SplitOutcome outcome = _service.Split(_scene, [element], element.Start + TimeSpan.FromMilliseconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.NewElements, Is.Empty);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Split_AtMidPoint_ProducesBackwardClipAndCommits()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
        int before = _history.UndoCount;
        int childrenBefore = _scene.Children.Count;
        TimeSpan splitAt = TimeSpan.FromSeconds(3);

        SplitOutcome outcome = _service.Split(_scene, [element], splitAt);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.NewElements, Has.Count.EqualTo(1));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(outcome.NewElements[0].Start, Is.EqualTo(splitAt));
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Group_TwoIds_AddsGroupAndCommits()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;
        int groupsBefore = _scene.Groups.Count;

        GroupOutcome outcome = _service.Group(_scene, [e1.Id, e2.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Created, Is.True);
            Assert.That(_scene.Groups.Count, Is.EqualTo(groupsBefore + 1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Group_SingleId_DoesNotCreateGroup()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int beforeUndo = _history.UndoCount;
        int beforeGroups = _scene.Groups.Count;

        GroupOutcome outcome = _service.Group(_scene, [element.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Created, Is.False);
            // Commit() must no-op when the transaction recorded no operations.
            Assert.That(_scene.Groups.Count, Is.EqualTo(beforeGroups));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo));
        });
    }

    [Test]
    public void Group_SingleId_PullsOutOfExistingGroup()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        _service.Group(_scene, [e1.Id, e2.Id]);

        _service.Group(_scene, [e1.Id]);

        Assert.That(_scene.Groups, Is.Empty);
    }

    [Test]
    public void Ungroup_IdsNotInAnyGroup_IsNoOp()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int beforeUndo = _history.UndoCount;
        int beforeGroups = _scene.Groups.Count;

        _service.Ungroup(_scene, [element.Id]);

        Assert.Multiple(() =>
        {
            // Same contract as Group_SingleId: an unconditional Commit would pull in unrelated mutations.
            Assert.That(_scene.Groups.Count, Is.EqualTo(beforeGroups));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo));
        });
    }

    [Test]
    public void Ungroup_RemovesIdsFromGroups_AndCommits()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        Element e3 = AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
        _service.Group(_scene, [e1.Id, e2.Id, e3.Id]);
        int before = _history.UndoCount;

        _service.Ungroup(_scene, [e1.Id, e2.Id, e3.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Groups, Is.Empty);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Group_LockedElementId_IsExcluded()
    {
        Element locked = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        locked.IsLocked = true;
        Element e1 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));

        GroupOutcome outcome = _service.Group(_scene, [locked.Id, e1.Id, e2.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Created, Is.True);
            Assert.That(_scene.Groups, Has.Count.EqualTo(1));
            Assert.That(_scene.Groups[0], Is.EquivalentTo(new[] { e1.Id, e2.Id }));
        });
    }

    [Test]
    public void Ungroup_LockedElementId_IsIgnored()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        _service.Group(_scene, [e1.Id, e2.Id]);
        e1.IsLocked = true;
        int before = _history.UndoCount;

        _service.Ungroup(_scene, [e1.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Groups, Has.Count.EqualTo(1));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Ungroup_EditableMember_KeepsGroupWhenOnlyLockedMemberWouldRemain()
    {
        Element editable = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element locked = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        _service.Group(_scene, [editable.Id, locked.Id]);
        locked.IsLocked = true;
        int before = _history.UndoCount;

        // Disbanding the two-member group would strand the locked member, silently changing its
        // grouping; the ungroup of the editable member must no-op instead.
        _service.Ungroup(_scene, [editable.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Groups, Has.Count.EqualTo(1));
            Assert.That(_scene.Groups[0], Is.EquivalentTo(new[] { editable.Id, locked.Id }));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Group_SingleEditableMember_KeepsGroupWhenOnlyLockedMemberWouldRemain()
    {
        Element editable = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element locked = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        _service.Group(_scene, [editable.Id, locked.Id]);
        locked.IsLocked = true;
        int before = _history.UndoCount;

        // A single-id Group acts as "ungroup this element"; it must not strand the locked member.
        GroupOutcome outcome = _service.Group(_scene, [editable.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Created, Is.False);
            Assert.That(_scene.Groups, Has.Count.EqualTo(1));
            Assert.That(_scene.Groups[0], Is.EquivalentTo(new[] { editable.Id, locked.Id }));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Delete_LockedElement_IsSkipped()
    {
        Element locked = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        locked.IsLocked = true;
        Element free = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Delete(_scene, [locked, free]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Contain(locked));
            Assert.That(_scene.Children, Does.Not.Contain(free));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Delete_AllLocked_DoesNotCommit()
    {
        Element locked = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        locked.IsLocked = true;
        int before = _history.UndoCount;

        _service.Delete(_scene, [locked]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Contain(locked));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Exclude_LayerLockedElement_IsSkipped()
    {
        Element onLockedLayer = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 3);
        _scene.Layers.Add(new TimelineLayer { ZIndex = 3, IsLocked = true });
        int before = _history.UndoCount;

        _service.Exclude(_scene, [onLockedLayer]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Contain(onLockedLayer));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Split_LockedElement_IsNotSplit()
    {
        Element locked = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4));
        locked.IsLocked = true;
        int beforeCount = _scene.Children.Count;

        SplitOutcome outcome = _service.Split(_scene, [locked], TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.NewElements, Is.Empty);
            Assert.That(_scene.Children.Count, Is.EqualTo(beforeCount));
        });
    }
}
