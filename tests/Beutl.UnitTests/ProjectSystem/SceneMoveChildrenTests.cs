using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneMoveChildrenTests
{
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
