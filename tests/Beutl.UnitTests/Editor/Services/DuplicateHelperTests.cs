using System.Collections.Immutable;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class DuplicateHelperTests
{
    private static Element CreateElement(TimeSpan start, TimeSpan length, int zIndex = 0, Guid? id = null, string? basePath = null)
    {
        var element = new Element
        {
            Start = start,
            Length = length,
            ZIndex = zIndex,
        };
        if (basePath is not null)
        {
            element.Uri = new Uri(Path.Combine(basePath, $"{Guid.NewGuid():N}.belm"));
        }
        if (id.HasValue) element.Id = id.Value;
        return element;
    }

    [Test]
    public void ComputePlacementRange_UsesRangeEnd_NotMaxStart()
    {
        // The placement search seed range should span A's full duration (10s),
        // not just maxStart-minStart (1s). Otherwise the spiral search picks
        // a "next slot" that overlaps A.
        Element a = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(10));
        Element b = CreateElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        var (range, minZ, maxZ) = DuplicateHelper.ComputePlacementRange([a, b]);

        Assert.That(range.Start, Is.EqualTo(TimeSpan.FromSeconds(0)));
        Assert.That(range.Duration, Is.EqualTo(TimeSpan.FromSeconds(10)));
        Assert.That(minZ, Is.EqualTo(0));
        Assert.That(maxZ, Is.EqualTo(0));
    }

    [Test]
    public void ExpandWithGroupSiblings_IncludesAllMembers_WhenOneIsSelected()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Guid c = Guid.NewGuid();
        Guid unrelated = Guid.NewGuid();
        var group = ImmutableHashSet.Create(a, b, c);

        HashSet<Guid> expanded = DuplicateHelper.ExpandWithGroupSiblings(
            seedIds: [a],
            groups: [group, ImmutableHashSet.Create(unrelated)]);

        Assert.That(expanded.Count, Is.EqualTo(3));
        Assert.That(expanded.Contains(a), Is.True);
        Assert.That(expanded.Contains(b), Is.True);
        Assert.That(expanded.Contains(c), Is.True);
        Assert.That(expanded.Contains(unrelated), Is.False);
    }

    [Test]
    public void ExpandWithGroupSiblings_LeavesSeedUntouched_WhenNoGroupOverlaps()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        var otherGroup = ImmutableHashSet.Create(Guid.NewGuid(), Guid.NewGuid());

        HashSet<Guid> expanded = DuplicateHelper.ExpandWithGroupSiblings(
            seedIds: [a, b],
            groups: [otherGroup]);

        Assert.That(expanded.Count, Is.EqualTo(2));
        Assert.That(expanded.Contains(a), Is.True);
        Assert.That(expanded.Contains(b), Is.True);
    }

    [Test]
    public void PlaceDuplicates_RemapsGroupsToNewIds_PreservingMembership()
    {
        using var harness = new SceneHistoryHarness("beutl_duplicate_helper");
        Scene scene = harness.Scene;
        Guid sourceA = Guid.NewGuid();
        Guid sourceB = Guid.NewGuid();
        Guid sourceC = Guid.NewGuid();
        Element a = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), zIndex: 0, id: sourceA, basePath: harness.BasePath);
        Element b = CreateElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 0, id: sourceB, basePath: harness.BasePath);
        Element c = CreateElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 0, id: sourceC, basePath: harness.BasePath);
        scene.Children.Add(a);
        scene.Children.Add(b);
        scene.Children.Add(c);
        scene.Groups.Add(ImmutableHashSet.Create(sourceA, sourceB, sourceC));

        // 新規要素は正規化済みの位置を持つ前提 (ObjectRegenerator が新しい Id を振った状態)
        Element newA = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), zIndex: 0);
        Element newB = CreateElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 0);
        Element newC = CreateElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 0);

        DuplicateHelper.PlaceDuplicates(
            scene,
            newElements: [newA, newB, newC],
            sourceElements: [a, b, c],
            anchorStart: TimeSpan.FromSeconds(10),
            anchorZIndex: 1);

        Assert.That(scene.Children.Count, Is.EqualTo(6));
        Assert.That(scene.Groups.Count, Is.EqualTo(2));

        ImmutableHashSet<Guid> originalGroup = scene.Groups[0];
        Assert.That(originalGroup.SetEquals([sourceA, sourceB, sourceC]), Is.True);

        ImmutableHashSet<Guid> newGroup = scene.Groups[1];
        Assert.That(newGroup.Count, Is.EqualTo(3));
        Assert.That(newGroup.SetEquals([newA.Id, newB.Id, newC.Id]), Is.True);
        Assert.That(newGroup.Overlaps([sourceA, sourceB, sourceC]), Is.False);

        Assert.That(newA.Start, Is.EqualTo(TimeSpan.FromSeconds(10)));
        Assert.That(newB.Start, Is.EqualTo(TimeSpan.FromSeconds(11)));
        Assert.That(newC.Start, Is.EqualTo(TimeSpan.FromSeconds(12)));
        Assert.That(newA.ZIndex, Is.EqualTo(1));
        Assert.That(newB.ZIndex, Is.EqualTo(1));
        Assert.That(newC.ZIndex, Is.EqualTo(1));
        // The next save of the scene writes the files.
        Assert.That(newA.Uri, Is.EqualTo(ElementFileNaming.GetUri(scene.Uri!, newA.Id)));
        Assert.That(File.Exists(newA.Uri!.LocalPath), Is.False);
    }

    [Test]
    public void PlaceDuplicates_PositionalIdMapping_PreservesOrder()
    {
        // idMapping zips sourceElements[i] with newElements[i], so each new group holds the clones of one
        // source group only while the two arrays keep the same order.
        using var harness = new SceneHistoryHarness("beutl_duplicate_helper");
        Scene scene = harness.Scene;
        Guid sourceX = Guid.NewGuid();
        Guid sourceY = Guid.NewGuid();
        Guid sourceZ = Guid.NewGuid();
        Guid sourceW = Guid.NewGuid();
        Element x = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), id: sourceX, basePath: harness.BasePath);
        Element y = CreateElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), id: sourceY, basePath: harness.BasePath);
        Element z = CreateElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), id: sourceZ, basePath: harness.BasePath);
        Element w = CreateElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), id: sourceW, basePath: harness.BasePath);
        scene.Children.Add(x);
        scene.Children.Add(y);
        scene.Children.Add(z);
        scene.Children.Add(w);
        scene.Groups.Add(ImmutableHashSet.Create(sourceX, sourceY));
        scene.Groups.Add(ImmutableHashSet.Create(sourceZ, sourceW));

        Element newX = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1));
        Element newY = CreateElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Element newZ = CreateElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        Element newW = CreateElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1));

        DuplicateHelper.PlaceDuplicates(
            scene,
            newElements: [newX, newY, newZ, newW],
            sourceElements: [x, y, z, w],
            anchorStart: TimeSpan.FromSeconds(20),
            anchorZIndex: 0);

        Assert.That(scene.Groups.Count, Is.EqualTo(4));
        Assert.That(scene.Groups[2].SetEquals([newX.Id, newY.Id]), Is.True);
        Assert.That(scene.Groups[3].SetEquals([newZ.Id, newW.Id]), Is.True);
    }

    [Test]
    public void WouldOverlapSources_ReturnsTrue_WhenAnchorLandsInsideSource()
    {
        // Single 5s clip at t=0..5 on layer 0. Anchor at t=3 → new range 3..8 still
        // overlaps the original on the same layer.
        Element a = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5), zIndex: 0);

        bool result = DuplicateHelper.WouldOverlapSources([a], TimeSpan.FromSeconds(3), anchorZIndex: 0);

        Assert.That(result, Is.True);
    }

    [Test]
    public void WouldOverlapSources_ReturnsFalse_WhenAnchorIsExactlyAtSourceEnd()
    {
        // [0,5) and [5,10) on layer 0 are adjacent but not intersecting.
        Element a = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5), zIndex: 0);

        bool result = DuplicateHelper.WouldOverlapSources([a], TimeSpan.FromSeconds(5), anchorZIndex: 0);

        Assert.That(result, Is.False);
    }

    [Test]
    public void WouldOverlapSources_ReturnsFalse_WhenLayerDiffers()
    {
        // Same time range but different ZIndex — no overlap.
        Element a = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5), zIndex: 0);

        bool result = DuplicateHelper.WouldOverlapSources([a], TimeSpan.FromSeconds(0), anchorZIndex: 1);

        Assert.That(result, Is.False);
    }

    [Test]
    public void WouldOverlapSources_ReturnsTrue_WhenAnyMemberOverlaps()
    {
        // a: [0,2), b: [10,12) on layer 0. Shift by +1 → new a: [1,3), new b: [11,13).
        // new a overlaps source a.
        Element a = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), zIndex: 0);
        Element b = CreateElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), zIndex: 0);

        bool result = DuplicateHelper.WouldOverlapSources([a, b], TimeSpan.FromSeconds(1), anchorZIndex: 0);

        Assert.That(result, Is.True);
    }

    [Test]
    public void WouldOverlapSources_ReturnsFalse_WhenAllNewRangesClearSources()
    {
        // a: [0,2), b: [4,6) on layer 0. Anchor at t=10 → new a: [10,12), new b: [14,16).
        // Neither intersects any source on the same layer.
        Element a = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), zIndex: 0);
        Element b = CreateElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), zIndex: 0);

        bool result = DuplicateHelper.WouldOverlapSources([a, b], TimeSpan.FromSeconds(10), anchorZIndex: 0);

        Assert.That(result, Is.False);
    }

    [Test]
    public void WouldOverlapSources_ReturnsFalse_OnEmptyInput()
    {
        Assert.That(
            DuplicateHelper.WouldOverlapSources([], TimeSpan.Zero, anchorZIndex: 0), Is.False);
    }

    [Test]
    public void PlaceDuplicates_ThrowsInvalidOperation_WhenSceneUriIsNull()
    {
        var scene = new Scene(100, 100, string.Empty);
        Assert.That(scene.Uri, Is.Null);

        Element source = CreateElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Element newOne = CreateElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => DuplicateHelper.PlaceDuplicates(
                scene,
                newElements: [newOne],
                sourceElements: [source],
                anchorStart: TimeSpan.Zero,
                anchorZIndex: 0));
    }

    [Test]
    public void PlaceDuplicates_ThrowsArgumentException_WhenLengthsMismatch()
    {
        // Positional zip mapping breaking silently would corrupt group remap.
        var scene = new Scene(100, 100, string.Empty);
        Element source = CreateElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Element newA = CreateElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Element newB = CreateElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Assert.Throws<ArgumentException>(
            () => DuplicateHelper.PlaceDuplicates(
                scene,
                newElements: [newA, newB],
                sourceElements: [source],
                anchorStart: TimeSpan.Zero,
                anchorZIndex: 0));
    }
}
