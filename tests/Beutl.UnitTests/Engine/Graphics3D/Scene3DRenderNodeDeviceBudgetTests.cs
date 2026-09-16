using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D;
using Beutl.Media;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

// A 3D scene wider than the device can attach is refused by the allocation, and a preview drops the value
// rather than failing. The recording has to reach the same answer, because hit testing never executes: a
// scene that is never drawn must not answer clicks, and walking topmost-first it must not swallow the clicks
// meant for the 2D content beneath it.
[NonParallelizable]
[TestFixture]
public sealed class Scene3DRenderNodeDeviceBudgetTests
{
    private static readonly Rect s_sceneBounds = new(0, 0, 64, 48);
    private static readonly Point s_sceneCenter = new(32, 24);

    [Test]
    public void HitTest_OverTheDeviceAttachmentBudget_ReportsNoHit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                HitTestSceneCenter(max3DAttachmentDimension: 32),
                Is.False,
                "a scene whose surface the device cannot attach is never drawn, so it must not answer a hit");
            Assert.That(
                HitTestSceneCenter(max3DAttachmentDimension: 64),
                Is.True,
                "the control: the same scene exactly within the budget still answers");
            Assert.That(
                HitTestSceneCenter(max3DAttachmentDimension: 0),
                Is.True,
                "the control: an unreported limit refuses nothing, and the allocation still decides");
        });
    }

    [Test]
    public void HitTest_OverTheBudget_LeavesThe2DContentBeneathAnswering()
    {
        var rect = new Rect(0, 0, 8, 8);
        var insideRectOnly = new Point(4, 4);
        var insideSceneOnly = new Point(48, 32);
        using var fill = new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        using var root = new ContainerRenderNode();
        using var scene = CreateScene();
        // Children draw in order, so the scene is on top of the rectangle.
        root.AddChild(new RectangleRenderNode(rect, fill, null));
        root.AddChild(new Scene3DRenderNode(scene));
        using var renderer = new RenderNodeRenderer(root, CreateRequest(max3DAttachmentDimension: 32));

        Assert.Multiple(() =>
        {
            Assert.That(renderer.HitTest(insideRectOnly), Is.True,
                "the 2D content beneath the undrawn scene still answers");
            Assert.That(renderer.HitTest(insideSceneOnly), Is.False,
                "where only the undrawn scene lies there is nothing to hit");
        });
    }

    [Test]
    public void Recording_OverTheDeviceAttachmentBudget_PublishesNothing()
    {
        using RenderNodeMeasurementProbe over = MeasureScene(CreateRequest(max3DAttachmentDimension: 32));
        using RenderNodeMeasurementProbe within = MeasureScene(CreateRequest(max3DAttachmentDimension: 64));

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
        using RenderNodeMeasurementProbe atOne = MeasureScene(AtScale(1f));
        using RenderNodeMeasurementProbe atTwo = MeasureScene(AtScale(2f));

        Assert.Multiple(() =>
        {
            Assert.That(atOne.Measurement.HasFragments, Is.True,
                "64x48 logical units at one device pixel each fit a 96 pixel attachment");
            Assert.That(atTwo.Measurement.HasFragments, Is.False,
                "the same scene at two device pixels per unit is 128x96, which does not");
        });

        static RenderNodeRenderRequest AtScale(float outputScale)
            => CreateRequest(max3DAttachmentDimension: 96) with
            {
                OutputScale = outputScale,
                MaxWorkingScale = outputScale,
            };
    }

    [Test]
    public void Recording_ForDelivery_KeepsPublishingOverTheBudget()
    {
        using RenderNodeMeasurementProbe delivery = MeasureScene(
            CreateRequest(max3DAttachmentDimension: 32) with { Intent = RenderIntent.Delivery });

        Assert.That(delivery.Measurement.HasFragments, Is.True,
            "delivery reports a refused allocation instead of dropping it, so the value stays described");
    }

    [Test]
    public void HitTest_UnderADeviceThatCannotAttachTheScene_ReportsNoHit()
    {
        // The production path: no request value is stated, so the renderer predicts the budget from the
        // process-wide graphics state.
        bool overTheLimit = HitTestSceneCenterUnder(CreateDevice(maxAttachmentDimension: 32));
        bool withinTheLimit = HitTestSceneCenterUnder(CreateDevice(maxAttachmentDimension: 8192));

        Assert.Multiple(() =>
        {
            Assert.That(overTheLimit, Is.False,
                "an installed device that cannot attach the scene's surface leaves it undrawn");
            Assert.That(withinTheLimit, Is.True,
                "the control: a device that can attach it draws it, so it answers");
        });
    }

    // The record-time question and the allocation-time refusal have to be one question. This walks the grid
    // where they could disagree - unreported limits, exact fits, one axis over - and pins that they do not.
    [TestCase(0, 64, 48)]
    [TestCase(-1, 64, 48)]
    [TestCase(64, 64, 48)]
    [TestCase(63, 64, 48)]
    [TestCase(48, 64, 48)]
    [TestCase(47, 64, 48)]
    [TestCase(8192, 64, 48)]
    public void TheRecordTimeQuestion_IsTheAllocationTimeQuestion(int budget, int width, int height)
    {
        var device = new Mock<IGraphicsContext>();
        device.SetupGet(c => c.MaxAttachmentDimension).Returns(budget);

        bool recordTimeAnswer = DeviceExtentLimits.CanAttach(budget, width, height);
        bool allocationAccepts = true;
        try
        {
            DeviceExtentLimits.ThrowIfCannotAttach(device.Object, width, height);
        }
        catch (InvalidOperationException)
        {
            allocationAccepts = false;
        }

        Assert.That(recordTimeAnswer, Is.EqualTo(allocationAccepts));
    }

    [Test]
    public void TheDeviceFootprint_IsTheOneTheAllocationAsksFor()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                Scene3DRenderNode.ResolveDeviceFootprint(s_sceneBounds, 1f),
                Is.EqualTo((64, 48)));
            Assert.That(
                Scene3DRenderNode.ResolveDeviceFootprint(s_sceneBounds, 2f),
                Is.EqualTo((128, 96)));
            Assert.That(
                Scene3DRenderNode.ResolveDeviceFootprint(s_sceneBounds, 0.5f),
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
            return HitTestSceneCenter(max3DAttachmentDimension: null);
        }
        finally
        {
            GraphicsContextFactory.ExchangeInstalledGraphics(previous);
        }
    }

    private static IGraphicsContext CreateDevice(int maxAttachmentDimension)
    {
        var device = new Mock<IGraphicsContext>();
        device.SetupGet(static c => c.Supports3DRendering).Returns(true);
        device.SetupGet(c => c.MaxAttachmentDimension).Returns(maxAttachmentDimension);
        return device.Object;
    }

    private static bool HitTestSceneCenter(int? max3DAttachmentDimension)
    {
        using var scene = CreateScene();
        using var node = new Scene3DRenderNode(scene);
        using var renderer = new RenderNodeRenderer(node, CreateRequest(max3DAttachmentDimension));
        return renderer.HitTest(s_sceneCenter);
    }

    private static RenderNodeMeasurementProbe MeasureScene(RenderNodeRenderRequest request)
    {
        Scene3D.Resource scene = CreateScene();
        var node = new Scene3DRenderNode(scene);
        var renderer = new RenderNodeRenderer(node, request);
        return new RenderNodeMeasurementProbe(scene, node, renderer, renderer.Measure());
    }

    private static Scene3D.Resource CreateScene()
    {
        var scene = new Scene3D();
        scene.RenderWidth.CurrentValue = (float)s_sceneBounds.Width;
        scene.RenderHeight.CurrentValue = (float)s_sceneBounds.Height;
        return (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
    }

    private static RenderNodeRenderRequest CreateRequest(int? max3DAttachmentDimension)
        => new()
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_sceneBounds,
            CacheOptions = RenderCacheOptions.Disabled,
            // Stated so these tests pin the extent budget rather than whether this process has a backend.
            Supports3DRendering = true,
            Max3DAttachmentDimension = max3DAttachmentDimension,
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
