using Beutl.Editor;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneGapTests
{
    private static List<SceneGap> Gaps(Scene scene)
        => scene.EnumerateGaps().ToList();

    // The range the timeline searches: the whole scene.
    private static TimeRange SceneRange(Scene scene)
        => new(scene.Start, scene.Duration);

    [Test]
    public void EnumerateGaps_NoElements_ReturnsEmpty()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Assert.That(Gaps(scene), Is.Empty);
    }

    [Test]
    public void EnumerateGaps_ContiguousElements_NoGaps()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2));

        Assert.That(Gaps(scene), Is.Empty);
    }

    [Test]
    public void EnumerateGaps_OverlappingElements_NoGaps()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        harness.AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

        Assert.That(Gaps(scene), Is.Empty);
    }

    [Test]
    public void EnumerateGaps_NestedOverlap_UsesCoveredEndForNextGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        harness.AddElement(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1));

        List<SceneGap> gaps = Gaps(scene);

        Assert.That(gaps, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(gaps[0].Range.Start, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(gaps[0].Range.Duration, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void EnumerateGaps_WithGap_YieldsGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element b = harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        List<SceneGap> gaps = Gaps(scene);

        Assert.That(gaps, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(gaps[0].ZIndex, Is.EqualTo(0));
            Assert.That(gaps[0].Range.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(gaps[0].Range.Duration, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void EnumerateGaps_MultipleZIndexes_YieldsPerZIndex()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 0);
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), zIndex: 0);
        harness.AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 1);
        harness.AddElement(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(1), zIndex: 1);

        List<SceneGap> gaps = Gaps(scene);

        Assert.That(gaps, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(gaps[0].ZIndex, Is.EqualTo(0));
            Assert.That(gaps[1].ZIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void EnumerateGaps_MultipleGapsOnSameZIndex_YieldsAllInOrder()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        harness.AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1));
        harness.AddElement(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1));

        List<SceneGap> gaps = Gaps(scene);

        Assert.That(gaps, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(gaps[0].Range.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(gaps[1].Range.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void EnumerateGaps_SpaceBeforeFirstElement_IsNotAGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1));

        Assert.That(Gaps(scene), Is.Empty);
    }

    [Test]
    public void CloseGapAfter_ClosesGapAfterAnchor()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element b = harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        Element c = harness.AddElement(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(2));

        bool closed = scene.CloseGapAfter(a);

        // CloseGapAfter closes only the gap immediately after the anchor; subsequent
        // elements all shift by the same delta, so the gap between b and c is
        // preserved (moved left along with them).
        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(7)));
            List<SceneGap> gaps = Gaps(scene);
            Assert.That(gaps, Has.Count.EqualTo(1));
            Assert.That(gaps[0].Range.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(gaps[0].Range.Duration, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void CloseGapAfter_NoNextElement_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        bool closed = scene.CloseGapAfter(a);

        Assert.That(closed, Is.False);
    }

    [Test]
    public void CloseGapAfter_TouchingNextElement_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element b = harness.AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));

        bool closed = scene.CloseGapAfter(a);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.False);
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
        });
    }

    [Test]
    public void CloseGapAfter_OverlappingNextElement_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // b overlaps a and extends coverage up to c, so the layer is continuous and there is
        // no gap after a to close.
        Element a = harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        Element b = harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7));
        Element c = harness.AddElement(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1));

        bool closed = scene.CloseGapAfter(a);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.False);
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(12)));
        });
    }

    [Test]
    public void CloseGapAfter_AnchorCoveredByEarlierElement_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // The layer is continuously covered by [0s..100s], so selecting [10s..15s] must not
        // treat the space before [20s..25s] as a closeable gap.
        Element cover = harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(100));
        Element anchor = harness.AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        Element next = harness.AddElement(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5));

        bool closed = scene.CloseGapAfter(anchor);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.False);
            Assert.That(next.Start, Is.EqualTo(TimeSpan.FromSeconds(20)));
        });
    }

    [Test]
    public void CloseGapAfter_CoverageExtendsPastAnchor_UsesCoveredEnd()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // [10s..15s] anchor, then [12s..50s] extends coverage to 50s, then [60s..70s]; the real
        // gap is 50s-60s, so the trailing clip must land at 50s, not be shifted by 45s.
        Element anchor = harness.AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        Element mid = harness.AddElement(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(38));
        Element next = harness.AddElement(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10));

        bool closed = scene.CloseGapAfter(anchor);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(next.Start, Is.EqualTo(TimeSpan.FromSeconds(50)));
        });
    }

    [Test]
    public void CloseGapAfter_ZeroLengthElements_CloseTheGapAfterTheirRun()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // [1s..3s], an empty element at 3s that touches it, an empty element at 4s inside the gap, then [6s..8s].
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element touching = harness.AddElement(TimeSpan.FromSeconds(3), TimeSpan.Zero);
        Element inGap = harness.AddElement(TimeSpan.FromSeconds(4), TimeSpan.Zero);
        Element b = harness.AddElement(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(2));

        bool closedAfterInGap = scene.CloseGapAfter(inGap);
        TimeSpan bAfterFirstClose = b.Start;
        bool closedAfterTouching = scene.CloseGapAfter(touching);

        Assert.Multiple(() =>
        {
            Assert.That(closedAfterInGap, Is.True);
            Assert.That(bAfterFirstClose, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(closedAfterTouching, Is.True);
            Assert.That(a.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(touching.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(inGap.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(Gaps(scene), Is.Empty);
        });
    }

    [Test]
    public void CloseGapAfter_AnchorNotInScene_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        var orphan = new Element { Start = TimeSpan.FromSeconds(1), Length = TimeSpan.FromSeconds(2) };

        bool closed = scene.CloseGapAfter(orphan);

        Assert.That(closed, Is.False);
    }

    [Test]
    public void CloseAllGaps_RemovesAllGapsAcrossZIndexes()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 0);
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), zIndex: 0);
        harness.AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 1);
        harness.AddElement(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(1), zIndex: 1);

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(2));
            Assert.That(Gaps(scene), Is.Empty);
        });
    }

    [Test]
    public void CloseAllGaps_NoGaps_ReturnsZero()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));

        int closed = scene.CloseAllGaps();

        Assert.That(closed, Is.EqualTo(0));
    }

    [Test]
    public void CloseAllGaps_DoesNotCloseLeadingGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(0));
            Assert.That(scene.Children[0].Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void CloseAllGaps_ThreeElementsWithTwoGaps_BecomesContiguous()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Element b = harness.AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1));
        Element c = harness.AddElement(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1));

        scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(a.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
        });
    }

    [Test]
    public void CloseAllGaps_NestedOverlap_ClosesOnlyRealGaps()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        Element b = harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        Element c = harness.AddElement(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1));
        Element d = harness.AddElement(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1));

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(2));
            Assert.That(a.Start, Is.EqualTo(TimeSpan.Zero));
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(d.Start, Is.EqualTo(TimeSpan.FromSeconds(11)));
            Assert.That(Gaps(scene), Is.Empty);
        });
    }

    [Test]
    public void CloseGapAfter_LockedLayer_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        scene.Layers.Add(new TimelineLayer { ZIndex = 0, IsLocked = true });
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element b = harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        bool closed = scene.CloseGapAfter(a);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.False);
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void CloseGapAfter_LockedFollowerBlocksShift_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Anchor [1s..3s], gap 3s-5s, then a locked block [5s..15s], then [16s..18s]. The only
        // unlocked follower would land on the locked block when shifted left, so nothing moves.
        Element anchor = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element locked = harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
        locked.IsLocked = true;
        Element trailing = harness.AddElement(TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(2));

        bool closed = scene.CloseGapAfter(anchor);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.False);
            Assert.That(locked.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(trailing.Start, Is.EqualTo(TimeSpan.FromSeconds(16)));
        });
    }

    [Test]
    public void CloseAllGaps_SkipsLockedLayer()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        scene.Layers.Add(new TimelineLayer { ZIndex = 0, IsLocked = true });
        Element locked0 = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 0);
        Element locked1 = harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), zIndex: 0);
        Element open0 = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 1);
        Element open1 = harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), zIndex: 1);

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(locked1.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(open1.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void CloseAllGaps_LockedElementActsAsBarrier()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // [0s..2s], gap 2s-4s, locked [4s..6s], gap 6s-8s, [8s..10s]. The trailing clip closes
        // the gap after the locked block (down to 6s) but cannot pass it, so the gap before the
        // locked block stays open.
        Element a = harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element locked = harness.AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2));
        locked.IsLocked = true;
        Element c = harness.AddElement(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(2));

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(a.Start, Is.EqualTo(TimeSpan.Zero));
            Assert.That(locked.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(6)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CloseAllGaps_SameStartCohort_ShiftsTogetherRegardlessOfOrder(bool reversedInsertion)
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Two unlocked elements share Start 6s; the gap-close must move them as one cohort and
        // reach the same result no matter which order Children enumerates them in.
        harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element m, n;
        if (reversedInsertion)
        {
            n = harness.AddElement(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(3));
            m = harness.AddElement(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(1));
        }
        else
        {
            m = harness.AddElement(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(1));
            n = harness.AddElement(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(3));
        }

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(m.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(n.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void CloseGapAfter_StopsAtLockedWall()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Anchor [0s..2s]; gap 2s-4s; a same-start cohort at 4s; a locked wall [8s..10s]; another
        // same-start cohort at 12s beyond the wall. The near cohort shifts left to close the gap,
        // the wall stops the shift, and the cohort beyond the wall stays put.
        Element anchor = harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element m = harness.AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1));
        Element n = harness.AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3));
        Element locked = harness.AddElement(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(2));
        locked.IsLocked = true;
        Element c1 = harness.AddElement(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1));
        Element c2 = harness.AddElement(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(4));

        bool closed = scene.CloseGapAfter(anchor);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(m.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(n.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(locked.Start, Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(c1.Start, Is.EqualTo(TimeSpan.FromSeconds(12)));
            Assert.That(c2.Start, Is.EqualTo(TimeSpan.FromSeconds(12)));
        });
    }

    [Test]
    public void FindNextGap_ReturnsNextGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));

        TimeRange? gap = scene.FindNextGap(TimeSpan.FromSeconds(1), SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3))));
    }

    [Test]
    public void FindNextGap_NoNextGap_ReturnsNull()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        TimeRange? gap = scene.FindNextGap(TimeSpan.FromSeconds(5), SceneRange(scene))?.Range;

        Assert.That(gap, Is.Null);
    }

    [Test]
    public void FindNextGap_WhenInsideGap_SkipsCurrentGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        harness.AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));

        TimeRange? gap = scene.FindNextGap(TimeSpan.FromSeconds(4), SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4))));
    }

    [Test]
    public void FindPreviousGap_ReturnsPreviousGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));

        TimeRange? gap = scene.FindPreviousGap(TimeSpan.FromSeconds(6), SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3))));
    }

    [Test]
    public void FindPreviousGap_NoPreviousGap_ReturnsNull()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        TimeRange? gap = scene.FindPreviousGap(TimeSpan.FromSeconds(1), SceneRange(scene))?.Range;

        Assert.That(gap, Is.Null);
    }

    [Test]
    public void FindPreviousGap_OverlappingCrossLayerGaps_PicksGapEndingClosest()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // The layer-1 gap (90s-91s) starts later than the layer-0 gap (80s-200s) but ends
        // far earlier; the previous gap must be chosen by end time, so the wide gap wins.
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(79));
        harness.AddElement(TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(1));
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(89), zIndex: 1);
        harness.AddElement(TimeSpan.FromSeconds(91), TimeSpan.FromSeconds(1), zIndex: 1);

        TimeRange? gap = scene.FindPreviousGap(TimeSpan.FromSeconds(201), SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(120))));
    }

    [Test]
    public void FindNextGap_TiedStart_PicksNearestByEnd()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Two layers both open a gap starting at 2s: layer 0 spans 2s-12s, layer 1 spans 2s-5s.
        // Next must visit the nearer (shorter) gap first, not whichever ZIndex enumerates first.
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 0);
        harness.AddElement(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1), zIndex: 0);
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 1);
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), zIndex: 1);

        TimeRange? gap = scene.FindNextGap(TimeSpan.Zero, SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3))));
    }

    [Test]
    public void FindPreviousGap_TiedEnd_PicksNearestByStart()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Two layers both close a gap ending at 12s: layer 0 spans 2s-12s, layer 1 spans 9s-12s.
        // Previous must visit the nearer (shorter) gap first, not whichever ZIndex enumerates first.
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 0);
        harness.AddElement(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1), zIndex: 0);
        harness.AddElement(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1), zIndex: 1);
        harness.AddElement(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1), zIndex: 1);

        TimeRange? gap = scene.FindPreviousGap(TimeSpan.FromSeconds(20), SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(3))));
    }

    [Test]
    public void FindNextGap_GapStraddlingSearchEnd_ReturnsClampedVisiblePortion()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // A far-right element leaves a gap 10s-100s that straddles a 30s scene end. Its visible
        // portion 10s-30s overlaps the range, so the search still reaches it and returns the raw
        // gap (clamping for display is the caller's job).
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9));
        harness.AddElement(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(10));

        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(30));
        var rawGap = new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(90));
        Assert.Multiple(() =>
        {
            Assert.That(scene.FindNextGap(TimeSpan.Zero, range)?.Range, Is.EqualTo(rawGap));
            Assert.That(scene.FindNextGap(TimeSpan.Zero, SceneRange(scene))?.Range, Is.EqualTo(rawGap));
        });
    }

    [Test]
    public void FindGap_GapStraddlingBothEnds_ReturnsClampedVisiblePortion()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // The only elements end at 10s and start at 100s, so the gap 10s-100s covers the whole
        // active range 50s-80s. A playhead resting exactly on either boundary must still reach the
        // gap through its visible overlap: next from the 50s start, previous from the 80s end.
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        harness.AddElement(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(5));
        var range = new TimeRange(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(30));
        var rawGap = new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(90));

        Assert.Multiple(() =>
        {
            Assert.That(scene.FindNextGap(TimeSpan.FromSeconds(50), range)?.Range, Is.EqualTo(rawGap));
            Assert.That(scene.FindPreviousGap(TimeSpan.FromSeconds(80), range)?.Range, Is.EqualTo(rawGap));
        });
    }

    [Test]
    public void FindGap_BoundsBothSides_IgnoresGapsOutsideOffsetScene()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Active scene range is 50s-150s. A gap 10s-40s sits entirely before it and a gap
        // 200s-260s entirely after it; neither may be selected from either direction.
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9));
        harness.AddElement(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60));
        harness.AddElement(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(80));
        harness.AddElement(TimeSpan.FromSeconds(260), TimeSpan.FromSeconds(10));
        var range = new TimeRange(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(100));
        var inRangeGap = new TimeRange(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(20));

        Assert.Multiple(() =>
        {
            // The only in-range gap is 100s-120s.
            Assert.That(scene.FindNextGap(TimeSpan.FromSeconds(50), range)?.Range, Is.EqualTo(inRangeGap));
            Assert.That(scene.FindPreviousGap(TimeSpan.FromSeconds(150), range)?.Range, Is.EqualTo(inRangeGap));
            // Searching from outside the range must not reach the out-of-range gaps.
            Assert.That(scene.FindNextGap(TimeSpan.FromSeconds(150), range), Is.Null);
            Assert.That(scene.FindPreviousGap(TimeSpan.FromSeconds(50), range), Is.Null);
        });
    }

    [Test]
    public void EnumerateGaps_AnchorIsElementEndingAtGapStart()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // A big element covers 0s-10s while a shorter one nests inside; the gap 10s-12s must anchor
        // on the covering element (it ends at the gap start), not the nested one.
        Element cover = harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        Element nested = harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        Element trailing = harness.AddElement(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1));

        List<SceneGap> gaps = Gaps(scene);

        Assert.Multiple(() =>
        {
            Assert.That(gaps, Has.Count.EqualTo(1));
            Assert.That(gaps[0].Range, Is.EqualTo(new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2))));
            Assert.That(gaps[0].Anchor, Is.SameAs(cover));
        });
    }

    [Test]
    public void FindGapAt_ReturnsGapContainingPointOnLayer()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 0);
        harness.AddElement(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(2), zIndex: 0);

        Assert.Multiple(() =>
        {
            SceneGap? hit = scene.FindGapAt(TimeSpan.FromSeconds(4), zIndex: 0);
            Assert.That(hit?.Range, Is.EqualTo(new TimeRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3))));
            Assert.That(hit?.Anchor, Is.SameAs(a));
            // Half-open: the gap start is inside, the gap end is not.
            Assert.That(scene.FindGapAt(TimeSpan.FromSeconds(3), zIndex: 0), Is.Not.Null);
            Assert.That(scene.FindGapAt(TimeSpan.FromSeconds(6), zIndex: 0), Is.Null);
            // No gap on an empty layer, and no gap when the point sits on an element.
            Assert.That(scene.FindGapAt(TimeSpan.FromSeconds(4), zIndex: 1), Is.Null);
            Assert.That(scene.FindGapAt(TimeSpan.FromSeconds(2), zIndex: 0), Is.Null);
        });
    }

    [Test]
    public void CloseGapAfter_ThenUndo_RestoresPositions()
    {
        using var harness = new SceneHistoryHarness("beutl_gap_undo", duration: TimeSpan.FromSeconds(60));
        Scene scene = harness.Scene;
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element b = harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        Element c = harness.AddElement(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(2));
        int beforeUndoCount = harness.History.UndoCount;

        bool closed = scene.CloseGapAfter(a);
        harness.History.Commit(CommandNames.CloseGap);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(harness.History.UndoCount, Is.EqualTo(beforeUndoCount + 1));
        });

        harness.History.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(a.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(9)));
        });
    }

    [Test]
    public void CloseAllGaps_ThenUndo_RestoresPositions()
    {
        using var harness = new SceneHistoryHarness("beutl_gap_all_undo", duration: TimeSpan.FromSeconds(60));
        Scene scene = harness.Scene;
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Element b = harness.AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1));
        Element c = harness.AddElement(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1));
        int beforeUndoCount = harness.History.UndoCount;

        int closed = scene.CloseAllGaps();
        harness.History.Commit(CommandNames.CloseAllGaps);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(2));
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(harness.History.UndoCount, Is.EqualTo(beforeUndoCount + 1));
        });

        harness.History.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(a.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(8)));
        });
    }
}
