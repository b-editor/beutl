using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Compile gate for the out-of-tree effect-authoring surface. This assembly is deliberately not a friend of
/// Beutl.Engine: making any API used below internal breaks this project at compile time.
/// </summary>
[TestFixture]
public sealed class EffectAuthoringPublicApiTests
{
    [Test]
    public void PluginEffect_CanOverrideDescribeAndReferenceEveryAuthoringBoundary()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(PublicAuthoringEffect).GetMethod(nameof(FilterEffect.Describe))!.IsPublic, Is.True);
            Assert.That(typeof(PublicCustomRenderNode).IsSubclassOf(typeof(FilterEffectRenderNode)), Is.True);
            Assert.That(typeof(PublicPlanRenderNode).IsSubclassOf(typeof(PlanFilterEffectRenderNode)), Is.True);
            Assert.That(typeof(PublicCustomResource).IsSubclassOf(typeof(CustomRenderNodeFilterEffect.Resource)),
                Is.True);
            Assert.That(typeof(PublicPlanResource).GetProperty(nameof(FilterEffect.Resource.PlanRenderNodeFactory)),
                Is.Not.Null);
            Assert.That(typeof(RenderPullPurpose).IsPublic, Is.True);
            Assert.That(typeof(RenderNodeContext).GetProperty(nameof(RenderNodeContext.PullPurpose)), Is.Not.Null);
            Assert.That(typeof(RenderNodeContext).GetProperty(
                "Pool", BindingFlags.Instance | BindingFlags.Public), Is.Null,
                "the executor-owned target pool must remain opaque to plugins");
            Assert.That(typeof(RenderNodeContext).GetProperty(
                "IsAuxiliaryPull", BindingFlags.Instance | BindingFlags.Public), Is.Null,
                "plugins branch on the public PullPurpose enum instead of a duplicate convenience flag");
            Assert.That(typeof(RenderNodeContext).GetMethod(nameof(RenderNodeContext.CreateChildProcessor)),
                Is.Not.Null);
            Assert.That(typeof(EffectGraphBuilder).GetProperty(nameof(EffectGraphBuilder.PullPurpose)), Is.Not.Null);
            Assert.That(typeof(GeometrySession).GetProperty(nameof(GeometrySession.PullPurpose)), Is.Not.Null);
            Assert.That(typeof(PassUniformContext).GetProperty(nameof(PassUniformContext.PullPurpose)), Is.Not.Null);
            Assert.That(typeof(ImmediateCanvas).GetProperty(nameof(ImmediateCanvas.PullPurpose)), Is.Not.Null);
            Assert.That(typeof(BrushConstructor).GetProperty(nameof(BrushConstructor.PullPurpose)), Is.Not.Null);
            Assert.That(typeof(RenderNodeProcessor).GetConstructors()
                .SelectMany(static constructor => constructor.GetParameters())
                .Any(static parameter => parameter.ParameterType.Name == "RenderTargetPool"), Is.False,
                "the executor-owned target pool must not appear in public constructor signatures");
            Assert.That(typeof(RenderNodeProcessor).Assembly
                .GetType("Beutl.Graphics.Rendering.RenderTargetPool")!.IsPublic, Is.False,
                "the executor-owned target pool type itself must remain internal");
            Assert.That(typeof(PassUniformContext).GetProperties()
                .All(static property => property.SetMethod == null), Is.True,
                "uniform context values must be immutable after validated construction");
        });
    }

    public abstract class PublicAuthoringEffect : FilterEffect
    {
        public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
        {
            builder.Shader(ShaderNodeDescriptor.Snippet("half4 apply(half4 c) { return c; }"));
            builder.ColorFilter(ColorFilterNodeDescriptor.Create(
                static () => SKColorFilter.CreateLumaColor(), "public-color-filter"));
            builder.SkiaFilter(SkiaFilterNodeDescriptor.Create(
                static inner => inner, BoundsContract.Identity, "public-skia-filter"));
            builder.Compute(ComputeNodeDescriptor.Create(
                static context =>
                {
                    _ = context.SourceBounds;
                    _ = context.TargetBounds;
                    _ = context.SourceScale;
                    _ = context.WorkingScale;
                    context.CopySourceToDestination();
                },
                passCount: 1,
                BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
                structuralToken: "public-compute"));
            builder.Geometry(GeometryNodeDescriptor.Create(
                static _ => { }, BoundsContract.Identity, "public-geometry"));
            builder.Split(SplitNodeDescriptor.Static(
                static emitter => emitter.Emit(emitter.Input.Bounds, static _ => { }),
                branchCount: 1,
                structuralToken: "public-split"));
            builder.Composite(CompositeNodeDescriptor.Create(
                BlendMode.SrcOver, structuralToken: "public-composite"));
            builder.NestedGraph(NestedGraphNodeDescriptor.Create(
                static (nested, _) => nested.Saturate(1f), "public-nested-graph"));

            if (resource is PublicCustomResource custom)
                builder.Effect(custom);
        }
    }

    public sealed class PublicCustomResource : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<PublicCustomResource, PublicCustomRenderNode>(
                static resource => new PublicCustomRenderNode(resource));

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }

    public sealed class PublicCustomRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context) => context.Input;

        public static RenderNodeProcessor CreateNestedProcessor(RenderNodeContext context, RenderNode root)
            => context.CreateChildProcessor(root, useRenderCache: false);
    }

    public sealed class PublicPlanResource : FilterEffect.Resource
    {
        private static readonly PlanFilterEffectRenderNodeFactory s_factory =
            PlanFilterEffectRenderNodeFactory.Of<PublicPlanResource, PublicPlanRenderNode>(
                static resource => new PublicPlanRenderNode(resource));

        public override PlanFilterEffectRenderNodeFactory PlanRenderNodeFactory => s_factory;
    }

    public sealed class PublicPlanRenderNode(FilterEffect.Resource resource) : PlanFilterEffectRenderNode(resource)
    {
        protected override float ResolveWorkingScale(
            RenderNodeContext context,
            ReadOnlySpan<EffectiveScale> inputScales)
            => Math.Min(context.OutputScale, context.MaxWorkingScale);
    }
}
