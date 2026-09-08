using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderScaleMappingContractTests
{
    // Read once here rather than inside the callback: Colors.White is a get-only property whose getter this
    // compilation cannot see, so a callback naming it is not shown to answer the same way twice.
    private static readonly Color s_white = Colors.White;

    [TestCase(2, 4)]
    public void MapInputSupplyPreservingDemand_IsUsableByExternalRenderNodeAuthors(
        float inputDensity,
        float expectedDensity)
    {
        using var node = new SupplyMappingNode(EffectiveScale.At(inputDensity));
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(expectedDensity)));
    }

    [Test]
    public void MapInputSupplyPreservingDemand_AllowsExternalAuthorsToPreserveUnboundedSupply()
    {
        using var node = new SupplyMappingNode(EffectiveScale.Unbounded);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
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
    public void MapInputSupplyPreservingDemand_PassesOutputDemandThroughUnchanged()
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
    public void AWholeSourceShaderCanRaiseTheDemandOnTheInputItEnlarges()
    {
        var probe = new MaterializationDensityProbe();
        using var node = new EnlargingShaderNode(probe, mapsOutputDemand: true);
        using var renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(probe.ObservedWorkingScale, Is.EqualTo(2f));
        });
    }

    [Test]
    public void AWholeSourceShaderThatDeclaresNoDemandMappingPassesItThroughUnchanged()
    {
        var probe = new MaterializationDensityProbe();
        using var node = new EnlargingShaderNode(probe, mapsOutputDemand: false);
        using var renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(probe.ObservedWorkingScale, Is.EqualTo(1f));
        });
    }

    [Test]
    public void ACombineCanRaiseTheDemandOfOnlyTheInputItEnlarges()
    {
        var enlarged = new MaterializationDensityProbe();
        var passedThrough = new MaterializationDensityProbe();
        using var node = new AsymmetricCombineNode(enlarged, passedThrough, mapsPerInputDemand: true);
        using var renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(enlarged.ObservedWorkingScale, Is.EqualTo(2f), "the enlarged input");
            Assert.That(passedThrough.ObservedWorkingScale, Is.EqualTo(1f), "the input it leaves alone");
        });
    }

    [Test]
    public void ACombineThatDeclaresNoPerInputDemandAsksEveryInputForTheSameDensity()
    {
        var enlarged = new MaterializationDensityProbe();
        var passedThrough = new MaterializationDensityProbe();
        using var node = new AsymmetricCombineNode(enlarged, passedThrough, mapsPerInputDemand: false);
        using var renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(enlarged.ObservedWorkingScale, Is.EqualTo(1f));
            Assert.That(passedThrough.ObservedWorkingScale, Is.EqualTo(1f));
        });
    }

    [Test]
    public void APerInputDemandMappingIsRejectedOnATopologyThatCannotCarryIt()
    {
        using var node = new PerInputDemandOnAMapNode();
        using var renderer = CreateRenderer(node);

        Assert.That(
            () => renderer.Measure(),
            Throws.ArgumentException.With.Message.Contains("per-input demand mapping"));
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
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                OutputScale = 1,
                MaxWorkingScale = 4,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
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

    private sealed class EnlargingTargetCommandNode(
        MaterializationDensityProbe probe,
        bool mapsOutputDemand) : RenderNode
    {
        private static readonly Rect s_sourceBounds = new(0, 0, 10, 10);
        private static readonly Rect s_targetBounds = new(0, 0, 20, 20);

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                probe,
                static (session, state) => state.Execute(session),
                bounds: OpaqueRenderBoundsContract.Source(s_sourceBounds),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Vector));
            TargetCommandDescription description = TargetCommandDescription.Create(
                (byte)0,
                static (session, _) => session.Canvas.Use(canvas =>
                {
                    using (canvas.PushTransform(Matrix.CreateScale(2, 2)))
                    {
                        session.Inputs[0].Draw(canvas);
                    }
                }),
                TargetRegion.Region(s_targetBounds),
                s_targetBounds,
                RenderHitTestContract.OutputBounds,
                inputDemand: mapsOutputDemand
                    ? RenderInputDemandContract.MapOutputDemandToInput(DoubleDemand)
                    : default);
            context.Publish(context.TargetCommand([source], description));
        }

        private static EffectiveScale DoubleDemand(EffectiveScale outputDemand)
            => EffectiveScale.At(outputDemand.Value * 2);
    }

    private sealed class EnlargingGeometryNode(
        MaterializationDensityProbe probe,
        bool mapsOutputDemand) : RenderNode
    {
        private static readonly Rect s_sourceBounds = new(0, 0, 10, 10);

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                probe,
                static (session, state) => state.Execute(session),
                bounds: OpaqueRenderBoundsContract.Source(s_sourceBounds),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Vector));
            GeometryDescription description = GeometryDescription.Create(
                (byte)0,
                static (session, _) => session.Canvas.Use(canvas =>
                {
                    using (canvas.PushTransform(Matrix.CreateScale(2, 2)))
                    {
                        session.Input.Draw(canvas);
                    }
                }),
                RenderBoundsContract.Create(Enlarge, Shrink),
                RenderHitTestContract.OutputBounds,
                inputDemand: mapsOutputDemand
                    ? RenderInputDemandContract.MapOutputDemandToInput(DoubleDemand)
                    : default);
            context.Publish(context.Geometry(source, description));
        }

        private static Rect Enlarge(Rect inputBounds)
            => new(inputBounds.X * 2, inputBounds.Y * 2, inputBounds.Width * 2, inputBounds.Height * 2);

        private static Rect Shrink(Rect outputBounds)
            => new(outputBounds.X / 2, outputBounds.Y / 2, outputBounds.Width / 2, outputBounds.Height / 2);

        private static EffectiveScale DoubleDemand(EffectiveScale outputDemand)
            => EffectiveScale.At(outputDemand.Value * 2);
    }

    private sealed class EnlargingMapNode(
        MaterializationDensityProbe probe,
        bool mapsOutputDemand) : RenderNode
    {
        private static readonly Rect s_sourceBounds = new(0, 0, 10, 10);

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                probe,
                static (session, state) => state.Execute(session),
                bounds: OpaqueRenderBoundsContract.Source(s_sourceBounds),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Vector));
            RenderScaleContract scale = mapsOutputDemand
                ? RenderScaleContract.MapInputSupply(HalveSupply, DoubleDemand)
                : RenderScaleContract.MapInputSupplyPreservingDemand(HalveSupply);
            RenderFragmentHandle enlarged = context.OpaqueMap(source, RenderDescriptionFactory.Opaque(
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

    private sealed class EnlargingShaderNode(
        MaterializationDensityProbe probe,
        bool mapsOutputDemand) : RenderNode
    {
        private const string EnlargingSource =
            "uniform shader src; half4 main(float2 coord) { return src.eval(coord * 0.5); }";

        private static readonly Rect s_sourceBounds = new(0, 0, 10, 10);

        private static readonly ShaderDescription s_mapsDemand =
            ShaderDescription.WholeSource(
                EnlargingSource,
                RenderBoundsContract.Create(Enlarge, Shrink),
                inputDemand: RenderInputDemandContract.MapOutputDemandToInput(DoubleDemand));

        private static readonly ShaderDescription s_leavesDemandUnchanged =
            ShaderDescription.WholeSource(
                EnlargingSource,
                RenderBoundsContract.Create(Enlarge, Shrink));

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                probe,
                static (session, state) => state.Execute(session),
                bounds: OpaqueRenderBoundsContract.Source(s_sourceBounds),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Vector));
            ShaderDescription description = mapsOutputDemand ? s_mapsDemand : s_leavesDemandUnchanged;
            context.Publish(context.Shader(source, description));
        }

        private static Rect Enlarge(Rect inputBounds)
            => new(inputBounds.X * 2, inputBounds.Y * 2, inputBounds.Width * 2, inputBounds.Height * 2);

        private static Rect Shrink(Rect outputBounds)
            => new(outputBounds.X / 2, outputBounds.Y / 2, outputBounds.Width / 2, outputBounds.Height / 2);

        private static EffectiveScale DoubleDemand(EffectiveScale outputDemand)
            => EffectiveScale.At(outputDemand.Value * 2);
    }

    private sealed class AsymmetricCombineNode(
        MaterializationDensityProbe enlarged,
        MaterializationDensityProbe passedThrough,
        bool mapsPerInputDemand) : RenderNode
    {
        private static readonly Rect s_sourceBounds = new(0, 0, 10, 10);

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle first = Source(context, enlarged);
            RenderFragmentHandle second = Source(context, passedThrough);
            context.Publish(context.OpaqueCombine(
                [first, second],
                OpaqueRenderDescription.Create(
                    nameof(AsymmetricCombineNode),
                    static (session, _) =>
                    {
                        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                        output.Canvas.Use(static canvas => canvas.Clear(s_white));
                        session.Publish(output);
                    },
                    OpaqueRenderBoundsContract.Combine(
                        static inputs => inputs[0].Union(inputs[1]),
                        static (_, inputs) => inputs),
                    RenderHitTestContract.OutputBounds,
                    RenderValueCardinality.Single,
                    RenderScaleContract.MaterializeAtWorkingScale,
                    inputDemand: mapsPerInputDemand
                        ? RenderInputDemandContract.MapOutputDemandPerInput(DoubleTheFirstInput)
                        : default)));
        }

        private static RenderFragmentHandle Source(
            RenderNodeContext context,
            MaterializationDensityProbe probe)
            => context.OpaqueSource(OpaqueRenderDescription.Create(
                probe,
                static (session, state) => state.Execute(session),
                bounds: OpaqueRenderBoundsContract.Source(s_sourceBounds),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Vector));

        private static EffectiveScale DoubleTheFirstInput(int inputIndex, EffectiveScale outputDemand)
            => inputIndex == 0
                ? EffectiveScale.At(outputDemand.Value * 2)
                : outputDemand;
    }

    private sealed class PerInputDemandOnAMapNode : RenderNode
    {
        private static readonly Rect s_bounds = new(0, 0, 10, 10);

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(RenderDescriptionFactory.Opaque(
                execute: static _ => throw new AssertionException("Measurement must not execute opaque callbacks."),
                bounds: OpaqueRenderBoundsContract.Source(s_bounds),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Vector));
            context.Publish(context.OpaqueMap(source, RenderDescriptionFactory.Opaque(
                execute: static _ => throw new AssertionException("Measurement must not execute opaque callbacks."),
                bounds: OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.MaterializeAtWorkingScale,
                inputDemand: RenderInputDemandContract.MapOutputDemandToInput(Double))));
        }

        private static EffectiveScale Double(EffectiveScale outputDemand)
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
            RenderFragmentHandle source = context.OpaqueSource(RenderDescriptionFactory.Opaque(
                execute: static _ => throw new AssertionException("Measurement must not execute opaque callbacks."),
                bounds: OpaqueRenderBoundsContract.Source(s_bounds),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: sourceScale));
            RenderFragmentHandle mapped = context.OpaqueMap(source, RenderDescriptionFactory.Opaque(
                execute: static _ => throw new AssertionException("Measurement must not execute opaque callbacks."),
                bounds: OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.MapInputSupplyPreservingDemand(DoubleSupply)));
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
