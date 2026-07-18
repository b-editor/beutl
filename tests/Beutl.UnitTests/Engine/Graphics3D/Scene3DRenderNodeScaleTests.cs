using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D;
using Beutl.Media.Proxy;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

[NonParallelizable]
[TestFixture]
public class Scene3DRenderNodeScaleTests
{
    [Test]
    public void CreateCompositionContext_PreservesAmbientPolicyAndProxyRouting()
    {
        var scene = new Scene3D();
        using var resource = (Scene3D.Resource)scene.ToResource(new CompositionContext(
            TimeSpan.FromSeconds(2),
            RenderIntent.Preview,
            RenderPullPurpose.Frame)
        {
            DisableResourceShare = true,
            PreferProxy = true,
            PreferredProxyPreset = ProxyPreset.Half,
        });
        var renderContext = new RenderNodeContext(
            [],
            RenderIntent.Delivery,
            pullPurpose: RenderPullPurpose.Auxiliary);

        CompositionContext context = Scene3DRenderNode.CreateCompositionContext(resource, renderContext);

        Assert.Multiple(() =>
        {
            Assert.That(context.Time, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(context.RenderIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(context.PullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
            Assert.That(context.DisableResourceShare, Is.True);
            Assert.That(context.PreferProxy, Is.True);
            Assert.That(context.PreferredProxyPreset, Is.EqualTo(ProxyPreset.Half));
        });
    }

    [Test]
    public void Process_AuxiliaryPullDoesNotPopulateFrameRenderer()
    {
        var graphicsContext = VulkanTestEnvironment.EnsureAvailable();
        if (!graphicsContext.Supports3DRendering)
        {
            Assert.Ignore("3D rendering is not supported on this GPU.");
        }

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var scene = new Scene3D();
            scene.RenderWidth.CurrentValue = 32;
            scene.RenderHeight.CurrentValue = 32;
            using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
            using var node = new Scene3DRenderNode(resource);
            var context = new RenderNodeContext(
                [], RenderIntent.Preview, outputScale: 1f,
                pullPurpose: RenderPullPurpose.Auxiliary);

            RenderNodeOperation[] ops = node.Process(context);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(ops, Is.Not.Empty);
                    Assert.That(ops[0].Bounds, Is.EqualTo(new Rect(0, 0, 32, 32)));
                    Assert.That(resource.Renderer, Is.Null,
                        "an auxiliary pull must not create or replace the retained frame renderer");
                });
            }
            finally
            {
                DisposeAll(ops);
            }
        });
    }

    [Test]
    public void Process_AuxiliaryPullWithout3DSupport_ProducesNoOperation()
    {
        var unsupported = new Mock<IGraphicsContext>(MockBehavior.Strict);
        unsupported.SetupGet(x => x.Supports3DRendering).Returns(false);
        Scene3DRenderNode.SetGraphicsContextProviderForTest(() => unsupported.Object);
        try
        {
            var scene = new Scene3D();
            scene.RenderWidth.CurrentValue = 32;
            scene.RenderHeight.CurrentValue = 32;
            using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
            using var node = new Scene3DRenderNode(resource);
            var context = new RenderNodeContext(
                [], RenderIntent.Preview, outputScale: 1f,
                pullPurpose: RenderPullPurpose.Auxiliary);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.Multiple(() =>
            {
                Assert.That(ops, Is.Empty,
                    "auxiliary pulls must not advertise a 3D scene when the current context cannot render it");
                Assert.That(resource.Renderer, Is.Null);
            });
        }
        finally
        {
            Scene3DRenderNode.SetGraphicsContextProviderForTest(null);
        }
    }

    [Test]
    public void Process_HoldsSceneOperationUntilResourceUseCompletes()
    {
        var unsupported = new Mock<IGraphicsContext>(MockBehavior.Strict);
        unsupported.SetupGet(x => x.Supports3DRendering).Returns(false);
        var scene = new Scene3D();
        scene.RenderWidth.CurrentValue = 32;
        scene.RenderHeight.CurrentValue = 32;
        using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        using var node = new Scene3DRenderNode(resource);
        Exception? concurrentCleanupFailure = null;
        Scene3DRenderNode.SetGraphicsContextProviderForTest(() =>
        {
            try
            {
                Task.Run(resource.Dispose).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                concurrentCleanupFailure = ex;
            }

            return unsupported.Object;
        });

        try
        {
            RenderNodeOperation[] operations = node.Process(new RenderNodeContext(
                [], RenderIntent.Preview, outputScale: 1f));

            Assert.Multiple(() =>
            {
                Assert.That(operations, Is.Empty);
                Assert.That(concurrentCleanupFailure, Is.TypeOf<InvalidOperationException>());
                Assert.That(resource.IsDisposed, Is.False,
                    "cleanup rejected during rendering must remain retryable");
            });

            Assert.DoesNotThrow(resource.Dispose);
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => resource.Renderer = null,
                "a renderer must never be published into a terminal resource");
        }
        finally
        {
            Scene3DRenderNode.SetGraphicsContextProviderForTest(null);
        }
    }

    // The teardown of a renderer discarded after an allocation failure is itself fallible (native pass disposal);
    // its throw must never replace the delivery allocation failure, abort the preview drop, or leave the
    // inconsistent renderer retained on the resource.
    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void Process_FrameAllocationFailure_PreservesPolicyWhenTeardownAlsoThrows(RenderIntent intent)
    {
        var compiler = new Mock<Beutl.Graphics.Backend.IShaderCompiler>();
        compiler.As<IDisposable>().Setup(x => x.Dispose())
            .Throws(new InvalidOperationException("teardown failed"));
        var graphicsContext = new Mock<IGraphicsContext>();
        graphicsContext.SetupGet(x => x.Supports3DRendering).Returns(true);
        graphicsContext.Setup(x => x.CreateShaderCompiler()).Returns(compiler.Object);
        graphicsContext
            .Setup(x => x.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Throws(new InvalidOperationException("allocation failed"));
        Scene3DRenderNode.SetGraphicsContextProviderForTest(() => graphicsContext.Object);
        try
        {
            var scene = new Scene3D();
            scene.RenderWidth.CurrentValue = 32;
            scene.RenderHeight.CurrentValue = 32;
            using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
            using var node = new Scene3DRenderNode(resource);
            var context = new RenderNodeContext([], intent, outputScale: 1f);

            if (intent == RenderIntent.Delivery)
            {
                InvalidOperationException? exception =
                    Assert.Throws<InvalidOperationException>(() => node.Process(context));
                Assert.That(exception!.Message, Does.Contain("3D render surface allocation failed"),
                    "the teardown throw must not replace the delivery allocation failure");
            }
            else
            {
                RenderNodeOperation[] ops = node.Process(context);
                Assert.That(ops, Is.Empty, "preview drops the frame when the 3D surface cannot allocate");
            }

            Assert.That(resource.Renderer, Is.Null,
                "the inconsistent renderer must be discarded even when its teardown throws");
        }
        finally
        {
            Scene3DRenderNode.SetGraphicsContextProviderForTest(null);
        }
    }

    [Test]
    public void Process_RespectsMaxWorkingScale_WhenOutputScaleIsHigher()
    {
        var graphicsContext = VulkanTestEnvironment.EnsureAvailable();
        if (!graphicsContext.Supports3DRendering)
        {
            Assert.Ignore("3D rendering is not supported on this GPU.");
        }

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var scene = new Scene3D();
            scene.RenderWidth.CurrentValue = 32;
            scene.RenderHeight.CurrentValue = 32;
            var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);

            using var node = new Scene3DRenderNode(resource);
            var context = new RenderNodeContext([], RenderIntent.Delivery, outputScale: 2f, maxWorkingScale: 0.5f);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty, "Scene3DRenderNode emitted no operation for a valid scene");
            Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False);
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(0.5f).Within(1e-4f));

            DisposeAll(ops);
            resource.Dispose();
        });
    }

    private static void DisposeAll(RenderNodeOperation[] ops)
    {
        foreach (RenderNodeOperation op in ops)
        {
            op.Dispose();
        }
    }
}
