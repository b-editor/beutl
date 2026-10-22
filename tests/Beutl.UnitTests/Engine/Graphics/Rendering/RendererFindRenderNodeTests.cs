using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using static Beutl.UnitTests.Engine.Graphics.Rendering.RendererTestSupport;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// A drawable drawn inside another one, such as a group's child or a clip an active transition draws, has no entry
// of its own. The preview's 3D camera control finds the node of a scene drawn that way in the nodes of the drawable
// that draws it.
[NonParallelizable]
[TestFixture]
public class RendererFindRenderNodeTests
{
    private const int Width = 320;
    private const int Height = 240;

    [Test]
    public void FindRenderNode_FindsTheNodeAGroupDrawsItsChildInto()
    {
        RectShape rect = CreateRect();
        var group = new DrawableGroup();
        group.Children.Add(rect);

        (DrawableRenderNode? Rect, DrawableRenderNode[] Drawn) found = QueryFrame(
            Width,
            Height,
            renderer => (renderer.FindRenderNode(rect), DrawableNodesUnder(renderer, group)),
            group);

        Assert.That(found.Drawn, Has.Length.EqualTo(1), "The child is drawn into a drawable node of its own.");
        Assert.That(found.Rect, Is.SameAs(found.Drawn[0]));
    }

    // The inner group's node is not the one asked for, but the nodes it holds are searched too.
    [Test]
    public void FindRenderNode_SearchesTheNodesANestedGroupDraws()
    {
        RectShape rect = CreateRect();
        var inner = new DrawableGroup();
        inner.Children.Add(rect);
        var outer = new DrawableGroup();
        outer.Children.Add(inner);

        (DrawableRenderNode? Inner, DrawableRenderNode? Rect, DrawableRenderNode[] Drawn) found = QueryFrame(
            Width,
            Height,
            renderer => (renderer.FindRenderNode(inner), renderer.FindRenderNode(rect), DrawableNodesUnder(renderer, outer)),
            outer);

        Assert.That(found.Drawn, Has.Length.EqualTo(2), "The outer group draws the inner one, which draws the rectangle.");
        Assert.Multiple(() =>
        {
            Assert.That(found.Inner, Is.SameAs(found.Drawn[0]));
            Assert.That(found.Rect, Is.SameAs(found.Drawn[1]));
        });
    }

    private static RectShape CreateRect()
    {
        var rect = new RectShape();
        rect.Width.Value = 100;
        rect.Height.Value = 60;
        return rect;
    }

    // The drawable nodes inside the node of a drawable drawn on its own, outermost first.
    private static DrawableRenderNode[] DrawableNodesUnder(Renderer renderer, Drawable drawable)
    {
        DrawableRenderNode root = renderer.FindRenderNode(drawable)
            ?? throw new InvalidOperationException("The drawable was never recorded.");
        var nodes = new List<DrawableRenderNode>();
        Collect(root);
        return [.. nodes];

        void Collect(ContainerRenderNode container)
        {
            foreach (RenderNode child in container.Children)
            {
                if (child is DrawableRenderNode drawableNode)
                {
                    nodes.Add(drawableNode);
                }

                if (child is ContainerRenderNode inner)
                {
                    Collect(inner);
                }
            }
        }
    }
}
