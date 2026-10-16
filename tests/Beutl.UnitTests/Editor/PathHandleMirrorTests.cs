using Beutl.Editor.Components.PathEditorTab.Views;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class PathHandleMirrorTests
{
    // anchor, dragged handle, opposite handle, symmetric, expected opposite handle
    [TestCase(100f, 100f, 80f, 90f, 0f, 0f, true, 120f, 110f)]
    [TestCase(0f, 0f, -3f, -4f, 0f, 10f, false, 6f, 8f)]
    [TestCase(10f, 20f, 10f, 20f, 10f, 25f, false, 10f, 25f)]
    [TestCase(10f, 20f, 10f, 20f, 10f, 25f, true, 10f, 20f)]
    public void Mirror_puts_the_opposite_handle_through_the_anchor(
        float ax, float ay, float dx, float dy, float ox, float oy, bool symmetric, float ex, float ey)
    {
        BtlPoint result = PathPointDragBehavior.Mirror(new(ax, ay), new(dx, dy), new(ox, oy), symmetric);

        Assert.That(result, Is.EqualTo(new BtlPoint(ex, ey)));
    }
}
