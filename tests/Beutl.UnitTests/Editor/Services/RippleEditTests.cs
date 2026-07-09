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
        _harness = new SceneHistoryHarness("beutl_ripple", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(120));
        _scene = _harness.Scene;
        _history = _harness.History;
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    [Test]
    public void ShiftAfter_ShiftsSameLayerElementsAfterAnchor()
    {
        Element a = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2));
        Element b = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        Element c = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2));

        RippleHelper.ShiftAfter(_scene, zIndex: 0, anchorEnd: TimeSpan.FromSeconds(2),
            delta: TimeSpan.FromSeconds(-2), except: [a]);

        Assert.Multiple(() =>
        {
            Assert.That(b.Start, Is.EqualTo(TimeSpan.Zero), "b should shift left by 2");
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(2)), "c should shift left by 2");
            Assert.That(a.Start, Is.EqualTo(TimeSpan.Zero), "a is before anchor, unchanged");
        });
    }

    [Test]
    public void ShiftAfter_ZeroDelta_IsNoOp()
    {
        Element b = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        RippleHelper.ShiftAfter(_scene, zIndex: 0, anchorEnd: TimeSpan.FromSeconds(2),
            delta: TimeSpan.Zero, except: []);

        Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void ShiftAfter_DifferentLayer_Unaffected()
    {
        Element b = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 1);

        RippleHelper.ShiftAfter(_scene, zIndex: 0, anchorEnd: TimeSpan.FromSeconds(2),
            delta: TimeSpan.FromSeconds(-2), except: []);

        Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void ShiftBefore_ShiftsSameLayerElementsEndingAtOrBeforeAnchor()
    {
        Element a = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        Element b = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2));
        Element c = AddElement(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(2));

        RippleHelper.ShiftBefore(_scene, zIndex: 0, anchorStart: TimeSpan.FromSeconds(6),
            delta: TimeSpan.FromSeconds(-2), except: [b]);

        Assert.Multiple(() =>
        {
            Assert.That(a.Start, Is.EqualTo(TimeSpan.Zero), "a ends before anchor, shifts left by 2");
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(4)), "b is excluded, unchanged");
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(6)), "c starts at anchor, unchanged");
        });
    }

    [Test]
    public void ShiftAfterRemoved_NonContiguous_ClosesEachGap()
    {
        // removed A[0,2] and B[10,12]; kept C[4,6] between them, D[14,16] after both.
        Element a = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2));
        Element c = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2));
        Element b = AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
        Element d = AddElement(TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(2));
        var removed = new (int ZIndex, TimeSpan End, TimeSpan Length)[]
        {
            (a.ZIndex, a.Range.End, a.Length),
            (b.ZIndex, b.Range.End, b.Length),
        };
        _scene.RemoveChild(a);
        _scene.RemoveChild(b);

        RippleHelper.ShiftAfterRemoved(_scene, removed);

        Assert.Multiple(() =>
        {
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(d.Start, Is.EqualTo(TimeSpan.FromSeconds(10)));
        });
    }

    [Test]
    public void Exclude_RippleOn_ShiftsSubsequentSameLayerOnly()
    {
        var structure = new ElementStructureService(_history);
        Element removed = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 0);
        Element afterOther = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 1);

        structure.Exclude(_scene, [removed], ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(removed));
            Assert.That(afterSame.Start, Is.EqualTo(TimeSpan.Zero), "same-layer follower closes the gap");
            Assert.That(afterOther.Start, Is.EqualTo(TimeSpan.FromSeconds(2)), "other-layer follower unaffected");
            Assert.That(_history.UndoCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Exclude_RippleOff_PreservesTraditionalBehavior()
    {
        var structure = new ElementStructureService(_history);
        Element removed = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 0);

        structure.Exclude(_scene, [removed], ripple: false);

        Assert.That(afterSame.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void Delete_RippleOn_ShiftsSubsequent()
    {
        var structure = new ElementStructureService(_history);
        Element removed = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 0);

        structure.Delete(_scene, [removed], ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(removed));
            Assert.That(afterSame.Start, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public async Task CutAsync_RippleOn_ShiftsSubsequent()
    {
        var clipboard = new InMemoryClipboardGateway();
        var clipboardService = new ElementClipboardService(
            _history, clipboard, new ElementDuplicateService(_history), () => Colors.Magenta);
        Element removed = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 0);

        bool result = await clipboardService.CutAsync(_scene, [removed], ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(_scene.Children, Does.Not.Contain(removed));
            Assert.That(afterSame.Start, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Resize_RippleOn_RightEdgeGrow_ShiftsSubsequentByEndDelta()
    {
        var resize = new ElementResizeService(_history);
        Element target = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), zIndex: 0);
        Element after = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 0);

        resize.Resize(_scene,
            [new ElementResizeRequest(target, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Length, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(after.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeTrim_EndUnchanged_NoShift()
    {
        var resize = new ElementResizeService(_history);
        Element target = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4), zIndex: 0);
        Element after = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), zIndex: 0);

        // Left-edge trim keeps the end fixed, so no gap opens for ripple to close.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(target.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(after.Start, Is.EqualTo(TimeSpan.FromSeconds(4)), "end unchanged -> no shift");
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeGrow_ShiftsUpstreamLeft()
    {
        var resize = new ElementResizeService(_history);
        Element upstream = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), zIndex: 0);
        Element target = AddElement(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4), zIndex: 0);

        // Grow the target's left edge from start 6 to 4 (end fixed at 10); the upstream
        // element in front is pushed left to make room.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(target.Range.End, Is.EqualTo(TimeSpan.FromSeconds(10)), "right edge stays fixed");
            Assert.That(upstream.Start, Is.EqualTo(TimeSpan.Zero), "upstream pushed left by 2");
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeGrow_ClampsWhenUpstreamWouldGoNegative()
    {
        var resize = new ElementResizeService(_history);
        Element upstream = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4), zIndex: 0);
        Element target = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4), zIndex: 0);

        // Growing target's left edge to start 2 would push upstream (start 0) to -2; the shift is
        // clamped to zero, so the left edge cannot move (upstream is already at the timeline start).
        resize.Resize(_scene,
            [new ElementResizeRequest(target, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(upstream.Start, Is.EqualTo(TimeSpan.Zero), "upstream stays at the timeline start");
            Assert.That(target.Start, Is.EqualTo(TimeSpan.FromSeconds(4)), "left edge clamped to the floor");
            Assert.That(target.Range.End, Is.EqualTo(TimeSpan.FromSeconds(8)), "requested end preserved");
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeGrow_ClampsPartialOverrun()
    {
        var resize = new ElementResizeService(_history);
        Element upstream = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), zIndex: 0);
        Element target = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4), zIndex: 0);

        // Requesting start 0 would push upstream (start 1) to -3; clamp allows only a 1s left shift
        // so upstream lands exactly at 0 and target starts at 3.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, TimeSpan.Zero, TimeSpan.FromSeconds(8), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(upstream.Start, Is.EqualTo(TimeSpan.Zero), "upstream clamped to zero");
            Assert.That(target.Start, Is.EqualTo(TimeSpan.FromSeconds(3)), "target start clamped by 1s of slack");
            Assert.That(target.Range.End, Is.EqualTo(TimeSpan.FromSeconds(8)), "requested end preserved");
        });
    }

    [Test]
    public void Resize_RippleOn_LeftEdgeTrim_WithUpstream_PullsUpstreamRight()
    {
        var resize = new ElementResizeService(_history);
        Element upstream = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), zIndex: 0);
        Element target = AddElement(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4), zIndex: 0);

        // Trim the target's left edge from start 6 to 8; the gap that opens in front is
        // closed by pulling the upstream element right.
        resize.Resize(_scene,
            [new ElementResizeRequest(target, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(2), 0)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Start, Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(upstream.Start, Is.EqualTo(TimeSpan.FromSeconds(4)), "upstream pulled right by 2");
        });
    }

    [Test]
    public void Resize_RippleOn_ThrowsWhenClampWouldMakeLengthNonPositive()
    {
        var resize = new ElementResizeService(_history);
        Element upstream = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), zIndex: 0);
        Element target = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4), zIndex: 0);

        // Incoherent request (end 0.5s sits before the clamped start 3s): keeping upstream >= 0
        // and the requested end cannot yield a positive length. Not reachable from the UI (end fixed).
        Assert.Throws<ArgumentOutOfRangeException>(() => resize.Resize(_scene,
            [new ElementResizeRequest(target, TimeSpan.Zero, TimeSpan.FromSeconds(0.5), 0)],
            ripple: true));

        Assert.Multiple(() =>
        {
            Assert.That(target.Start, Is.EqualTo(TimeSpan.FromSeconds(4)), "rejected before any mutation");
            Assert.That(upstream.Start, Is.EqualTo(TimeSpan.FromSeconds(1)), "upstream untouched");
        });
    }

    [Test]
    public void Resize_RippleOff_PreservesTraditionalBehavior()
    {
        var resize = new ElementResizeService(_history);
        Element target = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), zIndex: 0);
        Element after = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 0);

        resize.Resize(_scene,
            [new ElementResizeRequest(target, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5), 0)],
            ripple: false);

        Assert.That(after.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void Exclude_RippleOn_UndoRestoresShift()
    {
        var structure = new ElementStructureService(_history);
        Element removed = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 0);

        structure.Exclude(_scene, [removed], ripple: true);
        Assert.That(afterSame.Start, Is.EqualTo(TimeSpan.Zero), "follower shifted to close gap");
        _history.Undo();

        // Harness wires CoreObjectOperationObserver only, not CollectionOperationObserver, so removal undo is not asserted here.
        Assert.That(afterSame.Start, Is.EqualTo(TimeSpan.FromSeconds(2)), "single undo restores the ripple shift");
    }

    [Test]
    public void Exclude_RippleOn_GroupedFollower_ShiftsWithSameLayer()
    {
        // A grouped follower shares a group with the removed element but sits after it.
        // Ripple must still close the timeline gap; grouping only locks the grouped member
        // when clip-lock is on (item #2). Ripple itself does not skip grouped elements.
        var structure = new ElementStructureService(_history);
        Element removed = AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), zIndex: 0);
        Element afterSame = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 0);

        _scene.Groups.Add([removed.Id, afterSame.Id]);

        structure.Exclude(_scene, [removed], ripple: true);

        Assert.That(afterSame.Start, Is.EqualTo(TimeSpan.Zero));
    }
}
