using Beutl.Configuration;
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
        => new(scene.Start.Value, scene.Duration.Value);

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
        harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        harness.AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2));

        Assert.That(Gaps(scene), Is.Empty);
    }

    [Test]
    public void EnumerateGaps_OverlappingElements_NoGaps()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(3));
        harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));

        Assert.That(Gaps(scene), Is.Empty);
    }

    [Test]
    public void EnumerateGaps_NestedOverlap_UsesCoveredEndForNextGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(10));
        harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1));
        harness.AddElement(MediaTime.FromSeconds(12), MediaTime.FromSeconds(1));

        List<SceneGap> gaps = Gaps(scene);

        Assert.That(gaps, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(gaps[0].Range.Start, Is.EqualTo(MediaTime.FromSeconds(10)));
            Assert.That(gaps[0].Range.Duration, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public void EnumerateGaps_WithGap_YieldsGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element b = harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));

        List<SceneGap> gaps = Gaps(scene);

        Assert.That(gaps, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(gaps[0].ZIndex, Is.EqualTo(0));
            Assert.That(gaps[0].Range.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(gaps[0].Range.Duration, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public void EnumerateGaps_MultipleZIndexes_YieldsPerZIndex()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), zIndex: 0);
        harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1), zIndex: 0);
        harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 1);
        harness.AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(1), zIndex: 1);

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
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1));
        harness.AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(1));
        harness.AddElement(MediaTime.FromSeconds(8), MediaTime.FromSeconds(1));

        List<SceneGap> gaps = Gaps(scene);

        Assert.That(gaps, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(gaps[0].Range.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(gaps[1].Range.Start, Is.EqualTo(MediaTime.FromSeconds(5)));
        });
    }

    [Test]
    public void EnumerateGaps_SpaceBeforeFirstElement_IsNotAGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(1));

        Assert.That(Gaps(scene), Is.Empty);
    }

    [Test]
    public void CloseGapAfter_ClosesGapAfterAnchor()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element b = harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        Element c = harness.AddElement(MediaTime.FromSeconds(9), MediaTime.FromSeconds(2));

        bool closed = scene.CloseGapAfter(a);

        // CloseGapAfter closes only the gap immediately after the anchor; subsequent
        // elements all shift by the same delta, so the gap between b and c is
        // preserved (moved left along with them).
        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(7)));
            List<SceneGap> gaps = Gaps(scene);
            Assert.That(gaps, Has.Count.EqualTo(1));
            Assert.That(gaps[0].Range.Start, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(gaps[0].Range.Duration, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public void CloseGapAfter_NoNextElement_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));

        bool closed = scene.CloseGapAfter(a);

        Assert.That(closed, Is.False);
    }

    [Test]
    public void CloseGapAfter_TouchingNextElement_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element b = harness.AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2));

        bool closed = scene.CloseGapAfter(a);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.False);
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [Test]
    public void CloseGapAfter_OverlappingNextElement_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // b overlaps a and extends coverage up to c, so the layer is continuous and there is
        // no gap after a to close.
        Element a = harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(10));
        Element b = harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(7));
        Element c = harness.AddElement(MediaTime.FromSeconds(12), MediaTime.FromSeconds(1));

        bool closed = scene.CloseGapAfter(a);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.False);
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(12)));
        });
    }

    [Test]
    public void CloseGapAfter_AnchorCoveredByEarlierElement_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // The layer is continuously covered by [0s..100s], so selecting [10s..15s] must not
        // treat the space before [20s..25s] as a closeable gap.
        Element cover = harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(100));
        Element anchor = harness.AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(5));
        Element next = harness.AddElement(MediaTime.FromSeconds(20), MediaTime.FromSeconds(5));

        bool closed = scene.CloseGapAfter(anchor);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.False);
            Assert.That(next.Start.Value, Is.EqualTo(MediaTime.FromSeconds(20)));
        });
    }

    [Test]
    public void CloseGapAfter_CoverageExtendsPastAnchor_UsesCoveredEnd()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // [10s..15s] anchor, then [12s..50s] extends coverage to 50s, then [60s..70s]; the real
        // gap is 50s-60s, so the trailing clip must land at 50s, not be shifted by 45s.
        Element anchor = harness.AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(5));
        Element mid = harness.AddElement(MediaTime.FromSeconds(12), MediaTime.FromSeconds(38));
        Element next = harness.AddElement(MediaTime.FromSeconds(60), MediaTime.FromSeconds(10));

        bool closed = scene.CloseGapAfter(anchor);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(next.Start.Value, Is.EqualTo(MediaTime.FromSeconds(50)));
        });
    }

    [Test]
    public void CloseGapAfter_ZeroLengthElements_CloseTheGapAfterTheirRun()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // [1s..3s], an empty element at 3s that touches it, an empty element at 4s inside the gap, then [6s..8s].
        Element a = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element touching = harness.AddElement(MediaTime.FromSeconds(3), MediaTime.Zero);
        Element inGap = harness.AddElement(MediaTime.FromSeconds(4), MediaTime.Zero);
        Element b = harness.AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(2));

        bool closedAfterInGap = scene.CloseGapAfter(inGap);
        MediaTime bAfterFirstClose = b.Start.Value;
        bool closedAfterTouching = scene.CloseGapAfter(touching);

        Assert.Multiple(() =>
        {
            Assert.That(closedAfterInGap, Is.True);
            Assert.That(bAfterFirstClose, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(closedAfterTouching, Is.True);
            Assert.That(a.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(touching.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(inGap.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(Gaps(scene), Is.Empty);
        });
    }

    [Test]
    public void CloseGapAfter_AnchorNotInScene_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        var orphan = new Element { Start = { Value = MediaTime.FromSeconds(1) }, Length = { Value = MediaTime.FromSeconds(2) } };

        bool closed = scene.CloseGapAfter(orphan);

        Assert.That(closed, Is.False);
    }

    [Test]
    public void CloseAllGaps_RemovesAllGapsAcrossZIndexes()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), zIndex: 0);
        harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1), zIndex: 0);
        harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 1);
        harness.AddElement(MediaTime.FromSeconds(7), MediaTime.FromSeconds(1), zIndex: 1);

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
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        harness.AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2));

        int closed = scene.CloseAllGaps();

        Assert.That(closed, Is.EqualTo(0));
    }

    [Test]
    public void CloseAllGaps_DoesNotCloseLeadingGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(0));
            Assert.That(scene.Children[0].Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
        });
    }

    [Test]
    public void CloseAllGaps_ThreeElementsWithTwoGaps_BecomesContiguous()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1));
        Element b = harness.AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(1));
        Element c = harness.AddElement(MediaTime.FromSeconds(8), MediaTime.FromSeconds(1));

        scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(a.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [Test]
    public void CloseAllGaps_NestedOverlap_ClosesOnlyRealGaps()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(10));
        Element b = harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1));
        Element c = harness.AddElement(MediaTime.FromSeconds(12), MediaTime.FromSeconds(1));
        Element d = harness.AddElement(MediaTime.FromSeconds(15), MediaTime.FromSeconds(1));

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(2));
            Assert.That(a.Start.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(10)));
            Assert.That(d.Start.Value, Is.EqualTo(MediaTime.FromSeconds(11)));
            Assert.That(Gaps(scene), Is.Empty);
        });
    }

    [Test]
    public void CloseGapAfter_LockedLayer_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 0 }, IsLocked = { Value = true } });
        Element a = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element b = harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));

        bool closed = scene.CloseGapAfter(a);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.False);
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
        });
    }

    [Test]
    public void CloseGapAfter_LockedFollowerBlocksShift_ReturnsFalse()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Anchor [1s..3s], gap 3s-5s, then a locked block [5s..15s], then [16s..18s]. The only
        // unlocked follower would land on the locked block when shifted left, so nothing moves.
        Element anchor = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element locked = harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(10));
        locked.IsLocked.Value = true;
        Element trailing = harness.AddElement(MediaTime.FromSeconds(16), MediaTime.FromSeconds(2));

        bool closed = scene.CloseGapAfter(anchor);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.False);
            Assert.That(locked.Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(trailing.Start.Value, Is.EqualTo(MediaTime.FromSeconds(16)));
        });
    }

    [Test]
    public void CloseAllGaps_SkipsLockedLayer()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 0 }, IsLocked = { Value = true } });
        Element locked0 = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), zIndex: 0);
        Element locked1 = harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1), zIndex: 0);
        Element open0 = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), zIndex: 1);
        Element open1 = harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1), zIndex: 1);

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(locked1.Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(open1.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
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
        Element a = harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element locked = harness.AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2));
        locked.IsLocked.Value = true;
        Element c = harness.AddElement(MediaTime.FromSeconds(8), MediaTime.FromSeconds(2));

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(a.Start.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(locked.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(6)));
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
        harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element m, n;
        if (reversedInsertion)
        {
            n = harness.AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(3));
            m = harness.AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(1));
        }
        else
        {
            m = harness.AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(1));
            n = harness.AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(3));
        }

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(m.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(n.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public void CloseAllGaps_MovesEachElementOnce()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element[] elements = [.. Enumerable.Range(0, 20)
            .Select(i => harness.AddElement(MediaTime.FromSeconds(i * 3), MediaTime.FromSeconds(1)))];
        int moves = 0;
        foreach (Element element in elements)
        {
            element.Start.ValueChanged += (_, _) => moves++;
        }

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(19));
            Assert.That(moves, Is.EqualTo(19), "The first element stays; every other one moves once.");
            Assert.That(elements.Select(e => e.Start.Value),
                Is.EqualTo(Enumerable.Range(0, 20).Select(i => MediaTime.FromSeconds(i))));
        });
    }

    [Test]
    public void CloseAllGaps_MatchesClosingOneGapAtATime([Range(0, 199)] int seed)
    {
        Scene expected = CreateRandomScene(seed);
        Scene actual = CreateRandomScene(seed);

        int expectedClosed = CloseGapsOneAtATime(expected);
        int actualClosed = actual.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(actualClosed, Is.EqualTo(expectedClosed));
            Assert.That(actual.Children.Select(e => e.Start.Value), Is.EqualTo(expected.Children.Select(e => e.Start.Value)));
        });
    }

    private static Scene CreateRandomScene(int seed)
    {
        var random = new Random(seed);
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(1000) } };
        for (int layer = 0; layer < 3; layer++)
        {
            if (random.Next(5) == 0)
            {
                scene.Layers.Add(new TimelineLayer { ZIndex = { Value = layer }, IsLocked = { Value = true } });
            }

            for (int i = random.Next(2, 14); i > 0; i--)
            {
                bool locked = random.Next(6) == 0;
                scene.Children.Add(new Element
                {
                    Start = { Value = MediaTime.FromSeconds(random.Next(80)) },
                    Length = { Value = MediaTime.FromSeconds(random.Next(locked ? 1 : 0, 8)) },
                    ZIndex = { Value = layer },
                    IsLocked = { Value = locked },
                });
            }
        }

        return scene;
    }

    // Each gap, from the right, moves what follows it up to the first locked cohort by exactly its width, unless
    // that would still collide with what stays. CloseAllGaps reaches the same places without the intermediate moves.
    private static int CloseGapsOneAtATime(Scene scene)
    {
        int closed = 0;
        foreach (IGrouping<int, SceneGap> layer in Gaps(scene).GroupBy(g => g.ZIndex))
        {
            if (scene.IsLayerLocked(layer.Key)) continue;

            foreach (SceneGap gap in layer.OrderByDescending(g => g.Range.Start))
            {
                var toShift = new List<Element>();
                foreach (IGrouping<MediaTime, Element> cohort in scene.Children
                    .Where(e => e.ZIndex.Value == layer.Key && e.Start.Value >= gap.Range.End)
                    .GroupBy(e => e.Start.Value)
                    .OrderBy(g => g.Key))
                {
                    if (cohort.Any(scene.IsElementLocked)) break;
                    toShift.AddRange(cohort);
                }

                Element[] staying = [.. scene.Children.Where(e => e.ZIndex.Value == layer.Key && !toShift.Contains(e))];
                if (toShift.Count == 0
                    || toShift.Any(e => staying.Any(other => other.Range.Intersects(e.Range.SubtractStart(gap.Range.Duration)))))
                {
                    continue;
                }

                foreach (Element element in toShift)
                {
                    element.Start.Value -= gap.Range.Duration;
                }

                closed++;
            }
        }

        return closed;
    }

    [Test]
    public void CloseAllGaps_ClipStillOnALockedElement_KeepsItsGapOpen()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // [0s..5s], gap 5s-10s, then [10s..30s] over the locked [20s..25s]. At 5s the clip would still
        // overlap the locked one, so the gap stays.
        harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(5));
        Element clip = harness.AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(20));
        Element locked = harness.AddElement(MediaTime.FromSeconds(20), MediaTime.FromSeconds(5));
        locked.IsLocked.Value = true;

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(0));
            Assert.That(clip.Start.Value, Is.EqualTo(MediaTime.FromSeconds(10)));
        });
    }

    [Test]
    public void CloseAllGaps_LargerGapBeforeABlockedOne_ClearsTheLockedElement()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // The 5s gap before the last clip cannot close on its own, as the clip would still overlap the locked
        // [240s..260s]. The 100s gap before that takes the clip clear of it, and the 5s gap keeps its width.
        harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(50));
        Element middle = harness.AddElement(MediaTime.FromSeconds(150), MediaTime.FromSeconds(45));
        Element last = harness.AddElement(MediaTime.FromSeconds(200), MediaTime.FromSeconds(50));
        Element locked = harness.AddElement(MediaTime.FromSeconds(240), MediaTime.FromSeconds(20));
        locked.IsLocked.Value = true;

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(middle.Start.Value, Is.EqualTo(MediaTime.FromSeconds(50)));
            Assert.That(last.Start.Value, Is.EqualTo(MediaTime.FromSeconds(100)));
            Assert.That(locked.Start.Value, Is.EqualTo(MediaTime.FromSeconds(240)));
        });
    }

    [Test]
    public void CloseAllGaps_NeverMovesAClipFurtherThanTheGaps()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Neither gap takes the last clip clear of the locked [107s..120s]. Fitting it before the locked clip would
        // take 3s, and the short clip would jump over [100s..101s], so nothing moves.
        harness.AddElement(MediaTime.FromSeconds(100), MediaTime.FromSeconds(1));
        Element jumper = harness.AddElement(MediaTime.FromSeconds(102), MediaTime.FromSeconds(1));
        Element last = harness.AddElement(MediaTime.FromSeconds(105), MediaTime.FromSeconds(5));
        Element locked = harness.AddElement(MediaTime.FromSeconds(107), MediaTime.FromSeconds(13));
        locked.IsLocked.Value = true;

        int closed = scene.CloseAllGaps();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(0));
            Assert.That(jumper.Start.Value, Is.EqualTo(MediaTime.FromSeconds(102)));
            Assert.That(last.Start.Value, Is.EqualTo(MediaTime.FromSeconds(105)));
        });
    }

    [TestCase(true, 3, 6)]
    [TestCase(true, 10, 10)]
    [TestCase(false, 3, 3)]
    public void CloseAllGaps_ElementsPastTheScene_ExtendItOnlyToWhereTheyLand(bool adjust, int duration, int expected)
    {
        bool original = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration.Value;
        GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration.Value = adjust;
        try
        {
            using var harness = new SceneHistoryHarness("beutl_scene_gaps", duration: MediaTime.FromSeconds(duration));
            Scene scene = harness.Scene;
            harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
            harness.AddElement(MediaTime.FromSeconds(20), MediaTime.FromSeconds(2));
            harness.AddElement(MediaTime.FromSeconds(30), MediaTime.FromSeconds(2));

            scene.CloseAllGaps();

            Assert.That(scene.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(expected)));
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration.Value = original;
        }
    }

    [Test]
    public void CloseGapAfter_StopsAtLockedWall()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Anchor [0s..2s]; gap 2s-4s; a same-start cohort at 4s; a locked wall [8s..10s]; another
        // same-start cohort at 12s beyond the wall. The near cohort shifts left to close the gap,
        // the wall stops the shift, and the cohort beyond the wall stays put.
        Element anchor = harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element m = harness.AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(1));
        Element n = harness.AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(3));
        Element locked = harness.AddElement(MediaTime.FromSeconds(8), MediaTime.FromSeconds(2));
        locked.IsLocked.Value = true;
        Element c1 = harness.AddElement(MediaTime.FromSeconds(12), MediaTime.FromSeconds(1));
        Element c2 = harness.AddElement(MediaTime.FromSeconds(12), MediaTime.FromSeconds(4));

        bool closed = scene.CloseGapAfter(anchor);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(m.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(n.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(locked.Start.Value, Is.EqualTo(MediaTime.FromSeconds(8)));
            Assert.That(c1.Start.Value, Is.EqualTo(MediaTime.FromSeconds(12)));
            Assert.That(c2.Start.Value, Is.EqualTo(MediaTime.FromSeconds(12)));
        });
    }

    [Test]
    public void FindNextGap_ReturnsNextGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1));
        harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1));

        TimeRange? gap = scene.FindNextGap(MediaTime.FromSeconds(1), SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3))));
    }

    [Test]
    public void FindNextGap_NoNextGap_ReturnsNull()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));

        TimeRange? gap = scene.FindNextGap(MediaTime.FromSeconds(5), SceneRange(scene))?.Range;

        Assert.That(gap, Is.Null);
    }

    [Test]
    public void FindNextGap_WhenInsideGap_SkipsCurrentGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1));
        harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1));
        harness.AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(1));

        TimeRange? gap = scene.FindNextGap(MediaTime.FromSeconds(4), SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(MediaTime.FromSeconds(6), MediaTime.FromSeconds(4))));
    }

    [Test]
    public void FindPreviousGap_ReturnsPreviousGap()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1));
        harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1));

        TimeRange? gap = scene.FindPreviousGap(MediaTime.FromSeconds(6), SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3))));
    }

    [Test]
    public void FindPreviousGap_NoPreviousGap_ReturnsNull()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));

        TimeRange? gap = scene.FindPreviousGap(MediaTime.FromSeconds(1), SceneRange(scene))?.Range;

        Assert.That(gap, Is.Null);
    }

    [Test]
    public void FindPreviousGap_OverlappingCrossLayerGaps_PicksGapEndingClosest()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // The layer-1 gap (90s-91s) starts later than the layer-0 gap (80s-200s) but ends
        // far earlier; the previous gap must be chosen by end time, so the wide gap wins.
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(79));
        harness.AddElement(MediaTime.FromSeconds(200), MediaTime.FromSeconds(1));
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(89), zIndex: 1);
        harness.AddElement(MediaTime.FromSeconds(91), MediaTime.FromSeconds(1), zIndex: 1);

        TimeRange? gap = scene.FindPreviousGap(MediaTime.FromSeconds(201), SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(MediaTime.FromSeconds(80), MediaTime.FromSeconds(120))));
    }

    [Test]
    public void FindNextGap_TiedStart_PicksNearestByEnd()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Two layers both open a gap starting at 2s: layer 0 spans 2s-12s, layer 1 spans 2s-5s.
        // Next must visit the nearer (shorter) gap first, not whichever ZIndex enumerates first.
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), zIndex: 0);
        harness.AddElement(MediaTime.FromSeconds(12), MediaTime.FromSeconds(1), zIndex: 0);
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), zIndex: 1);
        harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1), zIndex: 1);

        TimeRange? gap = scene.FindNextGap(MediaTime.Zero, SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3))));
    }

    [Test]
    public void FindPreviousGap_TiedEnd_PicksNearestByStart()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Two layers both close a gap ending at 12s: layer 0 spans 2s-12s, layer 1 spans 9s-12s.
        // Previous must visit the nearer (shorter) gap first, not whichever ZIndex enumerates first.
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), zIndex: 0);
        harness.AddElement(MediaTime.FromSeconds(12), MediaTime.FromSeconds(1), zIndex: 0);
        harness.AddElement(MediaTime.FromSeconds(8), MediaTime.FromSeconds(1), zIndex: 1);
        harness.AddElement(MediaTime.FromSeconds(12), MediaTime.FromSeconds(1), zIndex: 1);

        TimeRange? gap = scene.FindPreviousGap(MediaTime.FromSeconds(20), SceneRange(scene))?.Range;

        Assert.That(gap, Is.EqualTo(new TimeRange(MediaTime.FromSeconds(9), MediaTime.FromSeconds(3))));
    }

    [Test]
    public void FindNextGap_GapStraddlingSearchEnd_ReturnsClampedVisiblePortion()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // A far-right element leaves a gap 10s-100s that straddles a 30s scene end. Its visible
        // portion 10s-30s overlaps the range, so the search still reaches it and returns the raw
        // gap (clamping for display is the caller's job).
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(9));
        harness.AddElement(MediaTime.FromSeconds(100), MediaTime.FromSeconds(10));

        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(30));
        var rawGap = new TimeRange(MediaTime.FromSeconds(10), MediaTime.FromSeconds(90));
        Assert.Multiple(() =>
        {
            Assert.That(scene.FindNextGap(MediaTime.Zero, range)?.Range, Is.EqualTo(rawGap));
            Assert.That(scene.FindNextGap(MediaTime.Zero, SceneRange(scene))?.Range, Is.EqualTo(rawGap));
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
        harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(5));
        harness.AddElement(MediaTime.FromSeconds(100), MediaTime.FromSeconds(5));
        var range = new TimeRange(MediaTime.FromSeconds(50), MediaTime.FromSeconds(30));
        var rawGap = new TimeRange(MediaTime.FromSeconds(10), MediaTime.FromSeconds(90));

        Assert.Multiple(() =>
        {
            Assert.That(scene.FindNextGap(MediaTime.FromSeconds(50), range)?.Range, Is.EqualTo(rawGap));
            Assert.That(scene.FindPreviousGap(MediaTime.FromSeconds(80), range)?.Range, Is.EqualTo(rawGap));
        });
    }

    [Test]
    public void FindGap_BoundsBothSides_IgnoresGapsOutsideOffsetScene()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // Active scene range is 50s-150s. A gap 10s-40s sits entirely before it and a gap
        // 200s-260s entirely after it; neither may be selected from either direction.
        harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(9));
        harness.AddElement(MediaTime.FromSeconds(40), MediaTime.FromSeconds(60));
        harness.AddElement(MediaTime.FromSeconds(120), MediaTime.FromSeconds(80));
        harness.AddElement(MediaTime.FromSeconds(260), MediaTime.FromSeconds(10));
        var range = new TimeRange(MediaTime.FromSeconds(50), MediaTime.FromSeconds(100));
        var inRangeGap = new TimeRange(MediaTime.FromSeconds(100), MediaTime.FromSeconds(20));

        Assert.Multiple(() =>
        {
            // The only in-range gap is 100s-120s.
            Assert.That(scene.FindNextGap(MediaTime.FromSeconds(50), range)?.Range, Is.EqualTo(inRangeGap));
            Assert.That(scene.FindPreviousGap(MediaTime.FromSeconds(150), range)?.Range, Is.EqualTo(inRangeGap));
            // Searching from outside the range must not reach the out-of-range gaps.
            Assert.That(scene.FindNextGap(MediaTime.FromSeconds(150), range), Is.Null);
            Assert.That(scene.FindPreviousGap(MediaTime.FromSeconds(50), range), Is.Null);
        });
    }

    [Test]
    public void EnumerateGaps_AnchorIsElementEndingAtGapStart()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        // A big element covers 0s-10s while a shorter one nests inside; the gap 10s-12s must anchor
        // on the covering element (it ends at the gap start), not the nested one.
        Element cover = harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(10));
        Element nested = harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1));
        Element trailing = harness.AddElement(MediaTime.FromSeconds(12), MediaTime.FromSeconds(1));

        List<SceneGap> gaps = Gaps(scene);

        Assert.Multiple(() =>
        {
            Assert.That(gaps, Has.Count.EqualTo(1));
            Assert.That(gaps[0].Range, Is.EqualTo(new TimeRange(MediaTime.FromSeconds(10), MediaTime.FromSeconds(2))));
            Assert.That(gaps[0].Anchor, Is.SameAs(cover));
        });
    }

    [Test]
    public void FindGapAt_ReturnsGapContainingPointOnLayer()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_gaps");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        harness.AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(2), zIndex: 0);

        Assert.Multiple(() =>
        {
            SceneGap? hit = scene.FindGapAt(MediaTime.FromSeconds(4), zIndex: 0);
            Assert.That(hit?.Range, Is.EqualTo(new TimeRange(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3))));
            Assert.That(hit?.Anchor, Is.SameAs(a));
            // Half-open: the gap start is inside, the gap end is not.
            Assert.That(scene.FindGapAt(MediaTime.FromSeconds(3), zIndex: 0), Is.Not.Null);
            Assert.That(scene.FindGapAt(MediaTime.FromSeconds(6), zIndex: 0), Is.Null);
            // No gap on an empty layer, and no gap when the point sits on an element.
            Assert.That(scene.FindGapAt(MediaTime.FromSeconds(4), zIndex: 1), Is.Null);
            Assert.That(scene.FindGapAt(MediaTime.FromSeconds(2), zIndex: 0), Is.Null);
        });
    }

    [Test]
    public void CloseGapAfter_ThenUndo_RestoresPositions()
    {
        using var harness = new SceneHistoryHarness("beutl_gap_undo", duration: MediaTime.FromSeconds(60));
        Scene scene = harness.Scene;
        Element a = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element b = harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        Element c = harness.AddElement(MediaTime.FromSeconds(9), MediaTime.FromSeconds(2));
        int beforeUndoCount = harness.History.UndoCount;

        bool closed = scene.CloseGapAfter(a);
        harness.History.Commit(CommandNames.CloseGap);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(7)));
            Assert.That(harness.History.UndoCount, Is.EqualTo(beforeUndoCount + 1));
        });

        harness.History.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(a.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(9)));
        });
    }

    [Test]
    public void CloseAllGaps_ThenUndo_RestoresPositions()
    {
        using var harness = new SceneHistoryHarness("beutl_gap_all_undo", duration: MediaTime.FromSeconds(60));
        Scene scene = harness.Scene;
        Element a = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1));
        Element b = harness.AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(1));
        Element c = harness.AddElement(MediaTime.FromSeconds(8), MediaTime.FromSeconds(1));
        int beforeUndoCount = harness.History.UndoCount;

        int closed = scene.CloseAllGaps();
        harness.History.Commit(CommandNames.CloseAllGaps);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(2));
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(harness.History.UndoCount, Is.EqualTo(beforeUndoCount + 1));
        });

        harness.History.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(a.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(c.Start.Value, Is.EqualTo(MediaTime.FromSeconds(8)));
        });
    }
}
