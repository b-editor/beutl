using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderScaleMappingContractTests
{
    [TestCase(2, 4)]
    public void MapInputSupplyPreservingDemand_IsUsableByExternalRenderNodeAuthors(
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
    public void MapInputSupplyPreservingDemand_AllowsExternalAuthorsToPreserveUnboundedSupply()
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

    /// <remarks>
    /// A target scope enlarging its input while replaying it needs that input denser, and its scale
    /// contract is where it says so. Only the engine's internal value-replay map used to be asked.
    /// </remarks>
    [Test]
    public void ATargetScopeCanRaiseTheDemandOnTheInputItEnlarges()
    {
        var probe = new MaterializationDensityProbe();
        using var node = new EnlargingTargetScopeNode(probe, mapsOutputDemand: true);
        using var renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(probe.ObservedWorkingScale, Is.EqualTo(2f));
        });
    }

    [Test]
    public void ATargetScopeThatDeclaresNoDemandMappingPassesItThroughUnchanged()
    {
        var probe = new MaterializationDensityProbe();
        using var node = new EnlargingTargetScopeNode(probe, mapsOutputDemand: false);
        using var renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(probe.ObservedWorkingScale, Is.EqualTo(1f));
        });
    }

    /// <remarks>
    /// Geometry is a materialization boundary, so an operation that draws its input through an enlarging
    /// transform needs it denser. Without a contract to say so the source was rasterized at the density the
    /// consumer asked for and then stretched, with no public remedy.
    /// </remarks>
    [Test]
    public void AGeometryOperationCanRaiseTheDemandOnTheInputItEnlarges()
    {
        var probe = new MaterializationDensityProbe();
        using var node = new EnlargingGeometryNode(probe, mapsOutputDemand: true);
        using var renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(probe.ObservedWorkingScale, Is.EqualTo(2f));
        });
    }

    [Test]
    public void AGeometryOperationThatDeclaresNoDemandMappingPassesItThroughUnchanged()
    {
        var probe = new MaterializationDensityProbe();
        using var node = new EnlargingGeometryNode(probe, mapsOutputDemand: false);
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

    private sealed class EnlargingGeometryNode(
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
            GeometryDefinition<byte> definition = GeometryDefinition<byte>.Create(
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
            context.Publish(context.Geometry(source, definition.Call(default)));
        }

        private static Rect Enlarge(Rect inputBounds)
            => new(inputBounds.X * 2, inputBounds.Y * 2, inputBounds.Width * 2, inputBounds.Height * 2);

        private static Rect Shrink(Rect outputBounds)
            => new(outputBounds.X / 2, outputBounds.Y / 2, outputBounds.Width / 2, outputBounds.Height / 2);

        private static EffectiveScale DoubleDemand(EffectiveScale outputDemand)
            => EffectiveScale.At(outputDemand.Value * 2);
    }

    private sealed class EnlargingTargetScopeNode(
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
                : RenderScaleContract.MapInputSupplyPreservingDemand(HalveSupply);
            TargetScopeDefinition<byte> definition = TargetScopeDefinition<byte>.Create(
                static (session, _) => session.Canvas.Use(canvas =>
                {
                    using (canvas.PushTransform(Matrix.CreateScale(2, 2)))
                    {
                        session.ReplayInput();
                    }
                }),
                RenderBoundsContract.Create(Enlarge, Shrink),
                RenderHitTestContract.AnyInput,
                scale,
                resources: []);
            context.Publish(context.TargetScope(source, definition.Call(default)));
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
                : RenderScaleContract.MapInputSupplyPreservingDemand(HalveSupply);
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

    private sealed class EnlargingShaderNode(
        MaterializationDensityProbe probe,
        bool mapsOutputDemand) : RenderNode
    {
        private const string EnlargingSource =
            "uniform shader src; half4 main(float2 coord) { return src.eval(coord * 0.5); }";

        private static readonly Rect s_sourceBounds = new(0, 0, 10, 10);

        private static readonly ShaderDefinition<byte> s_mapsDemand =
            ShaderDefinition<byte>.WholeSource(
                EnlargingSource,
                RenderBoundsContract.Create(Enlarge, Shrink),
                inputDemand: RenderInputDemandContract.MapOutputDemandToInput(DoubleDemand));

        private static readonly ShaderDefinition<byte> s_leavesDemandUnchanged =
            ShaderDefinition<byte>.WholeSource(
                EnlargingSource,
                RenderBoundsContract.Create(Enlarge, Shrink));

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(RenderDefinitionCallFactory.Opaque(
                probe,
                static (session, state) => state.Execute(session),
                bounds: OpaqueRenderBoundsContract.Source(s_sourceBounds),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Vector));
            ShaderDefinition<byte> definition = mapsOutputDemand ? s_mapsDemand : s_leavesDemandUnchanged;
            context.Publish(context.Shader(source, definition.Call(0)));
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
                RenderDefinitionCallFactory.Opaque(
                    execute: static session =>
                    {
                        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                        output.Canvas.Use(static canvas => canvas.Clear(Colors.White));
                        session.Publish(output);
                    },
                    bounds: OpaqueRenderBoundsContract.Combine(
                        static inputs => inputs[0].Union(inputs[1]),
                        static (_, inputs) => inputs),
                    hitTest: RenderHitTestContract.OutputBounds,
                    valueCardinality: RenderValueCardinality.Single,
                    scale: RenderScaleContract.MaterializeAtWorkingScale,
                    inputDemand: mapsPerInputDemand
                        ? RenderInputDemandContract.MapOutputDemandPerInput(DoubleTheFirstInput)
                        : default)));
        }

        private static RenderFragmentHandle Source(
            RenderNodeContext context,
            MaterializationDensityProbe probe)
            => context.OpaqueSource(RenderDefinitionCallFactory.Opaque(
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
            RenderFragmentHandle source = context.OpaqueSource(RenderDefinitionCallFactory.Opaque(
                execute: static _ => throw new AssertionException("Measurement must not execute opaque callbacks."),
                bounds: OpaqueRenderBoundsContract.Source(s_bounds),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Vector));
            context.Publish(context.OpaqueMap(source, RenderDefinitionCallFactory.Opaque(
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
