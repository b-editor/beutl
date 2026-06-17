using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics3D;

[NonParallelizable]
[TestFixture]
public class Scene3DRenderNodeScaleTests
{
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
            var context = new RenderNodeContext([], outputScale: 2f, maxWorkingScale: 0.5f);

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
