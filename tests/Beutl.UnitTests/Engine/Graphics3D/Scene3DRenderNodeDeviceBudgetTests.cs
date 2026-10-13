using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Nodes;
using Beutl.Media;
using Moq;
using static Beutl.UnitTests.Engine.Graphics3D.Scene3DRenderNodeTestScenes;

namespace Beutl.UnitTests.Engine.Graphics3D;

// A preview drops a 3D scene the device cannot allocate, so its recording must not answer hits either. The
// scene is larger than the fixed shadow extents, so a refusal is about its size; the tiny-scene cases cover
// devices too small for any 3D.
[NonParallelizable]
[TestFixture]
public sealed class Scene3DRenderNodeDeviceBudgetTests
{
    private const int SceneWidth = 4096;
    private const int SceneHeight = 3072;
    private const int CubeFaceBudget = PointShadowPass.DefaultCubeFaceSize;
    private const int ShadowMapBudget = ShadowPass.DefaultShadowMapSize;

    private static readonly Rect s_sceneBounds = new(0, 0, SceneWidth, SceneHeight);
    private static readonly Point s_sceneCenter = new(SceneWidth / 2, SceneHeight / 2);
    private static readonly Rect s_tinySceneBounds = new(0, 0, 64, 48);

    [Test]
    public void HitTest_OverTheDeviceAttachmentBudget_ReportsNoHit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                HitTestSceneCenter(Budget(attachment: SceneWidth / 2)),
                Is.False,
                "a scene whose surface the device cannot attach is never drawn, so it must not answer a hit");
            Assert.That(
                HitTestSceneCenter(Budget(attachment: SceneWidth)),
                Is.True,
                "the control: the same scene exactly within the budget still answers");
            Assert.That(
                HitTestSceneCenter(Device3DExtentBudget.Unreported),
                Is.True,
                "the control: unreported limits refuse nothing, and the allocation still decides");
        });
    }

    [Test]
    public void Recording_OverTheDeviceAttachmentBudget_PublishesNothing()
    {
        using RenderNodeMeasurementProbe over = MeasureScene(
            s_sceneBounds,
            CreateRequest(Budget(attachment: SceneWidth / 2), s_sceneBounds));
        using RenderNodeMeasurementProbe within = MeasureScene(
            s_sceneBounds,
            CreateRequest(Budget(attachment: SceneWidth), s_sceneBounds));

        Assert.Multiple(() =>
        {
            Assert.That(over.Measurement.HasFragments, Is.False,
                "a scene the request cannot allocate records no fragment at all");
            Assert.That(over.Measurement.OutputBounds.IsEmpty, Is.True);
            Assert.That(within.Measurement.HasFragments, Is.True,
                "the control: within the budget the scene records its value");
            Assert.That(within.Measurement.OutputBounds, Is.EqualTo(s_sceneBounds));
        });
    }

    [Test]
    public void Recording_MeasuresTheBudgetInDevicePixels()
    {
        using RenderNodeMeasurementProbe atOne = MeasureScene(s_sceneBounds, AtScale(1f));
        using RenderNodeMeasurementProbe atTwo = MeasureScene(s_sceneBounds, AtScale(2f));

        Assert.Multiple(() =>
        {
            Assert.That(atOne.Measurement.HasFragments, Is.True,
                "4096x3072 logical units at one device pixel each fit a 4096 pixel attachment");
            Assert.That(atTwo.Measurement.HasFragments, Is.False,
                "the same scene at two device pixels per unit is 8192x6144, which does not");
        });

        static RenderNodeRenderRequest AtScale(float outputScale)
            => CreateRequest(Budget(attachment: SceneWidth), s_sceneBounds) with
            {
                OutputScale = outputScale,
                MaxWorkingScale = outputScale,
            };
    }

    [Test]
    public void Recording_ForDelivery_KeepsPublishingOverTheBudget()
    {
        RenderNodeRenderRequest request =
            CreateRequest(Budget(attachment: SceneWidth / 2), s_sceneBounds) with
            {
                Intent = RenderIntent.Delivery,
            };
        using RenderNodeMeasurementProbe delivery = MeasureScene(s_sceneBounds, request);

        Assert.That(delivery.Measurement.HasFragments, Is.True,
            "delivery reports a refused allocation instead of dropping it, so the value stays described");
    }

    // The fixed shadow extents refuse every scene on a device too small for them, however small the scene is,
    // so a footprint check alone would leave exactly the same stale hit answer behind.
    [Test]
    public void HitTest_OnADeviceThatCannotAttachTheShadowMaps_ReportsNoHitForEvenATinyScene()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                HitTestTinySceneCenter(new Device3DExtentBudget(ShadowMapBudget - 1, CubeFaceBudget)),
                Is.False,
                "the shadow maps every scene allocates do not fit, so no scene is ever drawn");
            Assert.That(
                HitTestTinySceneCenter(new Device3DExtentBudget(ShadowMapBudget, CubeFaceBudget)),
                Is.True,
                "the control: one pixel more and the same tiny scene is drawn, so it answers");
        });
    }

    [Test]
    public void HitTest_OnADeviceThatCannotBuildTheShadowCube_ReportsNoHitForEvenATinyScene()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                HitTestTinySceneCenter(new Device3DExtentBudget(ShadowMapBudget, CubeFaceBudget - 1)),
                Is.False,
                "the shadow cube faces do not fit, and a cube answers to its own limit");
            Assert.That(
                HitTestTinySceneCenter(new Device3DExtentBudget(ShadowMapBudget, CubeFaceBudget)),
                Is.True,
                "the control: one pixel more and the same tiny scene is drawn, so it answers");
        });
    }

    [Test]
    public void HitTest_UnderADeviceThatCannotAttachTheScene_ReportsNoHit()
    {
        // The production path: no request value is stated, so the renderer predicts the budget from the
        // process-wide graphics state.
        bool overTheLimit = HitTestSceneCenterUnder(CreateDevice(attachment: SceneWidth / 2));
        bool withinTheLimit = HitTestSceneCenterUnder(CreateDevice(attachment: SceneWidth));

        Assert.Multiple(() =>
        {
            Assert.That(overTheLimit, Is.False,
                "an installed device that cannot attach the scene's surface leaves it undrawn");
            Assert.That(withinTheLimit, Is.True,
                "the control: a device that can attach it draws it, so it answers");
        });
    }

    // A known gap: under a fractional device-grid phase the intermediate is one pixel wider, so a scene exactly
    // at the limit records and is then refused by the pool. See Scene3DRenderNode.Process.
    [Test]
    public void ASceneExactlyAtTheDeviceLimit_IsAKnownGap()
    {
        var bounds = new Rect(0, 0, 4096, 4096);
        (int width, int height) = Scene3DRenderNode.ResolveDeviceFootprint(bounds, 1f);
        int budget = width;
        var fractionalPhase = new Vector(0.5f, 0.5f);

        PixelRect integralPhase = PixelRect.FromRect(bounds, 1f);
        PixelRect shifted = PixelRect.FromRect(bounds.Translate(fractionalPhase), 1f);

        Assert.Multiple(() =>
        {
            Assert.That(
                Renderer3D.CanInitialize(new Device3DExtentBudget(budget, CubeFaceBudget), width, height),
                Is.True,
                "the 3D surface fits exactly, so the scene records");
            Assert.That(
                BufferDimensionBudget.Named(budget).Fits(integralPhase.Size),
                Is.True,
                "on an integral phase the intermediate fits too, and the scene renders");
            Assert.That(
                (shifted.Width, shifted.Height),
                Is.EqualTo((width + 1, height + 1)),
                "a fractional phase covers one more device pixel on each axis");
            Assert.That(
                BufferDimensionBudget.Named(budget).Fits(shifted.Size),
                Is.False,
                "which the pool refuses, dropping the preview value while the hit answer stays behind");
        });
    }

    [Test]
    public void TheDeviceFootprint_IsTheOneTheAllocationAsksFor()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                Scene3DRenderNode.ResolveDeviceFootprint(s_tinySceneBounds, 1f),
                Is.EqualTo((64, 48)));
            Assert.That(
                Scene3DRenderNode.ResolveDeviceFootprint(s_tinySceneBounds, 2f),
                Is.EqualTo((128, 96)));
            Assert.That(
                Scene3DRenderNode.ResolveDeviceFootprint(s_tinySceneBounds, 0.5f),
                Is.EqualTo((32, 24)));
            Assert.That(
                Scene3DRenderNode.ResolveDeviceFootprint(new Rect(0, 0, 10.2f, 10.8f), 1f),
                Is.EqualTo((11, 11)),
                "a fractional extent rounds up, because the allocation asks for whole pixels");
            Assert.That(
                Scene3DRenderNode.ResolveDeviceFootprint(new Rect(0, 0, 1e30f, 1e30f), 1e10f),
                Is.EqualTo((int.MaxValue, int.MaxValue)),
                "an extent past int range saturates rather than wrapping to one the device would accept");
        });
    }

    private static bool HitTestSceneCenterUnder(IGraphicsContext device)
    {
        InstalledGraphics previous = GraphicsContextFactory.ExchangeInstalledGraphics(
            new InstalledGraphics(device, null, null, FailedToInitialize: false));
        try
        {
            return HitTestSceneCenter(budget: null);
        }
        finally
        {
            GraphicsContextFactory.ExchangeInstalledGraphics(previous);
        }
    }

    private static IGraphicsContext CreateDevice(int attachment)
    {
        var device = new Mock<IGraphicsContext>();
        device.SetupGet(static c => c.Supports3DRendering).Returns(true);
        device.SetupGet(c => c.MaxAttachmentDimension).Returns(attachment);
        device.SetupGet(static c => c.MaxCubeFaceDimension).Returns(CubeFaceBudget);
        return device.Object;
    }

    private static bool HitTestSceneCenter(Device3DExtentBudget? budget)
        => HitTest(s_sceneBounds, s_sceneCenter, budget);

    private static bool HitTestTinySceneCenter(Device3DExtentBudget budget)
        => HitTest(s_tinySceneBounds, new Point(32, 24), budget);

    private static bool HitTest(Rect sceneBounds, Point point, Device3DExtentBudget? budget)
    {
        using var scene = CreateScene(sceneBounds);
        using var node = new Scene3DRenderNode(scene);
        using var renderer = new RenderNodeRenderer(node, CreateRequest(budget, sceneBounds));
        return renderer.HitTest(point);
    }

    private static RenderNodeMeasurementProbe MeasureScene(Rect sceneBounds, RenderNodeRenderRequest request)
    {
        Scene3D.Resource scene = CreateScene(sceneBounds);
        var node = new Scene3DRenderNode(scene);
        var renderer = new RenderNodeRenderer(node, request);
        return new RenderNodeMeasurementProbe(scene, node, renderer, renderer.Measure());
    }

    // States the backend so these tests pin the extent budget rather than whether this process has one.
    private static RenderNodeRenderRequest CreateRequest(Device3DExtentBudget? budget, Rect targetDomain)
        => Scene3DRenderNodeTestScenes.CreateRequest(targetDomain, supports3DRendering: true, budget);

    // A device large enough for the fixed shadow extents, so a scene-sized refusal is about the scene.
    private static Device3DExtentBudget Budget(int attachment)
        => new(attachment, CubeFaceBudget);
}
