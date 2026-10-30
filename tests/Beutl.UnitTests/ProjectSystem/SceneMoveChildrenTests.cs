using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneMoveChildrenTests
{
    [TestCase(2, false)]
    [TestCase(200, false)]
    [TestCase(20000, false)]
    [TestCase(2, true)]
    public void MoveTenThousandOfTwentyThousandElementsAndUndo(int layers, bool withTransition)
    {
        var scene = new Scene();
        var elements = Enumerable.Range(0, 20000).Select(i => new Element
        {
            Start = { Value = MediaTime.FromSeconds(i / layers * 4) },
            Length = { Value = MediaTime.FromSeconds(1) },
            ZIndex = { Value = i % layers },
        }).ToArray();
        scene.Children.AddRange(elements);
        if (withTransition)
            elements[0].ExitTransition.Value = new Beutl.Graphics.Transitions.CrossDissolveTransition
            {
                Duration = { Value = MediaTime.FromSeconds(0.5) },
            };
        _ = scene.ElementIndex;
        Element[] selected = elements.Take(10000).ToArray();
        using var harness = new HistoryHarness(scene);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        scene.MoveChildren(0, MediaTime.FromSeconds(1), selected);
        harness.History.Commit("Move 10000 clips");
        TestContext.Out.WriteLine($"{layers} layers (transition={withTransition}): move 10000/20000 + history {watch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.That(selected.Select((element, i) => element.Start.Value == MediaTime.FromSeconds(i / layers * 4 + 1)), Has.All.True);
        Assert.That(elements.Skip(10000).Select((element, i) => element.Start.Value == MediaTime.FromSeconds((i + 10000) / layers * 4)), Has.All.True);
        watch.Restart();
        harness.History.Undo();
        TestContext.Out.WriteLine($"{layers} layers: undo 10000/20000 {watch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.That(elements.Select((element, i) => element.Start.Value == MediaTime.FromSeconds(i / layers * 4)), Has.All.True);
    }

    [Test]
    public void BulkConflictCorrectionPreservesOffsetsAndRefusesAnImpossibleDestination()
    {
        using var harness = new SceneHistoryHarness("beutl_bulk_conflict");
        Element first = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1));
        Element second = harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1));
        harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), 1);
        harness.Scene.MoveChildren(1, MediaTime.FromSeconds(2), [first, second]);
        Assert.That(first.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
        Assert.That(second.Start.Value, Is.EqualTo(MediaTime.FromSeconds(8)));
        Assert.That(first.ZIndex.Value, Is.EqualTo(1));

        harness.Scene.MoveChildren(-2, MediaTime.Zero, [first, second]);
        Assert.That(first.ZIndex.Value, Is.EqualTo(1));
        Assert.That(first.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
    }

    [Test]
    public void MoveChildren_SingleElement_ShiftsStartByDelta()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_movechildren");
        Scene scene = harness.Scene;
        Element element = harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));

        scene.MoveChildren(0, MediaTime.FromSeconds(1), [element]);

        Assert.That(element.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
        Assert.That(element.ZIndex.Value, Is.EqualTo(0));
    }

    [Test]
    public void MoveChildren_EmptyArray_Throws()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_movechildren");
        Scene scene = harness.Scene;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scene.MoveChildren(0, MediaTime.FromSeconds(1), []));
    }

    [Test]
    public void MoveChildren_NonZeroDeltaIndex_ShiftsZIndex()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_movechildren");
        Scene scene = harness.Scene;
        Element element = harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 1);

        scene.MoveChildren(+2, MediaTime.Zero, [element]);

        Assert.That(element.ZIndex.Value, Is.EqualTo(3));
        Assert.That(element.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void MoveChildren_ConflictingShift_LeavesElementUnchanged()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_movechildren");
        Scene scene = harness.Scene;
        Element movable = harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 0);
        Element obstacle = harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 1);

        // movable を obstacle の ZIndex に重ねようとすると衝突する。
        // 時間方向の自動補正は効かないので no-op になる契約。
        scene.MoveChildren(+1, MediaTime.Zero, [movable]);

        Assert.That(movable.ZIndex.Value, Is.EqualTo(0));
        Assert.That(movable.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void MoveChildren_MultipleElements_AllShiftTogether()
    {
        using var harness = new SceneHistoryHarness("beutl_scene_movechildren");
        Scene scene = harness.Scene;
        Element a = harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 0);
        Element b = harness.AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(1), zIndex: 1);

        scene.MoveChildren(0, MediaTime.FromSeconds(1), [a, b]);

        Assert.That(a.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
        Assert.That(b.Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
    }
}
