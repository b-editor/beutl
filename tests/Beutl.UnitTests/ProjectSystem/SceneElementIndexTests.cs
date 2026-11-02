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
    public void AppendingAndRemovingKeepTheIndexInPlace()
    {
        var scene = new Scene();
        Element first = Clip(0, 0);
        Element second = Clip(0, 0);
        scene.Children.AddRange([first, second]);
        SceneElementIndex index = scene.ElementIndex;

        Element added = Clip(0, 0);
        scene.Children.Add(added);
        scene.AddChild(Clip(10, 0), ElementOverlapHandling.Allow);
        scene.Children.Remove(second);

        Assert.Multiple(() =>
        {
            Assert.That(scene.ElementIndex, Is.SameAs(index));
            Assert.That(index.CompareSceneOrder(first, added), Is.Negative);
            Assert.That(index.Intersects(0, TimeRange.FromSeconds(10, 1)), Is.True);
        });

        // The trees know an added element's later edits.
        added.Start.Value = MediaTime.FromSeconds(50);
        Assert.That(index.Intersects(0, TimeRange.FromSeconds(50, 1)), Is.True);
    }

    [Test]
    public void StaysInStepWithTheSceneThroughEveryListChange([Range(0, 19)] int seed)
    {
        var random = new Random(seed);
        var scene = new Scene();
        _ = scene.ElementIndex;
        for (int step = 0; step < 200; step++)
        {
            int count = scene.Children.Count;
            switch (random.Next(9))
            {
                case 0 or 1 or 2:
                    scene.Children.Add(RandomClip(random));
                    break;
                case 3:
                    scene.Children.AddRange([RandomClip(random), RandomClip(random)]);
                    break;
                case 4:
                    scene.Children.Insert(random.Next(count + 1), RandomClip(random));
                    break;
                case 5 when count > 0:
                    scene.Children.RemoveAt(random.Next(count));
                    break;
                case 6 when count > 0:
                    scene.Children.Move(random.Next(count), random.Next(count));
                    break;
                case 7 when count > 0:
                    scene.Children[random.Next(count)] = RandomClip(random);
                    break;
                case 8 when count > 0:
                    Element edited = scene.Children[random.Next(count)];
                    edited.Start.Value = MediaTime.FromSeconds(random.Next(100));
                    edited.ZIndex.Value = random.Next(4);
                    break;
            }

            AssertMatchesScene(scene, random);
        }

        scene.Children.Clear();
        AssertMatchesScene(scene, random);
    }

    private static void AssertMatchesScene(Scene scene, Random random)
    {
        SceneElementIndex index = scene.ElementIndex;
        Element[] children = [.. scene.Children];
        int layer = random.Next(4);
        var range = TimeRange.FromSeconds(random.Next(100), random.Next(20));

        using var found = new PooledList<Element>();
        index.Query(range.Start, range.End, found, layer);
        index.Sort(found);
        Element[] expected = [.. children.Select((element, order) => (element, order))
            .Where(x => x.element.ZIndex.Value == layer && x.element.Start.Value <= range.End && x.element.Range.End >= range.Start)
            .OrderByDescending(x => x.order)
            .Select(x => x.element)];
        Assert.That(found.ToArray(), Is.EqualTo(expected));

        Element[] onLayer = [.. children.Where(element => element.ZIndex.Value == layer)];
        Assert.That(index.Intersects(layer, range), Is.EqualTo(onLayer.Any(element => element.Range.Intersects(range))));
        Assert.That(index.Overlaps(layer, range), Is.EqualTo(onLayer.Any(element =>
        {
            TimeRange other = element.Range;
            return other == range || other.Intersects(range) || other.Contains(range) || range.Contains(other);
        })));

        if (children.Length > 1)
        {
            int left = random.Next(children.Length);
            int right = random.Next(children.Length);
            Assert.That(Math.Sign(index.CompareSceneOrder(children[left], children[right])), Is.EqualTo(Math.Sign(left - right)));
        }
    }

    private static Element Clip(double start, int layer) => new()
    {
        Start = { Value = MediaTime.FromSeconds(start) },
        Length = { Value = MediaTime.FromSeconds(1) },
        ZIndex = { Value = layer },
    };

    private static Element RandomClip(Random random) => new()
    {
        Start = { Value = MediaTime.FromSeconds(random.Next(100)) },
        Length = { Value = MediaTime.FromSeconds(random.Next(25)) },
        ZIndex = { Value = random.Next(4) },
    };

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
