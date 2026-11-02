using System.Collections;
using System.Collections.Immutable;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

public class SceneGroupsTests
{
    [Test]
    public void FindGroupFollowsEveryChangeToTheGroups()
    {
        var scene = new Scene();
        using var history = new HistoryHarness(scene);
        Guid[] ids = [.. Enumerable.Range(0, 5).Select(_ => Guid.NewGuid())];

        scene.Groups.Add([ids[0], ids[1]]);
        Assert.That(Positions(), Is.EqualTo(new[] { 0, 0, -1, -1, -1 }));
        scene.Groups.Insert(0, [ids[2], ids[3]]);
        Assert.That(Positions(), Is.EqualTo(new[] { 1, 1, 0, 0, -1 }));
        scene.Groups[1] = [ids[0], ids[4]];
        Assert.That(Positions(), Is.EqualTo(new[] { 1, -1, 0, 0, 1 }));
        scene.Groups.Move(0, 1);
        Assert.That(Positions(), Is.EqualTo(new[] { 0, -1, 1, 1, 0 }));
        scene.Groups.RemoveAt(0);
        Assert.That(Positions(), Is.EqualTo(new[] { -1, -1, 0, 0, -1 }));
        history.History.Commit("Edit groups");

        history.History.Undo();
        Assert.That(Positions(), Is.EqualTo(new[] { -1, -1, -1, -1, -1 }));
        history.History.Redo();
        Assert.That(Positions(), Is.EqualTo(new[] { -1, -1, 0, 0, -1 }));
        scene.Groups.Clear();
        Assert.That(Positions(), Is.EqualTo(new[] { -1, -1, -1, -1, -1 }));

        // Where each id's group is in the list, or -1 for none.
        int[] Positions() => [.. ids.Select(Position)];

        int Position(Guid id)
        {
            if (scene.FindGroup(id) is not { } group) return -1;
            // IndexOf compares the groups by reference, so a group that has left the list shows up as -1.
            int position = scene.Groups.IndexOf(group);
            Assert.That(position, Is.Not.Negative, "The lookup returned a group that is no longer in the list.");
            return position;
        }
    }

    [Test]
    public void FindGroupReturnsTheFirstOfTheGroupsHoldingAnElement()
    {
        var scene = new Scene();
        Guid shared = Guid.NewGuid();
        ImmutableHashSet<Guid> first = [shared, Guid.NewGuid()];
        scene.Groups.Add([Guid.NewGuid(), Guid.NewGuid()]);
        scene.Groups.Add(first);
        Assert.That(scene.FindGroup(shared), Is.SameAs(first));

        // Groups added while the lookup exists, sharing the element: after the first group, then before it.
        scene.Groups.Add([shared, Guid.NewGuid()]);
        Assert.That(scene.FindGroup(shared), Is.SameAs(first));
        ImmutableHashSet<Guid> earlier = [shared, Guid.NewGuid()];
        scene.Groups.Insert(0, earlier);
        Assert.That(scene.FindGroup(shared), Is.SameAs(earlier));
    }

    [Test]
    public void RemoveElementsFromGroupsPrunesEveryGroupHoldingAnIdAndReadsTheIdsOnce()
    {
        var scene = new Scene();
        Guid[] ids = [.. Enumerable.Range(0, 6).Select(_ => Guid.NewGuid())];
        ImmutableHashSet<Guid> untouched = [ids[4], ids[5]];
        scene.Groups.Add([ids[0], ids[1], ids[2]]);
        scene.Groups.Add(untouched);
        scene.Groups.Add([ids[1], ids[3]]);
        var removed = new CountingIds(ids[1], Guid.NewGuid());

        Assert.That(scene.RemoveElementsFromGroups(removed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Groups, Has.Count.EqualTo(2));
            Assert.That(scene.Groups[0], Is.EquivalentTo(new[] { ids[0], ids[2] }));
            Assert.That(scene.Groups[1], Is.SameAs(untouched));
            // Overlaps and Except walked the ids again for every group, costing groups × ids.
            Assert.That(removed.Enumerations, Is.EqualTo(1));
        });
        Assert.That(scene.RemoveElementsFromGroups(removed), Is.False);
    }

    private sealed class CountingIds(params Guid[] ids) : IReadOnlyCollection<Guid>
    {
        public int Enumerations { get; private set; }

        public int Count => ids.Length;

        public IEnumerator<Guid> GetEnumerator()
        {
            Enumerations++;
            return ((IEnumerable<Guid>)ids).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
