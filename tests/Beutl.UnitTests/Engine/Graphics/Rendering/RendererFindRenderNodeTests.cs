using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

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

        (DrawableRenderNode? Rect, DrawableRenderNode[] Drawn) found =
            Find(renderer => (renderer.FindRenderNode(rect), DrawableNodesUnder(renderer, group)), group);

        Assert.That(found.Drawn, Has.Length.EqualTo(1), "The group draws its child into a node of the child's own.");
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

        (DrawableRenderNode? Inner, DrawableRenderNode? Rect, DrawableRenderNode[] Drawn) found = Find(
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
        rect.Width.CurrentValue = 100;
        rect.Height.CurrentValue = 60;
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

    // The nodes are disposed with the renderer, so the query walks them before it returns and only their identities
    // are compared afterwards.
    private static T Find<T>(Func<Renderer, T> query, Drawable drawable)
    {
        return RenderThread.Dispatcher.Invoke(() =>
        {
            using var renderer = new Renderer(
                Width,
                Height,
                RenderIntent.Preview,
                renderScale: 1,
                maxWorkingScale: float.PositiveInfinity,
                surface: new CpuRenderTarget(Width, Height));
            renderer.UpdateFrame(new CompositionFrame(
                [drawable.ToResource(CompositionContext.Default)],
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(Width, Height),
                null));
            return query(renderer);
        });
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
