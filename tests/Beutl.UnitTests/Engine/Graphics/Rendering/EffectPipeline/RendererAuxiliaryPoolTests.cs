using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

[NonParallelizable]
[TestFixture]
public class RendererAuxiliaryPoolTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void AuxiliaryPull_ForwardsRendererPoolToFilterEffects(bool hitTest)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            PoolProbeRenderNode.SawPool = false;
            PoolProbeRenderNode.SawAuxiliaryPull = false;
            var shape = new RectShape
            {
                Width = { CurrentValue = 64 },
                Height = { CurrentValue = 48 },
                Fill = { CurrentValue = Brushes.White },
                FilterEffect = { CurrentValue = new PoolProbeEffect() },
            };
            Drawable.Resource resource = shape.ToResource(CompositionContext.Default);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(128, 96));

            using var renderer = new Renderer(128, 96, RenderIntent.Delivery);
            renderer.Diagnostics.Reset();
            if (hitTest)
                renderer.HitTest(frame, new Point(64, 48));
            else
            {
                renderer.UpdateFrame(frame);
                renderer.RecalculateBoundaries(shape.ZIndex);
            }

            Assert.Multiple(() =>
            {
                Assert.That(PoolProbeRenderNode.SawPool, Is.True,
                    "HitTest/RecalculateBoundaries must preserve the renderer pool on the auxiliary pull");
                Assert.That(PoolProbeRenderNode.SawAuxiliaryPull, Is.True,
                    "HitTest/RecalculateBoundaries must mark the pull as cache-state-neutral auxiliary work");
                Assert.That(renderer.Diagnostics.Snapshot().GpuPasses, Is.Zero,
                    "auxiliary pulls must not contaminate frame-rendering diagnostics");
            });
        });
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class PoolProbeEffect : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<Resource, PoolProbeRenderNode>(static r => new PoolProbeRenderNode(r));

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }
}

internal sealed class PoolProbeRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    internal static bool SawPool { get; set; }

    internal static bool SawAuxiliaryPull { get; set; }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        SawPool |= context.Pool != null;
        SawAuxiliaryPull |= context.IsAuxiliaryPull;
        if (context.Diagnostics != null)
            context.Diagnostics.GpuPasses++;
        return context.Input;
    }
}
