using System.Reflection;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
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
        var effect = new PublicAuthoringEffect();
        effect.PluginValue.CurrentValue = 0.25f;
        using PublicAuthoringEffect.Resource generatedResource = effect.ToResource(CompositionContext.Default);

        Assert.Multiple(() =>
        {
            Assert.That(typeof(PublicAuthoringEffect).GetMethod(nameof(FilterEffect.Describe))!.IsPublic, Is.True);
            Assert.That(generatedResource, Is.TypeOf<PublicAuthoringEffect.Resource>(),
                "the public gate must consume the source-generated concrete Resource type");
            Assert.That(generatedResource.PluginValue, Is.EqualTo(0.25f),
                "the generated Resource must snapshot an out-of-tree IProperty value");
            Assert.That(typeof(PublicCustomRenderNode).IsSubclassOf(typeof(FilterEffectRenderNode)), Is.True);
            Assert.That(typeof(PublicPlanRenderNode).IsSubclassOf(typeof(PlanFilterEffectRenderNode)), Is.True);
            Assert.That(typeof(PublicCustomResource).IsSubclassOf(typeof(CustomRenderNodeFilterEffect.Resource)),
                Is.True);
            Assert.That(typeof(PublicPlanResource).GetProperty(nameof(FilterEffect.Resource.PlanRenderNodeFactory)),
                Is.Not.Null);
            Assert.That(typeof(FilterEffect.Resource).GetMethod(
                "Push", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly), Is.Null,
                "top-level placement must not expose a third virtual customization route");
            Assert.That(typeof(RenderPullPurpose).IsPublic, Is.True);
            Assert.That(typeof(RenderNodeContext).GetProperty(nameof(RenderNodeContext.PullPurpose)), Is.Not.Null);
            PropertyInfo diagnosticsProperty = typeof(RenderNodeContext).GetProperty(
                nameof(RenderNodeContext.Diagnostics))!;
            Assert.That(diagnosticsProperty.GetMethod!.IsPublic, Is.True,
                "plugins must be able to observe the owning renderer's counters");
            Assert.That(diagnosticsProperty.GetSetMethod(nonPublic: true)!.IsPublic, Is.False,
                "plugins must not replace the owning renderer's diagnostics instance");
            Assert.That(typeof(RenderNodeContext).GetProperty(
                "Pool", BindingFlags.Instance | BindingFlags.Public), Is.Null,
                "the executor-owned target pool must remain opaque to plugins");
            Assert.That(typeof(RenderNodeContext).GetProperty(
                "IsAuxiliaryPull", BindingFlags.Instance | BindingFlags.Public), Is.Null,
                "plugins branch on the public PullPurpose enum instead of a duplicate convenience flag");
            Assert.That(typeof(RenderNodeContext).GetMethod(nameof(RenderNodeContext.CreateChildProcessor)),
                Is.Not.Null);
            Assert.That(typeof(EffectGraphBuilder).GetProperty(nameof(EffectGraphBuilder.PullPurpose)), Is.Not.Null);
            Assert.That(typeof(EffectGraphBuilder).GetMethod(
                nameof(EffectGraphBuilder.CustomRenderNode), [typeof(CustomRenderNodeDescriptor)]), Is.Not.Null,
                "the public CustomRenderNodeDescriptor.Create factory needs a public appender to be reachable");
            Assert.That(typeof(GeometrySession).GetProperty(nameof(GeometrySession.PullPurpose)), Is.Not.Null);
            Assert.That(typeof(PassUniformContext).GetProperty(nameof(PassUniformContext.PullPurpose)), Is.Not.Null);
            Assert.That(typeof(ImmediateCanvas).GetProperty(nameof(ImmediateCanvas.PullPurpose)), Is.Not.Null);
            Assert.That(typeof(BrushConstructor).GetProperty(nameof(BrushConstructor.PullPurpose)), Is.Not.Null);
            Assert.That(typeof(RenderNodeCacheHelper).GetMethods()
                .Where(static method => method.Name == nameof(RenderNodeCacheHelper.MakeCache))
                .SelectMany(static method => method.GetParameters())
                .Any(static parameter => parameter.ParameterType == typeof(RenderPullPurpose)), Is.True,
                "public cache warm-up must retain an explicit purpose parameter so auxiliary requests fail loudly");
            Assert.That(typeof(RenderNodeCache).GetProperty(nameof(RenderNodeCache.CachedRenderIntent)), Is.Not.Null);
            Assert.That(typeof(RenderNodeCache).GetProperty(nameof(RenderNodeCache.CachedPullPurpose)), Is.Not.Null);
            Assert.That(typeof(RenderNodeCache).GetMethod(nameof(RenderNodeCache.IsCachedFor)), Is.Not.Null);
            Assert.That(typeof(RenderNodeCache).GetMethod(nameof(RenderNodeCache.IsCacheRejectedFor)), Is.Not.Null);
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

    [Test]
    public void PersistentRenderNodeCacheWarmup_IsFrameOnlyForNonFriendCallers()
    {
        using var node = new PublicCacheProbeNode();

        Assert.Multiple(() =>
        {
            Assert.Throws<NotSupportedException>(() => RenderNodeCacheHelper.MakeCache(
                node,
                RenderCacheOptions.Disabled,
                RenderIntent.Preview,
                pullPurpose: RenderPullPurpose.Auxiliary));
            Assert.Throws<NotSupportedException>(() => RenderNodeCacheHelper.CreateDefaultCache(
                node,
                RenderCacheOptions.Default,
                RenderIntent.Preview,
                pullPurpose: RenderPullPurpose.Auxiliary));
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(node.Cache.IsCacheRejected, Is.False);
        });
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
        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            _ = context.Diagnostics;
            return context.Input;
        }

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

    public sealed record PublicUniformBinding(string Name) : UniformBinding(Name)
    {
        protected override void Apply(
            SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context)
            => builder.Uniforms[effectiveName] = context.WorkingScale;
    }

    // Compile-only coverage for the three shader-ownership modes in the public authoring contract:
    // graph-owned eager products, caller-owned cached products, and executor-owned deferred products.
    private static void ReferenceShaderOwnership(
        EffectGraphBuilder builder,
        SKShader graphOwnedSampler,
        SKShader graphOwnedChild,
        SKShader callerOwnedChild,
        IDisposable graphOwnedDisposable,
        Func<PassUniformContext, SKShader> deferredFactory)
    {
        _ = builder.Sampler("publicSampler", graphOwnedSampler);
        _ = builder.Child("publicChild", graphOwnedChild);
        _ = builder.Track(graphOwnedDisposable);
        _ = new ChildBinding("publicCachedChild", callerOwnedChild);
        _ = ChildBinding.Deferred("publicDeferredChild", deferredFactory);
    }

    // Compile-only coverage for the direct opaque-node appender and a runtime-count split. Containers normally use
    // builder.Effect(customResource); both lower-level factories remain public for authors that need them explicitly.
    private static void ReferenceDirectGraphBoundaries(
        EffectGraphBuilder builder,
        PublicCustomResource customResource,
        Action<ISplitEmitter> dynamicSplit)
    {
        _ = NestedGraphNodeDescriptor.Create(
            static (_, _) => { },
            null);
        builder.Split(SplitNodeDescriptor.Dynamic(dynamicSplit, "public-dynamic-split"));
        builder.CustomRenderNode(CustomRenderNodeDescriptor.Create(customResource));
    }

    // Compile-only coverage for explicit frame-cache policy from a non-friend plugin assembly.
    private static void ReferenceFrameCacheWarmup(RenderNode node)
    {
        RenderNodeCacheHelper.MakeCache(
            node,
            RenderCacheOptions.Default,
            RenderIntent.Preview,
            pullPurpose: RenderPullPurpose.Frame);
        RenderNodeCacheHelper.CreateDefaultCache(
            node,
            RenderCacheOptions.Default,
            RenderIntent.Preview,
            pullPurpose: RenderPullPurpose.Frame);
        _ = node.Cache.CachedRenderIntent;
        _ = node.Cache.CachedPullPurpose;
        _ = node.Cache.IsCachedFor(RenderIntent.Preview, RenderPullPurpose.Frame);
        _ = node.Cache.IsCacheRejectedFor(RenderIntent.Preview, RenderPullPurpose.Frame);
    }

    private sealed class PublicCacheProbeNode : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context) => [];
    }
}

// This must remain concrete, partial, property-backed, and top-level. Those constraints make this project a real
// non-friend compile gate for the same source-generator path an out-of-tree effect package uses.
public sealed partial class PublicAuthoringEffect : FilterEffect
{
    public PublicAuthoringEffect()
    {
        ScanProperties<PublicAuthoringEffect>();
    }

    public IProperty<float> PluginValue { get; } = Property.CreateAnimatable(1f);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        builder.Shader(ShaderNodeDescriptor.Snippet(
            "uniform float pluginValue; half4 apply(half4 c) { return c * pluginValue; }",
            uniforms => uniforms.Float("pluginValue", r.PluginValue)));
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
        builder.NestedGraph(NestedGraphNodeDescriptor.CreateStateful(
            static (nested, _) => nested.Saturate(1f),
            static liveBranchOrdinals => _ = liveBranchOrdinals.Count,
            "public-stateful-nested-graph"));

        if (resource is EffectAuthoringPublicApiTests.PublicCustomResource custom)
            builder.Effect(custom);
    }
}
