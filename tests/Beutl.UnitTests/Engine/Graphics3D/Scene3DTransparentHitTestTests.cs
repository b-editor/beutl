using System.Numerics;

using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics3D;

// With a transparent background the 2D content beneath a 3D scene shows through its empty areas, so only
// the scene's objects may answer clicks there; an opaque background still covers the whole scene.
[NonParallelizable]
[TestFixture]
public sealed class Scene3DTransparentHitTestTests
{
    private static readonly Rect s_sceneBounds = new(0, 0, 1920, 1080);

    [Test]
    public void TransparentBackground_AnswersOnlyOverObjects()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HitTest(Colors.Transparent, new Point(960, 540)), Is.True, "over the cube");
            Assert.That(HitTest(Colors.Transparent, new Point(40, 40)), Is.False, "over the empty background");
        });
    }

    [Test]
    public void OpaqueBackground_AnswersOverTheWholeScene()
    {
        Assert.That(HitTest(Colors.Black, new Point(40, 40)), Is.True);
    }

    private static bool HitTest(Color background, Point point)
    {
        var cube = new Cube3D();
        cube.Position.CurrentValue = Vector3.Zero;
        var scene = new Scene3D();
        scene.BackgroundColor.CurrentValue = background;
        scene.RenderWidth.CurrentValue = (float)s_sceneBounds.Width;
        scene.RenderHeight.CurrentValue = (float)s_sceneBounds.Height;
        scene.Objects.Add(cube);
        using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        using var node = new Scene3DRenderNode(resource);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = s_sceneBounds,
                CacheOptions = RenderCacheOptions.Disabled,
                Supports3DRendering = true,
            });
        return renderer.HitTest(point);
    }
}
