using System.Linq;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

// Generalizes the node-reuse guarantee (EffectNodeReuseTests covers the default PlanFilterEffectRenderNode) to a
// plugin node type. Because RenderNodeFactory captures the node type alongside its constructor, the render-graph
// diff reuses a custom FilterEffectRenderNode across re-renders instead of rebuilding it — which is what keeps a
// custom node's plan/prefix caches alive on animated frames. This is the invariant the removed CreateRenderNode +
// RenderNodeType pair could silently break by overriding one and forgetting the other.
[TestFixture]
public class CustomRenderNodeFactoryReuseTests
{
    [Test]
    public void CustomFactoryNodeType_IsReusedAcrossReRenders()
    {
        var effect = new CustomNodeEffect();
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var container = new ContainerRenderNode();

        FilterEffectRenderNode first = PushOnce(container, resource);
        FilterEffectRenderNode second = PushOnce(container, resource);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.TypeOf<CustomFactoryRenderNode>(),
                "Push did not create the factory-declared node type");
            Assert.That(container.Children, Has.Count.EqualTo(1), "the re-render must not append a second node");
            Assert.That(second, Is.SameAs(first),
                "the render-graph diff must reuse the custom factory node across a re-render — RenderNodeFactory "
                + "pairs the node type with its constructor so the reuse check cannot drift from it");
        });
    }

    [Test]
    public void ResourcePush_RejectsFactoryWhoseDeclaredTypeDiffersFromCreatedType()
    {
        var effect = new MismatchedFactoryEffect();
        using var resource = (MismatchedFactoryEffect.Resource)effect.ToResource(CompositionContext.Default);
        using var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container, new Size(120, 90), outputScale: 1f);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => resource.Push(context))!;

        Assert.Multiple(() =>
        {
            Assert.That(error.Message, Does.Contain(nameof(MismatchedFactoryRenderNode))
                .And.Contain("concrete node type"));
            Assert.That(resource.MismatchedNodeDisposed, Is.True,
                "the rejected node must release its owned resources");
            Assert.That(container.Children, Is.Empty,
                "the top-level recording path must reject the mismatch before appending it to the render tree");
        });
    }

    private static FilterEffectRenderNode PushOnce(ContainerRenderNode container, FilterEffect.Resource resource)
    {
        using var context = new GraphicsContext2D(container, new Size(120, 90), outputScale: 1f);
        using (resource.Push(context))
        {
        }

        return (FilterEffectRenderNode)container.Children[0];
    }
}

// A FilterEffect whose Resource returns a plugin-defined FilterEffectRenderNode subclass via RenderNodeFactory,
// mirroring the NodeGraphFilterEffect pattern (manual Resource + SuppressResourceClassGeneration).
[SuppressResourceClassGeneration]
internal sealed partial class CustomNodeEffect : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => FilterEffectRenderNodeFactory.Of(static r => new CustomFactoryRenderNode(r));
    }
}

internal sealed class CustomFactoryRenderNode(FilterEffect.Resource fe) : FilterEffectRenderNode(fe)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context) => context.Input.ToArray();
}

[SuppressResourceClassGeneration]
internal sealed partial class MismatchedFactoryEffect : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public bool MismatchedNodeDisposed { get; set; }

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => FilterEffectRenderNodeFactory.Of<FilterEffectRenderNode>(
                static resource => new MismatchedFactoryRenderNode((Resource)resource));
    }
}

internal sealed class MismatchedFactoryRenderNode(MismatchedFactoryEffect.Resource resource)
    : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context) => context.Input.ToArray();

    protected override void OnDispose(bool disposing)
    {
        resource.MismatchedNodeDisposed = true;
        base.OnDispose(disposing);
    }
}
