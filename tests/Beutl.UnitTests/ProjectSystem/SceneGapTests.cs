using Beutl.Editor;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneGapTests
{
    private static string GetTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"beutl_scene_gaps_{Guid.NewGuid():N}");
    }

    private static Scene CreateScene(string basePath)
    {
        Directory.CreateDirectory(basePath);
        return new Scene(100, 100, string.Empty)
        {
            Uri = new Uri(Path.Combine(basePath, "test.scene")),
        };
    }

    private static Element CreateElement(string basePath, TimeSpan start, TimeSpan length, int zIndex = 0)
    {
        return new Element
        {
            Start = start,
            Length = length,
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(basePath, $"{Guid.NewGuid():N}.layer")),
        };
    }

    private static List<(int ZIndex, TimeRange Gap)> Gaps(Scene scene, bool includeLeadingGap = false)
        => scene.EnumerateGaps(includeLeadingGap).ToList();

    [Test]
    public void EnumerateGaps_NoElements_ReturnsEmpty()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Assert.That(Gaps(scene), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EnumerateGaps_ContiguousElements_NoGaps()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2)));

            Assert.That(Gaps(scene), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EnumerateGaps_TouchingElements_NoGaps()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2)));

            Assert.That(Gaps(scene), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EnumerateGaps_OverlappingElements_NoGaps()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)));

            Assert.That(Gaps(scene), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EnumerateGaps_NestedOverlap_UsesCoveredEndForNextGap()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.Zero, TimeSpan.FromSeconds(10)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1)));

            List<(int ZIndex, TimeRange Gap)> gaps = Gaps(scene);

            Assert.That(gaps, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(gaps[0].Gap.Start, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(gaps[0].Gap.Duration, Is.EqualTo(TimeSpan.FromSeconds(2)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EnumerateGaps_WithGap_YieldsGap()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element a = CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            Element b = CreateElement(basePath, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
            scene.Children.Add(a);
            scene.Children.Add(b);

            List<(int ZIndex, TimeRange Gap)> gaps = Gaps(scene);

            Assert.That(gaps, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(gaps[0].ZIndex, Is.EqualTo(0));
                Assert.That(gaps[0].Gap.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
                Assert.That(gaps[0].Gap.Duration, Is.EqualTo(TimeSpan.FromSeconds(2)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EnumerateGaps_MultipleZIndexes_YieldsPerZIndex()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 0));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), zIndex: 0));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 1));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(1), zIndex: 1));

            List<(int ZIndex, TimeRange Gap)> gaps = Gaps(scene);

            Assert.That(gaps, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(gaps[0].ZIndex, Is.EqualTo(0));
                Assert.That(gaps[1].ZIndex, Is.EqualTo(1));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EnumerateGaps_MultipleGapsOnSameZIndex_YieldsAllInOrder()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1)));

            List<(int ZIndex, TimeRange Gap)> gaps = Gaps(scene);

            Assert.That(gaps, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(gaps[0].Gap.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(gaps[1].Gap.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EnumerateGaps_IncludeLeadingGap_YieldsLeadingGap()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1)));

            List<(int ZIndex, TimeRange Gap)> gaps = Gaps(scene, includeLeadingGap: true);

            Assert.That(gaps, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(gaps[0].Gap.Start, Is.EqualTo(TimeSpan.Zero));
                Assert.That(gaps[0].Gap.Duration, Is.EqualTo(TimeSpan.FromSeconds(3)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EnumerateGaps_IncludeLeadingGapFalse_OmitsLeadingGap()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1)));

            Assert.That(Gaps(scene, includeLeadingGap: false), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseGap_ClosesGapAfterAnchor()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element a = CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            Element b = CreateElement(basePath, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
            Element c = CreateElement(basePath, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(2));
            scene.Children.Add(a);
            scene.Children.Add(b);
            scene.Children.Add(c);

            bool closed = scene.CloseGap(a);

            // CloseGap closes only the gap immediately after the anchor; subsequent
            // elements all shift by the same delta, so the gap between b and c is
            // preserved (moved left along with them).
            Assert.Multiple(() =>
            {
                Assert.That(closed, Is.True);
                Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
                Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(7)));
                List<(int ZIndex, TimeRange Gap)> gaps = Gaps(scene);
                Assert.That(gaps, Has.Count.EqualTo(1));
                Assert.That(gaps[0].Gap.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(gaps[0].Gap.Duration, Is.EqualTo(TimeSpan.FromSeconds(2)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseGap_NoNextElement_ReturnsFalse()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element a = CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            scene.Children.Add(a);

            bool closed = scene.CloseGap(a);

            Assert.That(closed, Is.False);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseGap_TouchingNextElement_ReturnsFalse()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element a = CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            Element b = CreateElement(basePath, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));
            scene.Children.Add(a);
            scene.Children.Add(b);

            bool closed = scene.CloseGap(a);

            Assert.Multiple(() =>
            {
                Assert.That(closed, Is.False);
                Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseGap_OverlappingNextElement_ReturnsFalse()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            // b overlaps a and extends coverage up to c, so the layer is continuous and there is
            // no gap after a to close.
            Element a = CreateElement(basePath, TimeSpan.Zero, TimeSpan.FromSeconds(10));
            Element b = CreateElement(basePath, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7));
            Element c = CreateElement(basePath, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1));
            scene.Children.Add(a);
            scene.Children.Add(b);
            scene.Children.Add(c);

            bool closed = scene.CloseGap(a);

            Assert.Multiple(() =>
            {
                Assert.That(closed, Is.False);
                Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(12)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseGap_AnchorCoveredByEarlierElement_ReturnsFalse()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            // The layer is continuously covered by [0s..100s], so selecting [10s..15s] must not
            // treat the space before [20s..25s] as a closeable gap.
            Element cover = CreateElement(basePath, TimeSpan.Zero, TimeSpan.FromSeconds(100));
            Element anchor = CreateElement(basePath, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
            Element next = CreateElement(basePath, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5));
            scene.Children.Add(cover);
            scene.Children.Add(anchor);
            scene.Children.Add(next);

            bool closed = scene.CloseGap(anchor);

            Assert.Multiple(() =>
            {
                Assert.That(closed, Is.False);
                Assert.That(next.Start, Is.EqualTo(TimeSpan.FromSeconds(20)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseGap_CoverageExtendsPastAnchor_UsesCoveredEnd()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            // [10s..15s] anchor, then [12s..50s] extends coverage to 50s, then [60s..70s]; the real
            // gap is 50s-60s, so the trailing clip must land at 50s, not be shifted by 45s.
            Element anchor = CreateElement(basePath, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
            Element mid = CreateElement(basePath, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(38));
            Element next = CreateElement(basePath, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10));
            scene.Children.Add(anchor);
            scene.Children.Add(mid);
            scene.Children.Add(next);

            bool closed = scene.CloseGap(anchor);

            Assert.Multiple(() =>
            {
                Assert.That(closed, Is.True);
                Assert.That(next.Start, Is.EqualTo(TimeSpan.FromSeconds(50)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseGap_AnchorNotInScene_ReturnsFalse()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element orphan = CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

            bool closed = scene.CloseGap(orphan);

            Assert.That(closed, Is.False);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseAllGaps_RemovesAllGapsAcrossZIndexes()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 0));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), zIndex: 0));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 1));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(1), zIndex: 1));

            int closed = scene.CloseAllGaps();

            Assert.Multiple(() =>
            {
                Assert.That(closed, Is.EqualTo(2));
                Assert.That(Gaps(scene), Is.Empty);
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseAllGaps_NoGaps_ReturnsZero()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2)));

            int closed = scene.CloseAllGaps();

            Assert.That(closed, Is.EqualTo(0));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseAllGaps_DoesNotCloseLeadingGap()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2)));

            int closed = scene.CloseAllGaps();

            Assert.Multiple(() =>
            {
                Assert.That(closed, Is.EqualTo(0));
                Assert.That(scene.Children[0].Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseAllGaps_ThreeElementsWithTwoGaps_BecomesContiguous()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element a = CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            Element b = CreateElement(basePath, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1));
            Element c = CreateElement(basePath, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1));
            scene.Children.Add(a);
            scene.Children.Add(b);
            scene.Children.Add(c);

            scene.CloseAllGaps();

            Assert.Multiple(() =>
            {
                Assert.That(a.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
                Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseAllGaps_NestedOverlap_ClosesOnlyRealGaps()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element a = CreateElement(basePath, TimeSpan.Zero, TimeSpan.FromSeconds(10));
            Element b = CreateElement(basePath, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
            Element c = CreateElement(basePath, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1));
            Element d = CreateElement(basePath, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1));
            scene.Children.Add(a);
            scene.Children.Add(b);
            scene.Children.Add(c);
            scene.Children.Add(d);

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
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void FindNextGapCenter_ReturnsNextGapCenter()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)));

            TimeSpan? center = scene.FindNextGapCenter(TimeSpan.FromSeconds(1));

            Assert.That(center, Is.EqualTo(TimeSpan.FromSeconds(3.5)));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void FindNextGapCenter_NoNextGap_ReturnsNull()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

            TimeSpan? center = scene.FindNextGapCenter(TimeSpan.FromSeconds(5));

            Assert.That(center, Is.Null);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void FindNextGapCenter_WhenInsideGap_SkipsCurrentGap()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)));

            TimeSpan? center = scene.FindNextGapCenter(TimeSpan.FromSeconds(4));

            Assert.That(center, Is.EqualTo(TimeSpan.FromSeconds(8)));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void FindPreviousGapCenter_ReturnsPreviousGapCenter()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)));

            TimeSpan? center = scene.FindPreviousGapCenter(TimeSpan.FromSeconds(6));

            Assert.That(center, Is.EqualTo(TimeSpan.FromSeconds(3.5)));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void FindPreviousGapCenter_NoPreviousGap_ReturnsNull()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

            TimeSpan? center = scene.FindPreviousGapCenter(TimeSpan.FromSeconds(1));

            Assert.That(center, Is.Null);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void FindPreviousGapCenter_OverlappingCrossLayerGaps_PicksGapEndingClosest()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            // The layer-1 gap (90s-91s) starts later than the layer-0 gap (80s-200s) but ends
            // far earlier; the previous gap must be chosen by end time, so the wide gap wins.
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(79)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(1)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(89), zIndex: 1));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(91), TimeSpan.FromSeconds(1), zIndex: 1));

            TimeSpan? center = scene.FindPreviousGapCenter(TimeSpan.FromSeconds(201));

            Assert.That(center, Is.EqualTo(TimeSpan.FromSeconds(140)));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void FindNextGapCenter_GapBeyondSearchEnd_Ignored()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            // Elements left beyond a shortened scene form a gap 10s-100s; bounding the search to a
            // 30s scene end must reject it so navigation reports no gap instead of clamping.
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(10)));

            var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(30));
            Assert.Multiple(() =>
            {
                Assert.That(scene.FindNextGapCenter(TimeSpan.Zero, range), Is.Null);
                Assert.That(scene.FindNextGapCenter(TimeSpan.Zero), Is.EqualTo(TimeSpan.FromSeconds(55)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void FindGapCenter_BoundsBothSides_IgnoresGapsOutsideOffsetScene()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            // Active scene range is 50s-150s. A gap 10s-40s sits entirely before it and a gap
            // 200s-260s entirely after it; neither may be selected from either direction.
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(80)));
            scene.Children.Add(CreateElement(basePath, TimeSpan.FromSeconds(260), TimeSpan.FromSeconds(10)));
            var range = new TimeRange(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(100));

            Assert.Multiple(() =>
            {
                // The only in-range gap is 100s-120s (center 110s).
                Assert.That(scene.FindNextGapCenter(TimeSpan.FromSeconds(50), range), Is.EqualTo(TimeSpan.FromSeconds(110)));
                Assert.That(scene.FindPreviousGapCenter(TimeSpan.FromSeconds(150), range), Is.EqualTo(TimeSpan.FromSeconds(110)));
                // Searching from outside the range must not reach the out-of-range gaps.
                Assert.That(scene.FindNextGapCenter(TimeSpan.FromSeconds(150), range), Is.Null);
                Assert.That(scene.FindPreviousGapCenter(TimeSpan.FromSeconds(50), range), Is.Null);
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CloseGap_ThenUndo_RestoresPositions()
    {
        using var harness = new SceneHistoryHarness("beutl_gap_undo", duration: TimeSpan.FromSeconds(60));
        Scene scene = harness.Scene;
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element b = harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        Element c = harness.AddElement(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(2));
        int beforeUndoCount = harness.History.UndoCount;

        bool closed = scene.CloseGap(a);
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
