using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class RippleEditTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_ripple", start: MediaTime.Zero, duration: MediaTime.FromSeconds(120));
        _scene = _harness.Scene;
        _history = _harness.History;
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(MediaTime start, MediaTime length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    [Test]
    public void ShiftAfter_ShiftsSameLayerElementsAfterAnchor()
    {
        Element a = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2));
        Element b = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        Element c = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2));

        RippleHelper.ShiftAfter(_scene, zIndex: 0, anchorEnd: MediaTime.FromSeconds(2),
            delta: MediaTime.FromSeconds(-2), except: [a]);

        Assert.Multiple(() =>
        {
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.Zero), "b should shift left by 2");
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)), "c should shift left by 2");
            Assert.That(a.Start.Value, Is.EqualTo(MediaTime.Zero), "a is before anchor, unchanged");
        });
    }

    [Test]
    public void ShiftAfter_ZeroDelta_IsNoOp()
    {
        Element b = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));

        RippleHelper.ShiftAfter(_scene, zIndex: 0, anchorEnd: MediaTime.FromSeconds(2),
            delta: MediaTime.Zero, except: []);

        Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void ShiftAfter_DifferentLayer_Unaffected()
    {
        Element b = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 1);

        RippleHelper.ShiftAfter(_scene, zIndex: 0, anchorEnd: MediaTime.FromSeconds(2),
            delta: MediaTime.FromSeconds(-2), except: []);

        Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void ShiftBefore_ShiftsSameLayerElementsEndingAtOrBeforeAnchor()
    {
        Element a = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        Element b = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2));
        Element c = AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(2));

        RippleHelper.ShiftBefore(_scene, zIndex: 0, anchorStart: MediaTime.FromSeconds(6),
            delta: MediaTime.FromSeconds(-2), except: [b]);

        Assert.Multiple(() =>
        {
            Assert.That(a.Start.Value, Is.EqualTo(MediaTime.Zero), "a ends before anchor, shifts left by 2");
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)), "b is excluded, unchanged");
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(6)), "c starts at anchor, unchanged");
        });
    }

    [Test]
    public void ShiftAfterRemoved_NonContiguous_ClosesEachGap()
    {
        // removed A[0,2] and B[10,12]; kept C[4,6] between them, D[14,16] after both.
        Element a = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2));
        Element c = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2));
        Element b = AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(2));
        Element d = AddElement(MediaTime.FromSeconds(14), MediaTime.FromSeconds(2));
        var removed = new (int ZIndex, MediaTime End, MediaTime Length)[]
        {
            (a.ZIndex.Value, a.Range.End, a.Length.Value),
            (b.ZIndex.Value, b.Range.End, b.Length.Value),
        };
        _scene.RemoveChild(a);
        _scene.RemoveChild(b);

        RippleHelper.ShiftAfterRemoved(_scene, removed);

        Assert.Multiple(() =>
        {
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(d.Start.Value, Is.EqualTo(MediaTime.FromSeconds(10)));
        });
    }

    [Test]
    public void Delete_RippleOn_ShiftsSubsequentSameLayerOnly()
    {
        var structure = new ElementStructureService(_history);
        Element removed = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);
        Element afterOther = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 1);

        structure.Delete(_scene, [removed], ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(removed));
            Assert.That(afterSame.Start.Value, Is.EqualTo(MediaTime.Zero), "same-layer follower closes the gap");
            Assert.That(afterOther.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)), "other-layer follower unaffected");
            Assert.That(_history.UndoCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Delete_RippleOff_PreservesTraditionalBehavior()
    {
        var structure = new ElementStructureService(_history);
        Element removed = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);

        structure.Delete(_scene, [removed], ripple: false);

        Assert.That(afterSame.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void Delete_RippleOn_ShiftsSubsequent()
    {
        var structure = new ElementStructureService(_history);
        Element removed = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);

        structure.Delete(_scene, [removed], ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(removed));
            Assert.That(afterSame.Start.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    [Test]
    public async Task CutAsync_RippleOn_ShiftsSubsequent()
    {
        var clipboard = new InMemoryClipboardGateway();
        var clipboardService = new ElementClipboardService(
            _history, clipboard, new ElementDuplicateService(_history), () => Colors.Magenta);
        Element removed = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);

        bool result = await clipboardService.CutAsync(_scene, [removed], ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(_scene.Children, Does.Not.Contain(removed));
            Assert.That(afterSame.Start.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeTrim_EndUnchanged_NoShift()
    {
        var resize = new ElementResizeService(_history);
        Element target = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(4), zIndex: 0);
        Element after = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2), zIndex: 0);

        // Left-edge trim keeps the end fixed, so no gap opens for ripple to close.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.FromSeconds(1), MediaTime.FromSeconds(3), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(target.Length.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(after.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)), "end unchanged -> no shift");
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeGrow_ShiftsUpstreamLeft()
    {
        var resize = new ElementResizeService(_history);
        Element upstream = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(4), zIndex: 0);
        Element target = AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(4), zIndex: 0);

        // Grow the target's left edge from start 6 to 4 (end fixed at 10); the upstream
        // element in front is pushed left to make room.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.FromSeconds(4), MediaTime.FromSeconds(6), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(target.Range.End, Is.EqualTo(MediaTime.FromSeconds(10)), "right edge stays fixed");
            Assert.That(upstream.Start.Value, Is.EqualTo(MediaTime.Zero), "upstream pushed left by 2");
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeGrow_ClampsWhenUpstreamWouldGoNegative()
    {
        var resize = new ElementResizeService(_history);
        Element upstream = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(4), zIndex: 0);
        Element target = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(4), zIndex: 0);

        // Growing target's left edge to start 2 would push upstream (start 0) to -2; the shift is
        // clamped to zero, so the left edge cannot move (upstream is already at the timeline start).
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.FromSeconds(2), MediaTime.FromSeconds(6), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(upstream.Start.Value, Is.EqualTo(MediaTime.Zero), "upstream stays at the timeline start");
            Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)), "left edge clamped to the floor");
            Assert.That(target.Range.End, Is.EqualTo(MediaTime.FromSeconds(8)), "requested end preserved");
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeGrow_ClampsPartialOverrun()
    {
        var resize = new ElementResizeService(_history);
        Element upstream = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(3), zIndex: 0);
        Element target = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(4), zIndex: 0);

        // Requesting start 0 would push upstream (start 1) to -3; clamp allows only a 1s left shift
        // so upstream lands exactly at 0 and target starts at 3.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.Zero, MediaTime.FromSeconds(8), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(upstream.Start.Value, Is.EqualTo(MediaTime.Zero), "upstream clamped to zero");
            Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)), "target start clamped by 1s of slack");
            Assert.That(target.Range.End, Is.EqualTo(MediaTime.FromSeconds(8)), "requested end preserved");
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeTrim_WithUpstream_PullsUpstreamRight()
    {
        var resize = new ElementResizeService(_history);
        Element upstream = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(4), zIndex: 0);
        Element target = AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(4), zIndex: 0);

        // Trim the target's left edge from start 6 to 8; the gap that opens in front is
        // closed by pulling the upstream element right.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.FromSeconds(8), MediaTime.FromSeconds(2), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(8)));
            Assert.That(upstream.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)), "upstream pulled right by 2");
        });
    }

    [Test]
    public void Resize_RippleOn_FloorsLengthWhenRequestedEndPrecedesClampedStart()
    {
        var resize = new ElementResizeService(_history);
        Element upstream = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(3), zIndex: 0);
        Element target = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(4), zIndex: 0);

        int before = _history.UndoCount;
        // The requested end (0.5s) precedes the clamped start (3s), so preserve the upstream
        // boundary and use the positive minimum instead of preserving that end or throwing.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.Zero, MediaTime.FromSeconds(0.5), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(target.Length.Value, Is.EqualTo(MediaTime.FromFrames(1, new Rational(30))));
            Assert.That(upstream.Start.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(upstream.Length.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
        _history.Undo();
        Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
        Assert.That(target.Length.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
        Assert.That(upstream.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
    }

    [Test]
    public void Resize_LockedElement_IsFilteredOut_NoResizeNoCommit()
    {
        var resize = new ElementResizeService(_history);
        Element locked = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), zIndex: 0);
        locked.IsLocked.Value = true;
        int before = _history.UndoCount;

        // Simulates a lock toggled mid-drag: the release still submits the staged request, but the
        // service drops locked targets so nothing is committed.
        resize.Resize(_scene,
            [new ElementResizeRequest(locked, MediaTime.FromSeconds(0), MediaTime.FromSeconds(5), 0)],
            ripple: false);

        Assert.Multiple(() =>
        {
            Assert.That(locked.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)), "a locked clip must not be resized");
            Assert.That(_history.UndoCount, Is.EqualTo(before), "no commit when the only request is filtered");
        });
    }

    [Test]
    public void Resize_RippleOff_PreservesTraditionalBehavior()
    {
        var resize = new ElementResizeService(_history);
        Element target = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), zIndex: 0);
        Element after = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);

        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.FromSeconds(0), MediaTime.FromSeconds(5), 0)],
            ripple: false);

        Assert.That(after.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void Delete_RippleOn_UndoRestoresShift()
    {
        var structure = new ElementStructureService(_history);
        Element removed = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);

        structure.Delete(_scene, [removed], ripple: true);
        Assert.That(afterSame.Start.Value, Is.EqualTo(MediaTime.Zero), "follower shifted to close gap");
        _history.Undo();

        Assert.That(afterSame.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)), "single undo restores the ripple shift");
    }

    [Test]
    public void ShiftAfter_LockedClip_StopsPropagationToLaterFollowers()
    {
        Element a = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2));
        Element locked = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        Element free1 = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2));
        Element free2 = AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(2));
        locked.IsLocked.Value = true;

        RippleHelper.ShiftAfter(_scene, zIndex: 0, anchorEnd: MediaTime.FromSeconds(2),
            delta: MediaTime.FromSeconds(-2), except: [a]);

        Assert.Multiple(() =>
        {
            Assert.That(locked.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)), "locked clip must not move");
            Assert.That(free1.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)),
                "free1 is blocked by the locked clip and stays put");
            Assert.That(free2.Start.Value, Is.EqualTo(MediaTime.FromSeconds(6)),
                "propagation stops at the locked clip, so free2 is not shifted into free1");
        });
    }

    [Test]
    public void Delete_RippleOn_LockedClipBeforeFollower_StopsRipple()
    {
        var structure = new ElementStructureService(_history);
        Element removed = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(10), zIndex: 0);
        Element locked = AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(10), zIndex: 0);
        Element free = AddElement(MediaTime.FromSeconds(20), MediaTime.FromSeconds(10), zIndex: 0);
        locked.IsLocked.Value = true;

        structure.Delete(_scene, [removed], ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(removed));
            Assert.That(locked.Start.Value, Is.EqualTo(MediaTime.FromSeconds(10)), "locked clip is an immovable anchor");
            Assert.That(free.Start.Value, Is.EqualTo(MediaTime.FromSeconds(20)),
                "ripple stops at the locked clip; the later unlocked clip is not shifted over it");
        });
    }

    [Test]
    public void Delete_RippleOn_SeveralGaps_DoesNotMoveAFollowerPastALockedClip()
    {
        var structure = new ElementStructureService(_history);
        Element first = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(8));
        Element locked = AddElement(MediaTime.FromSeconds(9), MediaTime.FromSeconds(1));
        Element second = AddElement(MediaTime.FromSeconds(11), MediaTime.FromSeconds(8));
        Element follower = AddElement(MediaTime.FromSeconds(20), MediaTime.FromSeconds(1));
        locked.IsLocked.Value = true;

        // Closing the gap after the second clip moves the follower to [12, 13]; closing the first would take it
        // to [4, 5], across the locked clip.
        structure.Delete(_scene, [first, second], ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(locked.Start.Value, Is.EqualTo(MediaTime.FromSeconds(9)));
            Assert.That(follower.Start.Value, Is.EqualTo(MediaTime.FromSeconds(12)), "the follower stays on its side of the lock");
        });
    }

    [Test]
    public void ShiftBefore_LeftPush_StopsFreeClipAtLockedUpstream()
    {
        Element locked = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2));
        Element free = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        Element target = AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(2));
        locked.IsLocked.Value = true;

        // A left push (delta < 0) would move free from [2,4] to [0,2], onto the locked clip; the
        // locked clip anchors the ripple, so free is not shifted.
        RippleHelper.ShiftBefore(_scene, zIndex: 0, anchorStart: MediaTime.FromSeconds(6),
            delta: MediaTime.FromSeconds(-2), except: [target]);

        Assert.Multiple(() =>
        {
            Assert.That(locked.Start.Value, Is.EqualTo(MediaTime.Zero), "locked upstream must not move");
            Assert.That(free.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)), "free is blocked by the locked clip");
        });
    }

    [Test]
    public void Resize_RippleOn_RightEdgeGrow_ClampsAtLockedFollower()
    {
        var resize = new ElementResizeService(_history);
        Element target = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), zIndex: 0);
        Element lockedFollower = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);
        lockedFollower.IsLocked.Value = true;

        // Growing target's right edge from 2 to 5 would overlap the immovable locked follower at
        // [2,4]; the grow is clamped to the lock's start so nothing lands on it.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.FromSeconds(0), MediaTime.FromSeconds(5), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Range.End, Is.EqualTo(MediaTime.FromSeconds(2)),
                "right edge clamped to the locked follower's start");
            Assert.That(lockedFollower.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)),
                "ripple resize must not bypass the neighbor's lock");
        });
    }

    [Test]
    public void Resize_RippleOn_RightEdgeGrow_ClampsRunToTouchLockedFollower()
    {
        var resize = new ElementResizeService(_history);
        Element target = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), zIndex: 0);
        Element free = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);
        Element lockedFollower = AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(2), zIndex: 0);
        lockedFollower.IsLocked.Value = true;

        // A free follower sits between target and the locked clip; the grow may advance only until
        // that free follower's right edge touches the lock at 6, so target stops at end 4.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.FromSeconds(0), MediaTime.FromSeconds(10), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Range.End, Is.EqualTo(MediaTime.FromSeconds(4)), "grow stops short of the lock");
            Assert.That(free.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)), "free follower shifted right to touch the lock");
            Assert.That(lockedFollower.Start.Value, Is.EqualTo(MediaTime.FromSeconds(6)), "locked follower stays put");
        });
    }

    [Test]
    public void Resize_RippleOn_StartPastLockedFollower_DoesNotProduceNegativeLength()
    {
        var resize = new ElementResizeService(_history);
        Element target = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), zIndex: 0);
        Element locked = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2), zIndex: 0);
        locked.IsLocked.Value = true;

        // Contrived request: the element both jumps right (start 10) and grows (end 23) past the locked
        // follower. The end-clamp point (3) sits left of the new start, so an unguarded clamp would
        // yield a negative length; the guard keeps the requested positive length instead of corrupting.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.FromSeconds(10), MediaTime.FromSeconds(13), 0)],
            ripple: true);

        Assert.That(target.Length.Value, Is.EqualTo(MediaTime.FromSeconds(13)));
    }

    [Test]
    public void ShiftBefore_LeftEdgeTrim_StopsPullAtLockedUpstream()
    {
        Element free = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2));
        Element locked = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        Element target = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2));
        locked.IsLocked.Value = true;

        // Left-edge trim of target (start 4 -> 6) pulls upstream clips right; shifting the free clip
        // right would land it on the locked clip at [2,4], so the pull stops and it holds its start of 0.
        RippleHelper.ShiftBefore(_scene, zIndex: 0, anchorStart: MediaTime.FromSeconds(4),
            delta: MediaTime.FromSeconds(2), except: [target]);

        Assert.Multiple(() =>
        {
            Assert.That(free.Start.Value, Is.EqualTo(MediaTime.Zero), "free upstream is blocked by the locked clip, not pulled right");
            Assert.That(locked.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)), "locked clip must not move");
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeGrow_LockedUpstreamPacksFree_ClampsGrowAway()
    {
        var resize = new ElementResizeService(_history);
        Element lockedUpstream = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), zIndex: 0);
        Element freeUpstream = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);
        Element target = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(4), zIndex: 0);
        lockedUpstream.IsLocked.Value = true;

        // The free upstream [2,4] is packed against the locked clip's end (2), so the rigid ripple
        // cannot shift it left at all; the locked clip anchors the ripple and the grow is clamped away.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.FromSeconds(2), MediaTime.FromSeconds(6), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)), "grow clamped: locked upstream packs the free clip");
            Assert.That(freeUpstream.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)), "free upstream cannot move onto the lock");
            Assert.That(lockedUpstream.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)), "locked upstream stays put");
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeGrow_LockedClipDirectlyBefore_ClampsGrowAway()
    {
        var resize = new ElementResizeService(_history);
        Element locked = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);
        Element target = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2), zIndex: 0);
        locked.IsLocked.Value = true;

        // The only upstream clip is the locked one directly before target (no free clip to bound the
        // shift), so the grow must still be clamped by target's own room to the lock, which is zero.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.FromSeconds(2), MediaTime.FromSeconds(4), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)), "left edge cannot cross the locked clip");
            Assert.That(target.Range.End, Is.EqualTo(MediaTime.FromSeconds(6)), "end unchanged");
            Assert.That(locked.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)), "locked clip stays put");
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeGrow_ClampsToLockedUpstreamEnd()
    {
        var resize = new ElementResizeService(_history);
        Element locked = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(1), zIndex: 0);
        Element free = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2), zIndex: 0);
        Element target = AddElement(MediaTime.FromSeconds(7), MediaTime.FromSeconds(2), zIndex: 0);
        locked.IsLocked.Value = true;

        // Free [3,5] has room 2 before the locked clip's end (1); the grow may advance only that far,
        // so free lands touching the lock at [1,3] and target's left edge reaches 5.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, MediaTime.FromSeconds(1), MediaTime.FromSeconds(8), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(free.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)), "free clamped to touch the locked clip's end");
            Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)), "grow clamped by the free clip's room before the lock");
            Assert.That(target.Range.End, Is.EqualTo(MediaTime.FromSeconds(9)), "right edge preserved");
            Assert.That(locked.Start.Value, Is.EqualTo(MediaTime.Zero), "locked upstream stays put");
        });
    }

    [Test]
    public void Delete_RippleOn_GroupedFollower_ShiftsWithSameLayer()
    {
        // Ripple closes the gap even when the follower shares a group with the removed element.
        var structure = new ElementStructureService(_history);
        Element removed = AddElement(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);

        _scene.Groups.Add([removed.Id.Value, afterSame.Id.Value]);

        structure.Delete(_scene, [removed], ripple: true);

        Assert.That(afterSame.Start.Value, Is.EqualTo(MediaTime.Zero));
    }
}
