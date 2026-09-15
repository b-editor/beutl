using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D;
using Beutl.Media;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

// A 3D scene exists only on the 3D backend. Without one the node must record nothing, so that the request
// describes what it can actually draw: no bounds, no value, and above all no hit - a scene that is never
// drawn must not answer clicks, and walking topmost-first it must not swallow the clicks meant for the 2D
// content beneath it. main answered this with an empty operation list; the fragment recorder lost it.
[NonParallelizable]
[TestFixture]
public sealed class Scene3DRenderNodeBackendAvailabilityTests
{
    private static readonly Rect s_sceneBounds = new(0, 0, 32, 24);
    private static readonly Point s_sceneCenter = new(16, 12);

    [Test]
    public void HitTest_WithoutA3DCapableBackend_ReportsNoHit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                HitTestSceneCenter(supports3DRendering: false),
                Is.False,
                "a scene the request cannot draw must not answer a hit over its bounds");
            Assert.That(
                HitTestSceneCenter(supports3DRendering: true),
                Is.True,
                "the control: with a backend the same point over the same scene is hit");
        });
    }

    [Test]
    public void Recording_WithoutA3DCapableBackend_PublishesNothing()
    {
        using RenderNodeMeasurementProbe without = MeasureScene(supports3DRendering: false);
        using RenderNodeMeasurementProbe with = MeasureScene(supports3DRendering: true);

        Assert.Multiple(() =>
        {
            Assert.That(without.Measurement.HasFragments, Is.False,
                "a scene the request cannot draw records no fragment at all");
            Assert.That(without.Measurement.OutputBounds.IsEmpty, Is.True);
            Assert.That(with.Measurement.HasFragments, Is.True,
                "the control: with a backend the scene records its value");
            Assert.That(with.Measurement.OutputBounds, Is.EqualTo(s_sceneBounds));
        });
    }

    [Test]
    public void HitTest_WithoutA3DCapableBackend_LeavesThe2DContentBeneathAnswering()
    {
        var rect = new Rect(0, 0, 8, 8);
        var insideRectOnly = new Point(4, 4);
        var insideSceneOnly = new Point(24, 16);
        using var fill = new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        using var root = new ContainerRenderNode();
        using var scene = CreateScene();
        // Children draw in order, so the scene is on top of the rectangle.
        root.AddChild(new RectangleRenderNode(rect, fill, null));
        root.AddChild(new Scene3DRenderNode(scene));
        using var renderer = new RenderNodeRenderer(root, CreateRequest(supports3DRendering: false));

        Assert.Multiple(() =>
        {
            Assert.That(renderer.HitTest(insideRectOnly), Is.True,
                "the 2D content beneath the undrawn scene still answers");
            Assert.That(renderer.HitTest(insideSceneOnly), Is.False,
                "where only the undrawn scene lies there is nothing to hit");
        });
    }

    [Test]
    public void HitTest_AfterTheSharedContextFailedToInitialize_ReportsNoHit()
    {
        // The production path: no request value is stated, so the renderer predicts from the process-wide
        // graphics state, which here records that building the shared context failed.
        bool afterFailure = HitTestSceneCenterUnder(
            new InstalledGraphics(null, null, null, FailedToInitialize: true),
            supports3DRendering: null);
        bool beforeAnyContext = HitTestSceneCenterUnder(
            new InstalledGraphics(null, null, null, FailedToInitialize: false),
            supports3DRendering: null);

        Assert.Multiple(() =>
        {
            Assert.That(afterFailure, Is.False,
                "once the shared context is known to be unbuildable the scene is never drawn, so never hit");
            Assert.That(beforeAnyContext, Is.True,
                "the control: before any context exists the first allocation builds one, so the scene records");
        });
    }

    [Test]
    public void HitTest_UnderAContextWithout3DRendering_ReportsNoHit()
    {
        var context = new Mock<IGraphicsContext>();
        context.SetupGet(static c => c.Supports3DRendering).Returns(false);

        bool result = HitTestSceneCenterUnder(
            new InstalledGraphics(context.Object, null, null, FailedToInitialize: false),
            supports3DRendering: null);

        Assert.That(result, Is.False, "an installed context that cannot render 3D leaves the scene undrawn");
    }

    private static bool HitTestSceneCenterUnder(InstalledGraphics graphics, bool? supports3DRendering)
    {
        InstalledGraphics previous = GraphicsContextFactory.ExchangeInstalledGraphics(graphics);
        try
        {
            return HitTestSceneCenter(supports3DRendering);
        }
        finally
        {
            GraphicsContextFactory.ExchangeInstalledGraphics(previous);
        }
    }

    private static bool HitTestSceneCenter(bool? supports3DRendering)
    {
        using var scene = CreateScene();
        using var node = new Scene3DRenderNode(scene);
        using var renderer = new RenderNodeRenderer(node, CreateRequest(supports3DRendering));
        return renderer.HitTest(s_sceneCenter);
    }

    private static RenderNodeMeasurementProbe MeasureScene(bool supports3DRendering)
    {
        Scene3D.Resource scene = CreateScene();
        var node = new Scene3DRenderNode(scene);
        var renderer = new RenderNodeRenderer(node, CreateRequest(supports3DRendering));
        return new RenderNodeMeasurementProbe(scene, node, renderer, renderer.Measure());
    }

    private static Scene3D.Resource CreateScene()
    {
        var scene = new Scene3D();
        scene.RenderWidth.CurrentValue = (float)s_sceneBounds.Width;
        scene.RenderHeight.CurrentValue = (float)s_sceneBounds.Height;
        return (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
    }

    private static RenderNodeRenderRequest CreateRequest(bool? supports3DRendering)
        => new()
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_sceneBounds,
            CacheOptions = RenderCacheOptions.Disabled,
            Supports3DRendering = supports3DRendering,
        };

    private sealed class RenderNodeMeasurementProbe(
        Scene3D.Resource scene,
        Scene3DRenderNode node,
        RenderNodeRenderer renderer,
        RenderNodeMeasurement measurement) : IDisposable
    {
        public RenderNodeMeasurement Measurement { get; } = measurement;

        public void Dispose()
        {
            renderer.Dispose();
            node.Dispose();
            scene.Dispose();
        }
    }
}
