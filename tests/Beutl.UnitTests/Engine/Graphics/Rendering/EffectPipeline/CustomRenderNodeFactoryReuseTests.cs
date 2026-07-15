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
    public void SameNodeType_FromDifferentFactoryInstance_IsReplaced()
    {
        var effect = new SameTypeSwitchingFactoryEffect();
        using var resource = (SameTypeSwitchingFactoryEffect.Resource)effect.ToResource(CompositionContext.Default);
        using var container = new ContainerRenderNode();

        FilterEffectRenderNode first = PushOnce(container, resource);
        resource.UseSecond = true;
        FilterEffectRenderNode second = PushOnce(container, resource);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.TypeOf<SameTypeFactoryRenderNode>());
            Assert.That(second, Is.Not.SameAs(first),
                "node type alone must not alias two factories with different constructors or captured policy");
            Assert.That(first.IsDisposed, Is.True,
                "the node created by the previous factory must be released when the factory identity changes");
            Assert.That(container.Children, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void PushFilterEffect_RejectsFactoryWhoseDeclaredTypeDiffersFromCreatedType()
    {
        var effect = new MismatchedFactoryEffect();
        using var resource = (MismatchedFactoryEffect.Resource)effect.ToResource(CompositionContext.Default);
        using var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container, new Size(120, 90), outputScale: 1f);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => context.PushFilterEffect(resource))!;

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

    [Test]
    public void Group_UsesCustomFactoryForLegacyFilterEffectSubclass()
    {
        var custom = new CustomNodeEffect();
        var group = new FilterEffectGroup();
        group.Children.Add(custom);
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(
            new Rect(0, 0, 120, 90), 1f, 1f, RenderIntent.Delivery);

        group.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);

        Assert.That(plan.Passes, Has.Length.EqualTo(1));
        Assert.That(plan.Passes[0], Is.TypeOf<CustomRenderNodePass>(),
            "a factory override must keep the same custom execution path inside a group");
    }

    [Test]
    public void Group_UsesNonDefaultPlanFactoryAsAPlannedBoundary()
    {
        var customPlan = new ClampToOutputEffect();
        var group = new FilterEffectGroup();
        group.Children.Add(customPlan);
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(
            new Rect(0, 0, 120, 90), 1f, 1f, RenderIntent.Delivery);

        group.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);

        var customPass = (CustomRenderNodePass)plan.Passes.Single();
        Assert.That(customPass.NodeType, Is.EqualTo(typeof(ClampToOutputPlanNode)),
            "a non-default plan factory must preserve its narrow execution policy inside a group");
    }

    [Test]
    public void TypedFactory_RejectsWrongResourceBeforeInvokingPluginConstructor()
    {
        bool invoked = false;
        FilterEffectRenderNodeFactory factory =
            FilterEffectRenderNodeFactory.Of<CustomNodeEffect.Resource, CustomFactoryRenderNode>(resource =>
            {
                invoked = true;
                return new CustomFactoryRenderNode(resource);
            });
        var otherEffect = new FallbackFilterEffect();
        using FilterEffect.Resource other = otherEffect.ToResource(CompositionContext.Default);

        Assert.That(() => factory.Create(other), Throws.ArgumentException.With.Message.Contains("requires resource type"));
        Assert.That(invoked, Is.False);
    }

    [Test]
    public void EmbeddedPass_ExecutesTheSameFactoryInstanceCapturedDuringDescription()
    {
        var effect = new StatefulFactoryEffect();
        using var resource = (StatefulFactoryEffect.Resource)effect.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(
            new Rect(0, 0, 120, 90), 1f, 1f, RenderIntent.Delivery);
        builder.Effect(resource);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, 1f);
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 120, 90), static _ => { });

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, frame, [input], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(outputs, Has.Length.EqualTo(1));
                Assert.That(resource.FactoryReads, Is.EqualTo(1),
                    "the executor must use the factory captured by Effect instead of re-reading a stateful getter");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    private static FilterEffectRenderNode PushOnce(ContainerRenderNode container, FilterEffect.Resource resource)
    {
        using var context = new GraphicsContext2D(container, new Size(120, 90), outputScale: 1f);
        using (context.PushFilterEffect(resource))
        {
        }

        return (FilterEffectRenderNode)container.Children[0];
    }
}

// A FilterEffect whose Resource returns a plugin-defined FilterEffectRenderNode subclass via RenderNodeFactory,
// mirroring the NodeGraphFilterEffect pattern (manual Resource + SuppressResourceClassGeneration).
[SuppressResourceClassGeneration]
internal sealed partial class CustomNodeEffect : CustomRenderNodeFilterEffect
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
            FilterEffectRenderNodeFactory.Of<Resource, CustomFactoryRenderNode>(
                static r => new CustomFactoryRenderNode(r));

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }
}

internal sealed class CustomFactoryRenderNode(FilterEffect.Resource fe) : FilterEffectRenderNode(fe)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context) => context.Input.ToArray();
}

[SuppressResourceClassGeneration]
internal sealed partial class MismatchedFactoryEffect : CustomRenderNodeFilterEffect
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
            FilterEffectRenderNodeFactory.Of<Resource, FilterEffectRenderNode>(
                static resource => new MismatchedFactoryRenderNode(resource));

        public bool MismatchedNodeDisposed { get; set; }

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
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

[SuppressResourceClassGeneration]
internal sealed partial class StatefulFactoryEffect : CustomRenderNodeFilterEffect
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
        private static readonly FilterEffectRenderNodeFactory s_expected =
            FilterEffectRenderNodeFactory.Of<Resource, StatefulFactoryRenderNode>(
                static resource => new StatefulFactoryRenderNode(resource));
        private static readonly FilterEffectRenderNodeFactory s_unexpected =
            FilterEffectRenderNodeFactory.Of<Resource, UnexpectedFactoryRenderNode>(
                static resource => new UnexpectedFactoryRenderNode(resource));

        public int FactoryReads { get; private set; }

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => ++FactoryReads == 1 ? s_expected : s_unexpected;
    }
}

internal sealed class StatefulFactoryRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context) => context.Input.ToArray();
}

internal sealed class UnexpectedFactoryRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
        => throw new AssertionException("The executor re-read the stateful render-node factory.");
}

[SuppressResourceClassGeneration]
internal sealed partial class SameTypeSwitchingFactoryEffect : CustomRenderNodeFilterEffect
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
        private static readonly FilterEffectRenderNodeFactory s_first =
            FilterEffectRenderNodeFactory.Of<Resource, SameTypeFactoryRenderNode>(
                static resource => new SameTypeFactoryRenderNode(resource, 1));
        private static readonly FilterEffectRenderNodeFactory s_second =
            FilterEffectRenderNodeFactory.Of<Resource, SameTypeFactoryRenderNode>(
                static resource => new SameTypeFactoryRenderNode(resource, 2));

        public bool UseSecond { get; set; }

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => UseSecond ? s_second : s_first;
    }
}

internal sealed class SameTypeFactoryRenderNode(
    FilterEffect.Resource resource,
    int policy) : FilterEffectRenderNode(resource)
{
    public int Policy { get; } = policy;

    public override RenderNodeOperation[] Process(RenderNodeContext context) => context.Input;
}
