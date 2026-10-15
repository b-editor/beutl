using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderScaleMappingContractTests
{
    private static readonly Rect s_sourceBounds = new(0, 0, 10, 10);

    [Test]
    public void MapInputSupply_LetsExternalAuthorsRaiseTheInputDemandOfAnEnlargingScope()
    {
        var probe = new MaterializationDensityProbe();
        using var node = new EnlargingScopeNode(probe);
        using var renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(probe.ObservedWorkingScale, Is.EqualTo(2f));
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
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                OutputScale = 1,
                MaxWorkingScale = 4,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            });

    private static RenderFragmentHandle ProbeSource(RenderNodeContext context, MaterializationDensityProbe probe)
        => context.OpaqueSource(OpaqueRenderDescription.Create(
            probe,
            static (session, state) => state.Execute(session),
            bounds: OpaqueRenderBoundsContract.Source(s_sourceBounds),
            hitTest: RenderHitTestContract.OutputBounds,
            valueCardinality: RenderValueCardinality.Single,
            scale: RenderScaleContract.Vector));

    private static Rect Enlarge(Rect inputBounds)
        => new(inputBounds.X * 2, inputBounds.Y * 2, inputBounds.Width * 2, inputBounds.Height * 2);

    private static Rect Shrink(Rect outputBounds)
        => new(outputBounds.X / 2, outputBounds.Y / 2, outputBounds.Width / 2, outputBounds.Height / 2);

    private static EffectiveScale DoubleDemand(EffectiveScale outputDemand)
        => EffectiveScale.At(outputDemand.Value * 2);

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

    private sealed class EnlargingScopeNode(MaterializationDensityProbe probe) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = ProbeSource(context, probe);
            context.Publish(context.TargetScope(source, TargetScopeDescription.Create(
                nameof(EnlargingScopeNode),
                static (session, _) => session.Canvas.Use(canvas =>
                {
                    using (canvas.PushTransform(Matrix.CreateScale(2, 2)))
                        session.ReplayInput();
                }),
                RenderBoundsContract.Create(Enlarge, Shrink),
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.MapInputSupply(HalveSupply, DoubleDemand),
                transformSpace: RenderScopeTransformSpace.InputLogical)));
        }

        private static EffectiveScale HalveSupply(EffectiveScale inputSupply)
            => inputSupply.IsUnbounded
                ? EffectiveScale.Unbounded
                : EffectiveScale.At(inputSupply.Value / 2);
    }
}
