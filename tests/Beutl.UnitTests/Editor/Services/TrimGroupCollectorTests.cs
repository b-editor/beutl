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
        _harness = new SceneHistoryHarness("beutl_trimcollect", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(30));
        _scene = _harness.Scene;
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    // --- CollectRollPairs ---

    [Test]
    public void CollectRollPairs_MemberEndingAtBoundary_PairsWithAdjacentBack()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

        IReadOnlyList<ElementTrimPair> pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [front], TimeSpan.FromSeconds(2));

        Assert.That(pairs, Is.EqualTo(new[] { new ElementTrimPair(front, back) }));
    }

    [Test]
    public void CollectRollPairs_MemberStartingAtBoundary_PairsWithAdjacentFront()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

        IReadOnlyList<ElementTrimPair> pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [back], TimeSpan.FromSeconds(2));

        Assert.That(pairs, Is.EqualTo(new[] { new ElementTrimPair(front, back) }));
    }

    [Test]
    public void CollectRollPairs_BothSidesAreMembers_CollectsPairOnce()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

        IReadOnlyList<ElementTrimPair> pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [front, back], TimeSpan.FromSeconds(2));

        Assert.That(pairs, Is.EqualTo(new[] { new ElementTrimPair(front, back) }));
    }

    [Test]
    public void CollectRollPairs_GroupedCutsAcrossLayers_CollectsEveryAlignedPair()
    {
        Element frontA = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 1);

        IReadOnlyList<ElementTrimPair> pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [frontA, frontB], TimeSpan.FromSeconds(2));

        Assert.That(pairs, Is.EquivalentTo(new[]
        {
            new ElementTrimPair(frontA, backA),
            new ElementTrimPair(frontB, backB)
        }));
    }

    [Test]
    public void CollectRollPairs_MemberNotTouchingBoundary_IsExcluded()
    {
        Element frontA = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 0);
        Element offBoundary = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(3), zIndex: 1);
        AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), zIndex: 1);

        IReadOnlyList<ElementTrimPair> pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [frontA, offBoundary], TimeSpan.FromSeconds(2));

        Assert.That(pairs, Is.EqualTo(new[] { new ElementTrimPair(frontA, backA) }));
    }

    [Test]
    public void CollectRollPairs_MemberWithoutAdjacentPartner_IsExcluded()
    {
        Element frontA = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 0);
        Element lonely = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 1);

        IReadOnlyList<ElementTrimPair> pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [frontA, lonely], TimeSpan.FromSeconds(2));

        Assert.That(pairs, Is.EqualTo(new[] { new ElementTrimPair(frontA, backA) }));
    }

    [Test]
    public void CollectRollPairs_LockedPartner_SkipsPair()
    {
        Element frontA = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 1);
        Element lockedBack = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 1);
        lockedBack.IsLocked = true;

        IReadOnlyList<ElementTrimPair> pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [frontA, frontB], TimeSpan.FromSeconds(2));

        Assert.That(pairs, Is.EqualTo(new[] { new ElementTrimPair(frontA, backA) }));
    }

    [Test]
    public void CollectRollPairs_OffSceneMember_IsExcluded()
    {
        var offScene = new Element { Start = TimeSpan.Zero, Length = TimeSpan.FromSeconds(2) };

        IReadOnlyList<ElementTrimPair> pairs = TrimGroupCollector.CollectRollPairs(
            _scene, [offScene], TimeSpan.FromSeconds(2));

        Assert.That(pairs, Is.Empty);
    }

    // --- CollectSlideLanes ---

    [Test]
    public void CollectSlideLanes_SingleMemberWithNeighbours_BuildsLane()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        Element back = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

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
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element firstMiddle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        Element secondMiddle = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1));
        Element back = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3));

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
        Element frontA = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element middleA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 0);
        Element backA = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1), zIndex: 1);
        Element middleB = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), zIndex: 1);

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
        AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element firstMiddle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        Element secondMiddle = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1));
        AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));

        IReadOnlyList<ElementSlideLane>? lanes = TrimGroupCollector.CollectSlideLanes(
            _scene, [firstMiddle, secondMiddle]);

        Assert.That(lanes, Is.Null);
    }

    [Test]
    public void CollectSlideLanes_MissingNeighbourOnAnyLayer_ReturnsNull()
    {
        AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element middleA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 0);
        AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), zIndex: 0);
        Element noNeighbours = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 1);

        IReadOnlyList<ElementSlideLane>? lanes = TrimGroupCollector.CollectSlideLanes(
            _scene, [middleA, noNeighbours]);

        Assert.That(lanes, Is.Null);
    }

    [Test]
    public void CollectSlideLanes_LockedNeighbour_ReturnsNull()
    {
        AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        Element back = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        back.IsLocked = true;

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
