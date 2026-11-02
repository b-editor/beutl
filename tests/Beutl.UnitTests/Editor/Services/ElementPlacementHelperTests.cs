using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementPlacementHelperTests
{
    private static readonly HashSet<int> s_noLocks = [];
    private static readonly HashSet<int> s_oneRow = [0];

    private static Element Clip(double start, double length, int layer) => new()
    {
        Start = { Value = MediaTime.FromSeconds(start) },
        Length = { Value = MediaTime.FromSeconds(length) },
        ZIndex = { Value = layer },
    };

    private static MediaTime Frames(Scene scene, int frames)
        => new(SceneTimeRangeService.GetFrameDuration(scene).Flicks * frames);

    [Test]
    public void FindFreeAnchor_FreeClickedSlot_IsTheAnchor()
    {
        var scene = new Scene();
        scene.Children.Add(Clip(0, 1, 0));

        var anchor = ElementPlacementHelper.FindFreeAnchor(
            scene, TimeRange.FromSeconds(1), s_oneRow, s_noLocks, MediaTime.FromSeconds(1), 0);

        Assert.That(anchor, Is.EqualTo((MediaTime.FromSeconds(1), 0)));
    }

    [Test]
    public void FindFreeAnchor_OccupiedSlot_SpiralsToTheNearestFreeOne()
    {
        var scene = new Scene();
        scene.Children.Add(Clip(0, 10, 0));

        var anchor = ElementPlacementHelper.FindFreeAnchor(
            scene, TimeRange.FromSeconds(1), s_oneRow, s_noLocks, MediaTime.FromSeconds(5), 0);

        // One frame right is still on the clip; the turn down reaches the empty layer below.
        Assert.That(anchor, Is.EqualTo((MediaTime.FromSeconds(5) + Frames(scene, 1), 1)));
    }

    [Test]
    public void FindFreeAnchor_SparseRows_OnlySeeTheLayersTheyLandOn()
    {
        var scene = new Scene();
        scene.Children.Add(Clip(0, 10, 1));
        HashSet<int> sparse = [0, 2];

        Assert.That(ElementPlacementHelper.FindFreeAnchor(
                scene, TimeRange.FromSeconds(1), sparse, s_noLocks, MediaTime.FromSeconds(5), 0),
            Is.EqualTo((MediaTime.FromSeconds(5), 0)));

        scene.Children.Add(Clip(0, 10, 2));
        Assert.That(ElementPlacementHelper.FindFreeAnchor(
                scene, TimeRange.FromSeconds(1), sparse, s_noLocks, MediaTime.FromSeconds(5), 0),
            Is.Not.EqualTo((MediaTime.FromSeconds(5), 0)));
    }

    [Test]
    public void FindFreeAnchor_EmptyClipInsideTheSlot_OccupiesIt()
    {
        var scene = new Scene();
        scene.Children.Add(Clip(5.5, 0, 0));

        Assert.That(ElementPlacementHelper.FindFreeAnchor(
                scene, TimeRange.FromSeconds(1), s_oneRow, s_noLocks, MediaTime.FromSeconds(5), 0),
            Is.Not.EqualTo((MediaTime.FromSeconds(5), 0)));
    }

    [Test]
    public void FindFreeAnchor_ExhaustedSearch_DoesNotWalkTheElements()
    {
        var scene = new Scene();
        for (int i = 0; i < 100; i++) scene.Children.Add(Clip(i, 1, 0));
        HashSet<int> everyLayer = [.. Enumerable.Range(1, 2000)];
        _ = scene.ElementIndex;
        int walks = 0;
        ((ListProperty<Element>)scene.Children).BeforeAccess = () => walks++;

        var anchor = ElementPlacementHelper.FindFreeAnchor(
            scene, TimeRange.FromSeconds(1), s_oneRow, everyLayer, MediaTime.FromSeconds(50), 0);

        Assert.Multiple(() =>
        {
            Assert.That(anchor, Is.Null);
            Assert.That(walks, Is.Zero);
        });
    }

    [Test]
    public void AddChild_ZIndexHandling_TakesTheNearestFreeUnlockedLayerWithoutWalkingPerLayer()
    {
        var scene = new Scene();
        for (int layer = 0; layer < 50; layer++)
        {
            // Layer 30 is free but locked, and 40 is free.
            if (layer is not (30 or 40)) scene.Children.Add(Clip(0, 10, layer));
        }

        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 30 }, IsLocked = { Value = true } });
        _ = scene.ElementIndex;
        int walks = 0;
        ((ListProperty<Element>)scene.Children).BeforeAccess = () => walks++;

        Element added = Clip(5, 1, 25);
        scene.AddChild(added, ElementOverlapHandling.ZIndex);

        Assert.Multiple(() =>
        {
            Assert.That(added.ZIndex.Value, Is.EqualTo(40));
            Assert.That(walks, Is.LessThan(10));
        });
    }

    [Test]
    public void AddChild_AddingInABatch_KeepsTheIndex()
    {
        var scene = new Scene();
        scene.Children.Add(Clip(0, 1, 0));
        SceneElementIndex index = scene.ElementIndex;

        for (int i = 1; i < 50; i++) scene.AddChild(Clip(i, 1, 0));

        Assert.Multiple(() =>
        {
            Assert.That(scene.ElementIndex, Is.SameAs(index));
            Assert.That(scene.Children.Select(e => e.ZIndex.Value), Has.All.Zero);
            Assert.That(index.Intersects(0, TimeRange.FromSeconds(49, 1)), Is.True);
        });
    }
}
