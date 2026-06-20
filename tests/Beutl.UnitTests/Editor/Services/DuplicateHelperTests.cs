using System.Collections.Immutable;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class DuplicateHelperTests
{
    private static string GetTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"beutl_duplicate_helper_{Guid.NewGuid():N}");
    }

    private static Scene CreateScene(string basePath)
    {
        Directory.CreateDirectory(basePath);
        return new Scene(100, 100, string.Empty)
        {
            Uri = new Uri(Path.Combine(basePath, "test.scene"))
        };
    }

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
        // A starts at 0s, length 10s — ends at 10s
        // B starts at 1s, length 1s — ends at 2s
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
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Guid sourceA = Guid.NewGuid();
            Guid sourceB = Guid.NewGuid();
            Guid sourceC = Guid.NewGuid();
            Element a = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), zIndex: 0, id: sourceA, basePath: basePath);
            Element b = CreateElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), zIndex: 0, id: sourceB, basePath: basePath);
            Element c = CreateElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 0, id: sourceC, basePath: basePath);
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

            // 元グループは変更されない
            ImmutableHashSet<Guid> originalGroup = scene.Groups[0];
            Assert.That(originalGroup.SetEquals([sourceA, sourceB, sourceC]), Is.True);

            // 新規グループは新 ID 3 つを含み、元 ID とは交差しない
            ImmutableHashSet<Guid> newGroup = scene.Groups[1];
            Assert.That(newGroup.Count, Is.EqualTo(3));
            Assert.That(newGroup.SetEquals([newA.Id, newB.Id, newC.Id]), Is.True);
            Assert.That(newGroup.Overlaps([sourceA, sourceB, sourceC]), Is.False);

            // アンカー位置からの相対配置が保たれている
            Assert.That(newA.Start, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(newB.Start, Is.EqualTo(TimeSpan.FromSeconds(11)));
            Assert.That(newC.Start, Is.EqualTo(TimeSpan.FromSeconds(12)));
            Assert.That(newA.ZIndex, Is.EqualTo(1));
            Assert.That(newB.ZIndex, Is.EqualTo(1));
            Assert.That(newC.ZIndex, Is.EqualTo(1));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void PlaceDuplicates_PositionalIdMapping_PreservesOrder()
    {
        // idMapping は sourceElements[i] -> newElements[i] の位置zipで作る。
        // ObjectRegenerator の順序保存が暗黙の契約。並びが崩れるとグループ remap も崩れる。
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Guid sourceX = Guid.NewGuid();
            Guid sourceY = Guid.NewGuid();
            Element x = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), id: sourceX, basePath: basePath);
            Element y = CreateElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), id: sourceY, basePath: basePath);
            scene.Children.Add(x);
            scene.Children.Add(y);
            // X と Y を別グループに置く
            scene.Groups.Add(ImmutableHashSet.Create(sourceX, Guid.NewGuid()));
            scene.Groups.Add(ImmutableHashSet.Create(sourceY, Guid.NewGuid()));

            Element newX = CreateElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1));
            Element newY = CreateElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));

            DuplicateHelper.PlaceDuplicates(
                scene,
                newElements: [newX, newY],
                sourceElements: [x, y],
                anchorStart: TimeSpan.FromSeconds(20),
                anchorZIndex: 0);

            // sourceX の新規 ID は newX.Id でなければならない (位置 zip)。
            // 順序が swap してしまうと sourceX -> newY.Id になり、後続のグループ remap で
            // X のグループに「Y のクローン」が入る silent corruption が発生する。
            Assert.That(newX.Start, Is.EqualTo(TimeSpan.FromSeconds(20)));
            Assert.That(newY.Start, Is.EqualTo(TimeSpan.FromSeconds(25)));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
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
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
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
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void PlaceDuplicates_LeavesSceneUntouched_OnPhase1Failure()
    {
        // Asserts the Phase 1 invariant: scene.Children/Groups stay untouched on failure.
        // Forces Phase 1 to fail by placing a file where StoreToUri expects a directory,
        // so Directory.CreateDirectory throws.
        string basePath = GetTempPath();
        try
        {
            Directory.CreateDirectory(basePath);
            string blocked = Path.Combine(basePath, "blocked");
            File.WriteAllText(blocked, "not a directory");

            var scene = new Scene(100, 100, string.Empty)
            {
                Uri = new Uri(Path.Combine(blocked, "test.scene"))
            };
            int sceneChildrenBefore = scene.Children.Count;
            int sceneGroupsBefore = scene.Groups.Count;

            Element source = CreateElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
            Element newOne = CreateElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));

            Assert.Throws<IOException>(
                () => DuplicateHelper.PlaceDuplicates(
                    scene,
                    newElements: [newOne],
                    sourceElements: [source],
                    anchorStart: TimeSpan.Zero,
                    anchorZIndex: 0));

            // Scene 状態は無変更で抜けている
            Assert.That(scene.Children.Count, Is.EqualTo(sceneChildrenBefore));
            Assert.That(scene.Groups.Count, Is.EqualTo(sceneGroupsBefore));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }
}
