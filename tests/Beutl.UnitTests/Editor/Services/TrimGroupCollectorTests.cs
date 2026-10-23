using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class TrimGroupCollectorTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_trimcollect", start: MediaTime.Zero, duration: MediaTime.FromSeconds(30));
        _scene = _harness.Scene;
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(MediaTime start, MediaTime length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    // --- CollectRollPairs ---

    [Test]
    public void CollectRollPairs_MemberEndingAtBoundary_PairsWithAdjacentBack()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));

        IReadOnlyList<ElementTrimPair>? pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [front], MediaTime.FromSeconds(2));

        Assert.That(pairs, Is.EqualTo(new[] { new ElementTrimPair(front, back) }));
    }

    [Test]
    public void CollectRollPairs_MemberStartingAtBoundary_PairsWithAdjacentFront()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));

        IReadOnlyList<ElementTrimPair>? pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [back], MediaTime.FromSeconds(2));

        Assert.That(pairs, Is.EqualTo(new[] { new ElementTrimPair(front, back) }));
    }

    [Test]
    public void CollectRollPairs_BothSidesAreMembers_CollectsPairOnce()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));

        IReadOnlyList<ElementTrimPair>? pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [front, back], MediaTime.FromSeconds(2));

        Assert.That(pairs, Is.EqualTo(new[] { new ElementTrimPair(front, back) }));
    }

    [Test]
    public void CollectRollPairs_GroupedCutsAcrossLayers_CollectsEveryAlignedPair()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 1);

        IReadOnlyList<ElementTrimPair>? pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [frontA, frontB], MediaTime.FromSeconds(2));

        Assert.That(pairs, Is.EquivalentTo(new[]
        {
            new ElementTrimPair(frontA, backA),
            new ElementTrimPair(frontB, backB)
        }));
    }

    [Test]
    public void CollectRollPairs_MemberNotTouchingBoundary_IsExcluded()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 0);
        Element offBoundary = AddElement(MediaTime.Zero, MediaTime.FromSeconds(3), zIndex: 1);
        AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2), zIndex: 1);

        IReadOnlyList<ElementTrimPair>? pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [frontA, offBoundary], MediaTime.FromSeconds(2));

        Assert.That(pairs, Is.EqualTo(new[] { new ElementTrimPair(frontA, backA) }));
    }

    [Test]
    public void CollectRollPairs_MemberWithoutAdjacentPartner_IsExcluded()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 0);
        Element lonely = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 1);

        IReadOnlyList<ElementTrimPair>? pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [frontA, lonely], MediaTime.FromSeconds(2));

        Assert.That(pairs, Is.EqualTo(new[] { new ElementTrimPair(frontA, backA) }));
    }

    [Test]
    public void CollectRollPairs_LockedPartner_ReturnsNull()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 1);
        Element lockedBack = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 1);
        lockedBack.IsLocked.Value = true;

        IReadOnlyList<ElementTrimPair>? pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [frontA, frontB], MediaTime.FromSeconds(2));

        Assert.That(pairs, Is.Null);
    }

    [Test]
    public void CollectRollPairs_LockedMemberAtBoundary_ReturnsNull()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 0);
        Element lockedFront = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 1);
        AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 1);
        lockedFront.IsLocked.Value = true;

        IReadOnlyList<ElementTrimPair>? pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [frontA, lockedFront], MediaTime.FromSeconds(2));

        Assert.That(pairs, Is.Null);
    }

    [Test]
    public void CollectRollPairs_OffSceneMember_IsExcluded()
    {
        var offScene = new Element { Start = { Value = MediaTime.Zero }, Length = { Value = MediaTime.FromSeconds(2) } };

        IReadOnlyList<ElementTrimPair>? pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [offScene], MediaTime.FromSeconds(2));

        Assert.That(pairs, Is.Empty);
    }

    // --- CollectSlideLanes ---

    [Test]
    public void CollectSlideLanes_SingleMemberWithNeighbours_BuildsLane()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        Element back = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3));

        IReadOnlyList<ElementSlideLane>? lanes = TrimGroupCollector.CollectSlideLanes(_scene, [middle]);

        Assert.That(lanes, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(lanes, Has.Count.EqualTo(1));
            Assert.That(lanes![0].Front, Is.SameAs(front));
            Assert.That(lanes[0].Middles, Is.EqualTo(new[] { middle }));
            Assert.That(lanes[0].Back, Is.SameAs(back));
        });
    }

    [Test]
    public void CollectSlideLanes_ContiguousMembersOnOneLayer_FormOneLaneOrderedByStart()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element firstMiddle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        Element secondMiddle = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(1));
        Element back = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(3));

        IReadOnlyList<ElementSlideLane>? lanes = TrimGroupCollector.CollectSlideLanes(
            _scene, [secondMiddle, firstMiddle]);

        Assert.That(lanes, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(lanes![0].Front, Is.SameAs(front));
            Assert.That(lanes[0].Middles, Is.EqualTo(new[] { firstMiddle, secondMiddle }));
            Assert.That(lanes[0].Back, Is.SameAs(back));
        });
    }

    [Test]
    public void CollectSlideLanes_MembersAcrossLayers_BuildOneLanePerLayer()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element middleA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(MediaTime.Zero, MediaTime.FromSeconds(1), zIndex: 1);
        Element middleB = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2), zIndex: 1);

        IReadOnlyList<ElementSlideLane>? lanes = TrimGroupCollector.CollectSlideLanes(
            _scene, [middleA, middleB]);

        Assert.That(lanes, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(lanes, Has.Count.EqualTo(2));
            Assert.That(lanes![0].Front, Is.SameAs(frontA));
            Assert.That(lanes[0].Back, Is.SameAs(backA));
            Assert.That(lanes[1].Front, Is.SameAs(frontB));
            Assert.That(lanes[1].Back, Is.SameAs(backB));
        });
    }

    [Test]
    public void CollectSlideLanes_NonContiguousMembers_ReturnsNull()
    {
        AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element firstMiddle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        Element secondMiddle = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(1));
        AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(3));

        IReadOnlyList<ElementSlideLane>? lanes = TrimGroupCollector.CollectSlideLanes(
            _scene, [firstMiddle, secondMiddle]);

        Assert.That(lanes, Is.Null);
    }

    [Test]
    public void CollectSlideLanes_MissingNeighbourOnAnyLayer_ReturnsNull()
    {
        AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element middleA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 0);
        AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3), zIndex: 0);
        Element noNeighbours = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 1);

        IReadOnlyList<ElementSlideLane>? lanes = TrimGroupCollector.CollectSlideLanes(
            _scene, [middleA, noNeighbours]);

        Assert.That(lanes, Is.Null);
    }

    [Test]
    public void CollectSlideLanes_LockedMember_ReturnsNull()
    {
        AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3));
        middle.IsLocked.Value = true;

        IReadOnlyList<ElementSlideLane>? lanes = TrimGroupCollector.CollectSlideLanes(_scene, [middle]);

        Assert.That(lanes, Is.Null);
    }

    [Test]
    public void CollectSlideLanes_LockedNeighbour_ReturnsNull()
    {
        AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        Element back = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3));
        back.IsLocked.Value = true;

        IReadOnlyList<ElementSlideLane>? lanes = TrimGroupCollector.CollectSlideLanes(_scene, [middle]);

        Assert.That(lanes, Is.Null);
    }

    [Test]
    public void CollectSlideLanes_EmptyMembers_ReturnsNull()
    {
        IReadOnlyList<ElementSlideLane>? lanes = TrimGroupCollector.CollectSlideLanes(_scene, []);

        Assert.That(lanes, Is.Null);
    }
}
