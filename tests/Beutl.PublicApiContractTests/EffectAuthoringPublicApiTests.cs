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
            Assert.That(typeof(PublicCustomResource).IsSubclassOf(typeof(CustomRenderNodeFilterEffect.Resource)),
                Is.True);
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
                static context => context.CopySourceToDestination(),
                passCount: 1,
                ComputeFallback.Identity,
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
        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => FilterEffectRenderNodeFactory.Of<PublicCustomResource, PublicCustomRenderNode>(
                static resource => new PublicCustomRenderNode(resource));
    }

    public sealed class PublicCustomRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context) => context.Input;
    }
}
