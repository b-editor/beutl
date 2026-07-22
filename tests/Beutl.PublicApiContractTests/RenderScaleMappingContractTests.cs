using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderScaleMappingContractTests
{
    [TestCase(2, 4)]
    public void MapInputSupply_IsUsableByExternalRenderNodeAuthors(
        float inputDensity,
        float expectedDensity)
    {
        using var node = new SupplyMappingNode(EffectiveScale.At(inputDensity));
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions { UseRenderCache = false });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(expectedDensity)));
    }

    [Test]
    public void MapInputSupply_AllowsExternalAuthorsToPreserveUnboundedSupply()
    {
        using var node = new SupplyMappingNode(EffectiveScale.Unbounded);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions { UseRenderCache = false });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.Unbounded));
    }

    private sealed class SupplyMappingNode(EffectiveScale inputSupply) : RenderNode
    {
        private static readonly Rect s_bounds = new(0, 0, 20, 10);

        public override void Process(RenderNodeContext context)
        {
            RenderScaleContract sourceScale = inputSupply.IsUnbounded
                ? RenderScaleContract.Vector
                : RenderScaleContract.Custom(
                    new FixedScaleResolver(inputSupply.Value).Resolve,
                    structuralKey: (typeof(SupplyMappingNode), inputSupply));
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                execute: static _ => throw new AssertionException("Measurement must not execute opaque callbacks."),
                bounds: RenderOperationBoundsContract.Source(s_bounds),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: sourceScale,
                structuralKey: (typeof(SupplyMappingNode), "source", inputSupply)));
            RenderFragmentHandle mapped = context.OpaqueMap(source, OpaqueRenderDescription.Create(
                execute: static _ => throw new AssertionException("Measurement must not execute opaque callbacks."),
                bounds: RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.MapInputSupply(
                    DoubleSupply,
                    structuralKey: (typeof(SupplyMappingNode), "double")),
                structuralKey: (typeof(SupplyMappingNode), "map")));
            context.Publish(mapped);
        }

        private static EffectiveScale DoubleSupply(EffectiveScale input)
            => input.IsUnbounded
                ? EffectiveScale.Unbounded
                : EffectiveScale.At(input.Value * 2);

        private readonly record struct FixedScaleResolver(float Value)
        {
            public float Resolve(RenderScaleContext _) => Value;
        }
    }
}
