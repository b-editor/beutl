using Beutl.Collections.Pooled;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

public class SceneElementIndexTests
{
    [Test]
    public void QueriesMatchSceneOrderAcrossOverlapsEmptyRangesAndLayerDistributions()
    {
        var random = new Random(1947);
        var scene = new Scene();
        for (int i = 0; i < 2000; i++)
        {
            scene.Children.Add(new Element
            {
                Start = { Value = MediaTime.FromSeconds(random.Next(1000)) },
                Length = { Value = MediaTime.FromSeconds(random.Next(250)) },
                ZIndex = { Value = random.Next(20) },
            });
        }

        SceneElementIndex index = scene.ElementIndex;
        using var found = new PooledList<Element>();
        for (int i = 0; i < 200; i++)
        {
            Element changed = scene.Children[random.Next(scene.Children.Count)];
            changed.Start.Value = MediaTime.FromSeconds(random.Next(1000));
            changed.Length.Value = MediaTime.FromSeconds(random.Next(250));
            changed.ZIndex.Value = random.Next(20);
            var range = TimeRange.FromSeconds(random.Next(1100), random.Next(20));
            int? layer = i % 2 == 0 ? null : random.Next(20);
            found.Clear();
            index.Query(range.Start, range.End, found, layer);
            index.Sort(found);
            Element[] expected = scene.Children.Select((element, order) => (element, order))
                .Where(x => (!layer.HasValue || x.element.ZIndex.Value == layer)
                    && x.element.Start.Value <= range.End && x.element.Range.End >= range.Start)
                .OrderBy(x => x.element.ZIndex.Value).ThenByDescending(x => x.order)
                .Select(x => x.element).ToArray();
            Assert.That(found.ToArray(), Is.EqualTo(expected));
            if (layer.HasValue)
                Assert.That(index.Intersects(layer.Value, range),
                    Is.EqualTo(expected.Any(element => element.Range.Intersects(range))));
        }
    }

    [Test]
    public void EditingMovingRemovingAndUndoingUpdateTheSharedIndex()
    {
        var scene = new Scene();
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        scene.Children.Add(element);
        using var history = new TestInfrastructure.HistoryHarness(scene);
        SceneElementIndex original = scene.ElementIndex;
        Assert.That(scene.ElementIndex, Is.SameAs(original));

        element.Start.Value = MediaTime.FromSeconds(10);
        element.Length.Value = MediaTime.FromSeconds(2);
        element.ZIndex.Value = 3;
        history.History.Commit("Move");
        Assert.That(scene.ElementIndex, Is.SameAs(original));
        Assert.That(scene.ElementIndex.Intersects(0, TimeRange.FromSeconds(1)), Is.False);
        Assert.That(scene.ElementIndex.Intersects(3, TimeRange.FromSeconds(11, 1)), Is.True);
        history.History.Undo();
        Assert.That(scene.ElementIndex.Intersects(0, TimeRange.FromSeconds(1)), Is.True);

        scene.Children.Remove(element);
        history.History.Commit("Remove");
        Assert.That(scene.ElementIndex.Intersects(0, TimeRange.FromSeconds(1)), Is.False);
        history.History.Undo();
        Assert.That(scene.ElementIndex.Intersects(0, TimeRange.FromSeconds(1)), Is.True);
        scene.Children.Clear();
        Assert.That(scene.ElementIndex.Intersects(0, TimeRange.FromSeconds(1)), Is.False);
    }

    [Test]
    public void NeighborsUseNearestStartAndEndAndLastSceneOrderForTies()
    {
        var scene = new Scene();
        foreach ((int start, int length) in new[] { (1, 3), (1, 2), (4, 5), (7, 2), (0, 20) })
            scene.Children.Add(new Element
            {
                Start = { Value = MediaTime.FromSeconds(start) },
                Length = { Value = MediaTime.FromSeconds(length) },
            });
        var (before, after) = scene.ElementIndex.Neighbors(0, TimeRange.FromSeconds(3, 3));
        Assert.That(before, Is.SameAs(scene.Children[1]));
        Assert.That(after, Is.SameAs(scene.Children[3]));
    }
}
