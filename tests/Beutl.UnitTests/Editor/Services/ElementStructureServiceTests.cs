using Beutl.Animation;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Transitions;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.UnitTests.NodeGraph;
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
        _harness = new SceneHistoryHarness("beutl_life", start: MediaTime.Zero, duration: MediaTime.FromSeconds(60));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementStructureService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(MediaTime start, MediaTime length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    [Test]
    public void Split_LaterFailureRollsBackEarlierClipsAndWritesNoFile()
    {
        Element first = AddElement(MediaTime.Zero, MediaTime.FromSeconds(10));
        string directory = Path.GetDirectoryName(_scene.Uri!.LocalPath)!;
        var second = new FailingSplitElement
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(10) },
            ZIndex = { Value = 1 },
            Uri = new Uri(Path.Combine(directory, "second.belm")),
        };
        _scene.Children.Add(second);
        _history.Commit("Setup");
        int before = _history.UndoCount;
        string[] files = Directory.GetFiles(directory, "*.belm", SearchOption.AllDirectories);
        second.FailSerialization = true;
        Assert.That(() => _service.Split(_scene, [first, second], MediaTime.FromSeconds(4)), Throws.InstanceOf<IOException>());
        Assert.Multiple(() =>
        {
            Assert.That(first.Length.Value, Is.EqualTo(MediaTime.FromSeconds(10)));
            Assert.That(_scene.Children, Has.Count.EqualTo(2));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
            Assert.That(_history.HasPendingOperations, Is.False);
            Assert.That(Directory.GetFiles(directory, "*.belm", SearchOption.AllDirectories), Is.EquivalentTo(files));
        });
    }

    public sealed class FailingSplitElement : Element
    {
        public bool FailSerialization { get; set; }
        public override void Serialize(Beutl.Serialization.ICoreSerializationContext context)
        {
            if (FailSerialization) throw new IOException("Injected split serialization failure");
            base.Serialize(context);
        }
    }

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementStructureService(null!));
    }

    [Test]
    public void Delete_RemovesAllAndCommitsOnce()
    {
        Element e1 = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element e2 = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Delete(_scene, [e1, e2]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(e1));
            Assert.That(_scene.Children, Does.Not.Contain(e2));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Delete_PrunesRemovedIdsFromGroups_AndDisbandsShrunkGroup()
    {
        Element e1 = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element e2 = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        _service.Group(_scene, [e1.Id.Value, e2.Id.Value]);

        _service.Delete(_scene, [e1]);

        Assert.That(_scene.Groups.Any(g => g.Contains(e1.Id.Value)), Is.False);
        Assert.That(_scene.Groups, Is.Empty);
    }

    [Test]
    public void Delete_KeepsGroupWithRemainingMembers()
    {
        Element e1 = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element e2 = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        Element e3 = AddElement(MediaTime.FromSeconds(9), MediaTime.FromSeconds(2));
        _service.Group(_scene, [e1.Id.Value, e2.Id.Value, e3.Id.Value]);

        _service.Delete(_scene, [e1]);

        Assert.That(_scene.Groups, Has.Count.EqualTo(1));
        Assert.That(_scene.Groups[0], Is.EquivalentTo(new[] { e2.Id.Value, e3.Id.Value }));
    }

    [Test]
    public void Delete_Undo_PutsTheElementBackOnItsLayer()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 2);
        _history.Commit("Setup");

        _service.Delete(_scene, [element]);
        _history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Contain(element));
            Assert.That(element.ZIndex.Value, Is.EqualTo(2));
        });
    }

    [Test]
    public void Delete_EmptyList_NoCommit()
    {
        int before = _history.UndoCount;

        _service.Delete(_scene, []);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void Split_ShortBothSides_DoesNotCommit()
    {
        // Splitting a ~1-frame element leaves both halves under minDuration, so it must bail.
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(0.02));
        int before = _history.UndoCount;
        int childrenBefore = _scene.Children.Count;

        _service.Split(_scene, [element], element.Start.Value + MediaTime.FromSeconds(0.01));

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Split_AtMidPoint_ProducesBackwardClipAndCommits()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(4));
        _history.Commit("Add element");
        int before = _history.UndoCount;
        Element[] childrenBefore = [.. _scene.Children];
        MediaTime splitAt = MediaTime.FromSeconds(3);

        _service.Split(_scene, [element], splitAt);

        Element[] added = [.. _scene.Children.Except(childrenBefore)];
        Assert.Multiple(() =>
        {
            Assert.That(added, Has.Length.EqualTo(1));
            Assert.That(element.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(added[0].Start.Value, Is.EqualTo(splitAt));
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore.Length + 1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
            // The next save of the scene writes the file.
            Assert.That(added[0].Uri, Is.EqualTo(ElementFileNaming.GetUri(_scene.Uri!, added[0].Id.Value)));
            Assert.That(File.Exists(added[0].Uri!.LocalPath), Is.False);
        });
    }

    [Test]
    public void Split_ShiftsTheLocalKeyFramesOfNodePortsToo()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(4));
        var graph = new GraphModel();
        var drawable = new NodeGraphDrawable { Model = { Value = graph } };
        var port = NodeGraphTestHelpers.Source(graph, 0f, "Amount");
        ((IAnimatablePropertyAdapter)port.Property!).Animation = TestAnimations.Linear(0f, 1f, MediaTime.FromSeconds(4));
        element.AddObject(drawable);
        _history.Commit("Add element");
        Element[] childrenBefore = [.. _scene.Children];

        _service.Split(_scene, [element], MediaTime.FromSeconds(3));
        Element back = _scene.Children.Except(childrenBefore).Single();
        var keyFrames = (IKeyFrameAnimation)new ObjectSearcher(back, v => v is IKeyFrameAnimation).Search()!;

        Assert.That(keyFrames.KeyFrames.Select(k => k.KeyTime.Value),
            Is.EqualTo(new[] { MediaTime.FromSeconds(-2), MediaTime.FromSeconds(2) }));
    }

    [Test]
    public void Split_LeavesTheNewCutWithoutATransition()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(4));
        var enter = new CrossDissolveTransition();
        var exit = new WipeTransition { Direction = { Value = ClipTransitionDirection.TopToBottom } };
        element.EnterTransition.Value = enter;
        element.ExitTransition.Value = exit;
        _history.Commit("Add element");
        Element[] childrenBefore = [.. _scene.Children];

        _service.Split(_scene, [element], MediaTime.FromSeconds(3));
        Element back = _scene.Children.Except(childrenBefore).Single();

        Assert.Multiple(() =>
        {
            Assert.That(element.EnterTransition.Value, Is.SameAs(enter), "the front half keeps the clip's start");
            Assert.That(element.ExitTransition.Value, Is.Null);
            Assert.That(back.EnterTransition.Value, Is.Null);
            Assert.That(back.ExitTransition.Value, Is.TypeOf<WipeTransition>(), "the back half keeps the clip's end");
            Assert.That((back.ExitTransition.Value as WipeTransition)?.Direction.Value, Is.EqualTo(ClipTransitionDirection.TopToBottom));
        });
    }

    [Test]
    public void Group_TwoIds_AddsGroupAndCommits()
    {
        Element e1 = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element e2 = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;
        int groupsBefore = _scene.Groups.Count;

        _service.Group(_scene, [e1.Id.Value, e2.Id.Value]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Groups.Count, Is.EqualTo(groupsBefore + 1));
            Assert.That(_scene.Groups[^1], Does.Contain(e1.Id.Value).And.Contain(e2.Id.Value));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Group_SingleId_DoesNotCreateGroup()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        int beforeUndo = _history.UndoCount;
        int beforeGroups = _scene.Groups.Count;

        _service.Group(_scene, [element.Id.Value]);

        Assert.Multiple(() =>
        {
            // Commit() must no-op when the transaction recorded no operations.
            Assert.That(_scene.Groups.Count, Is.EqualTo(beforeGroups));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo));
        });
    }

    [Test]
    public void Group_SingleId_PullsOutOfExistingGroup()
    {
        Element e1 = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element e2 = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        _service.Group(_scene, [e1.Id.Value, e2.Id.Value]);

        _service.Group(_scene, [e1.Id.Value]);

        Assert.That(_scene.Groups, Is.Empty);
    }

    [Test]
    public void Ungroup_IdsNotInAnyGroup_IsNoOp()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        int beforeUndo = _history.UndoCount;
        int beforeGroups = _scene.Groups.Count;

        _service.Ungroup(_scene, [element.Id.Value]);

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
        Element e1 = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element e2 = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        Element e3 = AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(2));
        _service.Group(_scene, [e1.Id.Value, e2.Id.Value, e3.Id.Value]);
        int before = _history.UndoCount;

        _service.Ungroup(_scene, [e1.Id.Value, e2.Id.Value, e3.Id.Value]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Groups, Is.Empty);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Group_LockedElementId_IsExcluded()
    {
        Element locked = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        locked.IsLocked.Value = true;
        Element e1 = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        Element e2 = AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(2));

        _service.Group(_scene, [locked.Id.Value, e1.Id.Value, e2.Id.Value]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Groups, Has.Count.EqualTo(1));
            Assert.That(_scene.Groups[0], Is.EquivalentTo(new[] { e1.Id.Value, e2.Id.Value }));
        });
    }

    [Test]
    public void Ungroup_LockedElementId_IsIgnored()
    {
        Element e1 = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element e2 = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        _service.Group(_scene, [e1.Id.Value, e2.Id.Value]);
        e1.IsLocked.Value = true;
        int before = _history.UndoCount;

        _service.Ungroup(_scene, [e1.Id.Value]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Groups, Has.Count.EqualTo(1));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Ungroup_EditableMember_KeepsGroupWhenOnlyLockedMemberWouldRemain()
    {
        Element editable = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element locked = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        _service.Group(_scene, [editable.Id.Value, locked.Id.Value]);
        locked.IsLocked.Value = true;
        int before = _history.UndoCount;

        // Disbanding the two-member group would strand the locked member, silently changing its
        // grouping; the ungroup of the editable member must no-op instead.
        _service.Ungroup(_scene, [editable.Id.Value]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Groups, Has.Count.EqualTo(1));
            Assert.That(_scene.Groups[0], Is.EquivalentTo(new[] { editable.Id.Value, locked.Id.Value }));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Group_SingleEditableMember_KeepsGroupWhenOnlyLockedMemberWouldRemain()
    {
        Element editable = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element locked = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        _service.Group(_scene, [editable.Id.Value, locked.Id.Value]);
        locked.IsLocked.Value = true;
        int before = _history.UndoCount;

        // A single-id Group acts as "ungroup this element"; it must not strand the locked member.
        _service.Group(_scene, [editable.Id.Value]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Groups, Has.Count.EqualTo(1));
            Assert.That(_scene.Groups[0], Is.EquivalentTo(new[] { editable.Id.Value, locked.Id.Value }));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Delete_EditableMember_PrunesIdFromLockedGroup_NoStaleId()
    {
        Element editable = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element locked = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        _service.Group(_scene, [editable.Id.Value, locked.Id.Value]);
        locked.IsLocked.Value = true;

        // Removal (unlike a structural regroup) must always prune the removed id: leaving it in the
        // group would reference a deleted element. The two-member group disbands as usual.
        _service.Delete(_scene, [editable]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(editable));
            Assert.That(_scene.Groups.Any(g => g.Contains(editable.Id.Value)), Is.False, "no stale id left behind");
            Assert.That(_scene.Groups, Is.Empty);
        });
    }

    [Test]
    public void Group_EditableMemberEntangledWithLock_RefusesWithoutOverlappingGroups()
    {
        Element a = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element b = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        Element c = AddElement(MediaTime.FromSeconds(9), MediaTime.FromSeconds(2));
        _service.Group(_scene, [a.Id.Value, b.Id.Value]);
        a.IsLocked.Value = true;

        // Regrouping b with c would strand locked a in {a}; b is refused, so {a,b} stays intact and
        // no overlapping {b,c} group is created that would confuse group-based selection.
        _service.Group(_scene, [b.Id.Value, c.Id.Value]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Groups, Has.Count.EqualTo(1));
            Assert.That(_scene.Groups[0], Is.EquivalentTo(new[] { a.Id.Value, b.Id.Value }));
        });
    }

    [Test]
    public void Delete_LockedElement_IsSkipped()
    {
        Element locked = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        locked.IsLocked.Value = true;
        Element free = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
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
        Element locked = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        locked.IsLocked.Value = true;
        int before = _history.UndoCount;

        _service.Delete(_scene, [locked]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Contain(locked));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Delete_LayerLockedElement_IsSkipped()
    {
        Element onLockedLayer = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 3);
        _scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 3 }, IsLocked = { Value = true } });
        int before = _history.UndoCount;

        _service.Delete(_scene, [onLockedLayer]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Contain(onLockedLayer));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Split_LockedElement_IsNotSplit()
    {
        Element locked = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(4));
        locked.IsLocked.Value = true;
        int beforeCount = _scene.Children.Count;

        _service.Split(_scene, [locked], MediaTime.FromSeconds(2));

        Assert.That(_scene.Children.Count, Is.EqualTo(beforeCount));
    }
}
