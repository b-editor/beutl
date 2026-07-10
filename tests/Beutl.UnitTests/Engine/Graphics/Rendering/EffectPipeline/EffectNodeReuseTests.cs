using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins render-graph node reuse for filter effects across drawable re-renders. The production frame loop
/// (Renderer.RenderDrawable) re-renders a changed drawable through GraphicsContext2D into the SAME persistent
/// DrawableRenderNode; the graph diff must then hand the existing PlanFilterEffectRenderNode back to the effect,
/// because that node owns the plan and prefix caches — recreating it recompiles the plan on every animated frame
/// (C3.3/SC-002 hold at the node level but were lost at this boundary).
/// </summary>
[NonParallelizable]
[TestFixture]
public class EffectNodeReuseTests
{
    private static readonly PixelSize s_size = new(320, 240);

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        VulkanTestEnvironment.EnsureAvailable();
    }

    [Test]
    public void AnimatedRerender_ReusesEffectNodeAndCachedPlan()
    {
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var gamma = new Gamma();
            gamma.Amount.CurrentValue = 1.5f;
            Drawable.Resource resource = MakeShape(gamma);
            using var node = new DrawableRenderNode(resource);

            Rerender(node, resource);
            FilterEffectRenderNode first = FindEffectNode(node);
            PipelineDiagnosticsSnapshot warm = Process(node);
            Assert.That(warm.PlanCompilations, Is.EqualTo(1), "first frame compiles the plan once");

            // The animated frame: a parameter change flows through the real resource-update path, the drawable
            // re-renders, and the plan must rebind uniforms on the cached plan instead of recompiling.
            gamma.Amount.CurrentValue = 2.2f;
            bool updateOnly = false;
            resource.Update(resource.GetOriginal(), CompositionContext.Default, ref updateOnly);
            Assert.That(node.Update(resource), Is.True, "the parameter change must dirty the drawable node");

            Rerender(node, resource);
            FilterEffectRenderNode second = FindEffectNode(node);
            PipelineDiagnosticsSnapshot animated = Process(node);

            Assert.That(second, Is.SameAs(first),
                "the graph diff must reuse the effect's render node across a re-render — recreating it discards " +
                "the plan and prefix caches");
            Assert.That(animated.PlanCompilations, Is.Zero,
                "an animated parameter must rebind uniforms on the cached plan (C3.3), not recompile per frame");
        });
    }

    private static void Rerender(DrawableRenderNode node, Drawable.Resource resource)
    {
        using var ctx = new GraphicsContext2D(node, s_size.ToSize(1), 1f);
        resource.GetOriginal().Render(ctx, resource);
    }

    private static PipelineDiagnosticsSnapshot Process(DrawableRenderNode node)
    {
        var diagnostics = new PipelineDiagnostics();
        var processor = new RenderNodeProcessor(
            node, useRenderCache: false, outputScale: 1f, float.PositiveInfinity, diagnostics, pool: null);
        RenderNodeOperation[] ops = processor.PullToRoot();
        using RenderTarget target = RenderTarget.Create(s_size.Width, s_size.Height)
                                    ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, 1f, logicalSize: s_size.ToSize(1));
        foreach (RenderNodeOperation op in ops)
        {
            op.Render(canvas);
            op.Dispose();
        }

        return diagnostics.Snapshot();
    }

    private static FilterEffectRenderNode FindEffectNode(RenderNode node)
    {
        if (node is FilterEffectRenderNode effectNode)
            return effectNode;
        if (node is ContainerRenderNode container)
        {
            foreach (RenderNode child in container.Children)
            {
                if (FindNullable(child) is { } found)
                    return found;
            }
        }

        throw new InvalidOperationException("No FilterEffectRenderNode found in the render graph.");

        static FilterEffectRenderNode? FindNullable(RenderNode node)
        {
            if (node is FilterEffectRenderNode effectNode)
                return effectNode;
            if (node is ContainerRenderNode container)
            {
                foreach (RenderNode child in container.Children)
                {
                    if (FindNullable(child) is { } found)
                        return found;
                }
            }

            return null;
        }
    }

    private static Drawable.Resource MakeShape(FilterEffect effect)
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = 160;
        shape.Height.CurrentValue = 120;
        shape.Fill.CurrentValue = new SolidColorBrush { Color = { CurrentValue = Colors.Coral } };
        shape.FilterEffect.CurrentValue = effect;
        return shape.ToResource(CompositionContext.Default);
    }
}
