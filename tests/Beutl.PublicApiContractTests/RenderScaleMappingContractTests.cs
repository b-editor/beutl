using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

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
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(expectedDensity)));
    }

    [Test]
    public void MapInputSupply_AllowsExternalAuthorsToPreserveUnboundedSupply()
    {
        using var node = new SupplyMappingNode(EffectiveScale.Unbounded);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.Unbounded));
    }

    [Test]
    public void MapInputSupply_LetsExternalAuthorsRaiseTheInputDemandOfAnEnlargingMap()
    {
        var probe = new MaterializationDensityProbe();
        using var node = new EnlargingMapNode(probe, mapsOutputDemand: true);
        using var renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(probe.ObservedWorkingScale, Is.EqualTo(2f));
        });
    }

    [Test]
    public void MapInputSupply_ForwardOnlyOverloadPassesOutputDemandThroughUnchanged()
    {
        var probe = new MaterializationDensityProbe();
        using var node = new EnlargingMapNode(probe, mapsOutputDemand: false);
        using var renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(probe.ObservedWorkingScale, Is.EqualTo(1f));
        });
    }

    [Test]
    public void MapInputSupply_ComposesTheEngineAffineDensityHelpersFromOutsideTheAssembly()
    {
        var mapper = new AffineDensityMapper(Matrix.CreateScale(2, 2));

        Assert.Multiple(() =>
        {
            Assert.That(
                () => RenderScaleContract.MapInputSupply(mapper.MapSupply, mapper.MapDemand),
                Throws.Nothing);
            Assert.That(mapper.MapSupply(EffectiveScale.At(4)), Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(mapper.MapSupply(EffectiveScale.Unbounded), Is.EqualTo(EffectiveScale.Unbounded));
            Assert.That(mapper.MapDemand(EffectiveScale.At(1)), Is.EqualTo(EffectiveScale.At(2)));
        });
    }

    [Test]
    public void AffineDensityHelpers_AreNotInversesUnderAnAnisotropicTransform()
    {
        var mapper = new AffineDensityMapper(Matrix.CreateScale(0.5f, 0.25f));

        Assert.Multiple(() =>
        {
            Assert.That(mapper.MapSupply(EffectiveScale.At(1)), Is.EqualTo(EffectiveScale.At(4)));
            Assert.That(mapper.MapDemand(EffectiveScale.At(1)), Is.EqualTo(EffectiveScale.At(0.5f)));
        });
    }

    private readonly record struct AffineDensityMapper(Matrix Transform)
    {
        public EffectiveScale MapSupply(EffectiveScale inputSupply)
            => TransformRenderNode.RescaleDensity(inputSupply, Transform);

        public EffectiveScale MapDemand(EffectiveScale outputDemand)
            => TransformRenderNode.RescaleDemand(outputDemand, Transform);
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    OutputScale = 1,
                    MaxWorkingScale = 4,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

    private sealed class MaterializationDensityProbe
    {
        public float ObservedWorkingScale { get; private set; } = float.NaN;

        public void Execute(OpaqueRenderSession session)
        {
            ObservedWorkingScale = session.WorkingScale;
            using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
            output.Canvas.Use(static canvas => canvas.Clear(Colors.White));
            session.Publish(output);
        }
    }

    private sealed class EnlargingMapNode(
        MaterializationDensityProbe probe,
        bool mapsOutputDemand) : RenderNode
    {
        private static readonly Rect s_sourceBounds = new(0, 0, 10, 10);

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(RenderDefinitionCallFactory.Opaque(
                probe,
                static (session, state) => state.Execute(session),
                bounds: OpaqueRenderBoundsContract.Source(s_sourceBounds),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Vector));
            RenderScaleContract scale = mapsOutputDemand
                ? RenderScaleContract.MapInputSupply(HalveSupply, DoubleDemand)
                : RenderScaleContract.MapInputSupply(HalveSupply);
            RenderFragmentHandle enlarged = context.OpaqueMap(source, RenderDefinitionCallFactory.Opaque(
                execute: static session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                bounds: OpaqueRenderBoundsContract.Map(RenderBoundsContract.Create(Enlarge, Shrink)),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: scale));
            context.Publish(enlarged);
        }

        private static Rect Enlarge(Rect inputBounds)
            => new(inputBounds.X * 2, inputBounds.Y * 2, inputBounds.Width * 2, inputBounds.Height * 2);

        private static Rect Shrink(Rect outputBounds)
            => new(outputBounds.X / 2, outputBounds.Y / 2, outputBounds.Width / 2, outputBounds.Height / 2);

        private static EffectiveScale HalveSupply(EffectiveScale inputSupply)
            => inputSupply.IsUnbounded
                ? EffectiveScale.Unbounded
                : EffectiveScale.At(inputSupply.Value / 2);

        private static EffectiveScale DoubleDemand(EffectiveScale outputDemand)
            => EffectiveScale.At(outputDemand.Value * 2);
    }

    private sealed class SupplyMappingNode(EffectiveScale inputSupply) : RenderNode
    {
        private static readonly Rect s_bounds = new(0, 0, 20, 10);

        public override void Process(RenderNodeContext context)
        {
            RenderScaleContract sourceScale = inputSupply.IsUnbounded
                ? RenderScaleContract.Vector
                : RenderScaleContract.Custom(
                    new FixedScaleResolver(inputSupply.Value).Resolve);
            RenderFragmentHandle source = context.OpaqueSource(RenderDefinitionCallFactory.Opaque(
                execute: static _ => throw new AssertionException("Measurement must not execute opaque callbacks."),
                bounds: OpaqueRenderBoundsContract.Source(s_bounds),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: sourceScale));
            RenderFragmentHandle mapped = context.OpaqueMap(source, RenderDefinitionCallFactory.Opaque(
                execute: static _ => throw new AssertionException("Measurement must not execute opaque callbacks."),
                bounds: OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.MapInputSupply(DoubleSupply)));
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
