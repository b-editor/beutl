using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

/// <summary>
/// An <see cref="RenderScopeTransformSpace.InputLogical"/> scope replays its input in the input's own logical
/// space, so the density such a replay asks of an unbounded input is the scope's demand mapped through its
/// scale contract, not the surface density of the canvas the scope draws on. These tests pin that the
/// executor's materialization cross-check compares in that space (#2385).
/// </summary>
[TestFixture]
public sealed class InputLogicalScopeCallerDensityTests
{
    private static readonly Rect s_bounds = new(0, 0, 64, 48);

    private static readonly Color s_white = Colors.White;

    [TestCase(0.5f)]
    [TestCase(2f)]
    public void AuthoredInputLogicalScope_ReplaysAnUnboundedInput(float scale)
    {
        using var producer = new ProbeSourceNode();
        using var root = new ScalingScopeNode(producer, scale);
        using var renderer = CreateRenderer(root, RenderCacheOptions.Disabled);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(producer.ExecuteCount, Is.EqualTo(1));
        });
    }

    [TestCase(0.5f)]
    [TestCase(2f)]
    public void AuthoredInputLogicalScope_CacheBoundaryReplayMatchesTheDirectRender(float scale)
    {
        using var controlProducer = new ProbeSourceNode();
        using var controlRoot = new ScalingScopeNode(controlProducer, scale);
        using var controlRenderer = CreateRenderer(controlRoot, RenderCacheOptions.Disabled);
        using RenderNodeRasterization control = controlRenderer.Rasterize();

        using var producer = new ProbeSourceNode();
        producer.Cache.RecordStableRequests();
        using var root = new ScalingScopeNode(producer, scale);
        using var renderer = CreateRenderer(root, RenderCacheOptions.Enabled);

        using RenderNodeRasterization miss = renderer.Rasterize();
        using RenderNodeRasterization hit = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(producer.Cache.IsCached, Is.True,
                "an insensitive source under an InputLogical scope must be admitted as a cache boundary");
            Assert.That(producer.ExecuteCount, Is.EqualTo(1),
                "the second frame must replay the boundary from the cache");
            Assert.That(miss.Bounds, Is.EqualTo(control.Bounds));
            Assert.That(hit.Bounds, Is.EqualTo(control.Bounds));
            Assert.That(GetPixels(miss), Is.EqualTo(GetPixels(control)),
                "the boundary capture frame must match the direct render");
            Assert.That(GetPixels(hit), Is.EqualTo(GetPixels(control)),
                "the boundary replay frame must match the direct render");
        });
    }

    /// <summary>
    /// A raw scope's scale contract is the only statement of how its replayed input is consumed, so its
    /// backward map carries the caller's demand to the input the same way an InputLogical guarded scope does.
    /// </summary>
    [TestCase(0.5f)]
    [TestCase(2f)]
    public void AuthoredRawScope_ReplaysAnUnboundedInput(float scale)
    {
        using var producer = new ProbeSourceNode();
        using var root = new ScalingScopeNode(producer, scale, raw: true);
        using var renderer = CreateRenderer(root, RenderCacheOptions.Disabled);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(GetPixels(rasterization), Has.Some.Not.Zero);
            Assert.That(producer.ExecuteCount, Is.EqualTo(1));
        });
    }

    [TestCase(0.5f)]
    [TestCase(2f)]
    public void PrependTransform_ReplaysAnUnboundedInput(float scale)
    {
        using var producer = new ProbeSourceNode();
        using var root = new TransformRenderNode(Matrix.CreateScale(scale, scale), TransformOperator.Prepend);
        root.AddChild(producer);
        using var renderer = CreateRenderer(root, RenderCacheOptions.Disabled);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(producer.ExecuteCount, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A consumer that declares its input's demand lower than its own through <see cref="RenderInputDemandContract"/>
    /// asks the input for that mapped density, not for the consumer's own: the demand resolver compiled the
    /// input at the mapped value, and a caller above it would trip the materialization cross-check.
    /// </summary>
    [TestCase(DemandReducingConsumerKind.Geometry)]
    [TestCase(DemandReducingConsumerKind.Shader)]
    [TestCase(DemandReducingConsumerKind.OpaqueCombine)]
    public void DemandReducingConsumer_AsksItsUnboundedInputForTheMappedDemand(DemandReducingConsumerKind kind)
    {
        using var producer = new ProbeSourceNode();
        using var root = new DemandReducingConsumerNode(producer, kind);
        using var renderer = CreateRenderer(root, RenderCacheOptions.Disabled);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(producer.ExecuteCount, Is.EqualTo(1));
        });
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode root, RenderCacheOptions cacheOptions)
        => new(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_bounds,
            OutputScale = 1,
            MaxWorkingScale = 4,
            CacheOptions = cacheOptions,
            Purpose = RenderRequestPurpose.Frame,
        }, new CpuTargetFactory());

    private static byte[] GetPixels(RenderNodeRasterization rasterization)
    {
        Assert.That(rasterization.IsEmpty, Is.False);
        return rasterization.Bitmap!.GetPixelSpan().ToArray();
    }

    /// <summary>
    /// A guarded scope that draws its input through a uniform scale expressed in the input's own logical
    /// space and declares the matching density relationship, the way <see cref="TransformRenderNode"/> does
    /// for <see cref="TransformOperator.Prepend"/>.
    /// </summary>
    private sealed class ScalingScopeNode(RenderNode producer, float scale, bool raw = false) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.RecordNode(producer, []).Single();
            RenderBoundsContract bounds = RenderBoundsContract.Create(
                scale,
                static (factor, bounds) => bounds.TransformToAABB(Matrix.CreateScale(factor, factor)),
                static (factor, bounds) => bounds.TransformToAABB(Matrix.CreateScale(1 / factor, 1 / factor)));
            RenderScaleContract scaleContract = RenderScaleContract.MapInputSupply(
                scale,
                static (factor, supply) => supply.IsUnbounded
                    ? supply
                    : EffectiveScale.At(supply.Value / factor),
                static (factor, demand) => EffectiveScale.At(demand.Value * factor));
            if (raw)
            {
                context.Publish(context.RawTargetScope(
                    input,
                    RawTargetScopeDescription.Create(
                        scale,
                        static (session, factor) =>
                        {
                            using (session.Canvas.PushTransform(Matrix.CreateScale(factor, factor)))
                            {
                                session.ReplayInput();
                            }
                        },
                        bounds,
                        RenderHitTestContract.None,
                        scaleContract)));
                return;
            }

            TargetScopeDescription description = TargetScopeDescription.Create(
                scale,
                static (session, factor) => session.Canvas.Use(canvas =>
                {
                    using (canvas.PushTransform(Matrix.CreateScale(factor, factor)))
                    {
                        session.ReplayInput();
                    }
                }),
                bounds,
                RenderHitTestContract.None,
                scaleContract,
                RenderDeviceGridSensitivity.Insensitive,
                RenderDeviceGridMapping.Remapped,
                RenderScopeTransformSpace.InputLogical);
            context.Publish(context.TargetScope(input, description));
        }

        protected override void OnDispose(bool disposing)
        {
            producer.Dispose();
            base.OnDispose(disposing);
        }
    }

    public enum DemandReducingConsumerKind
    {
        Geometry,
        Shader,
        OpaqueCombine,
    }

    /// <summary>
    /// Consumes one input at three quarters of its own demand through each fragment kind that carries a
    /// <see cref="RenderInputDemandContract"/>.
    /// </summary>
    private sealed class DemandReducingConsumerNode(RenderNode producer, DemandReducingConsumerKind kind) : RenderNode
    {
        private const float InputDemandFactor = 0.75f;

        private static readonly RenderInputDemandContract s_inputDemand =
            RenderInputDemandContract.MapOutputDemandToInput(
                static demand => EffectiveScale.At(demand.Value * InputDemandFactor));

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.RecordNode(producer, []).Single();
            RenderFragmentHandle output = kind switch
            {
                DemandReducingConsumerKind.Geometry => context.Geometry(
                    input,
                    GeometryDescription.Create(
                        state: true,
                        static (session, _) => session.Canvas.Use(session.Input.Draw),
                        RenderBoundsContract.Identity,
                        RenderHitTestContract.AnyInput,
                        inputDemand: s_inputDemand)),
                DemandReducingConsumerKind.Shader => context.Shader(
                    input,
                    ShaderDescription.WholeSource(
                        "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
                        RenderBoundsContract.Identity,
                        inputDemand: s_inputDemand)),
                DemandReducingConsumerKind.OpaqueCombine => context.OpaqueCombine(
                    [input],
                    OpaqueRenderDescription.Create(
                        s_bounds,
                        static (session, bounds) =>
                        {
                            using OpaqueRenderOutput output = session.CreateOutput(bounds);
                            output.Canvas.Use(canvas =>
                            {
                                foreach (RenderExecutionInput item in session.Inputs)
                                    item.Draw(canvas);
                            });
                            session.Publish(output);
                        },
                        OpaqueRenderBoundsContract.FullInputs(static _ => s_bounds),
                        RenderHitTestContract.OutputBounds,
                        RenderValueCardinality.Single,
                        RenderScaleContract.MaterializeAtWorkingScale,
                        inputDemand: s_inputDemand)),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            context.Publish(output);
        }

        protected override void OnDispose(bool disposing)
        {
            producer.Dispose();
            base.OnDispose(disposing);
        }
    }

    /// <summary>
    /// An unbounded, device-grid-insensitive source with no direct replay, so replaying it always
    /// materializes it and the cache resolver may select it as a boundary.
    /// </summary>
    private sealed class ProbeSourceNode : RenderNode
    {
        private static readonly RenderResourceSlot<ExecutionProbe> s_probeSlot = new();
        private readonly ExecutionProbe _probe = new();

        public int ExecuteCount => _probe.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<ExecutionProbe> probeResource = context.Borrow(_probe);
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                s_bounds,
                static (session, bounds) => session.UseResource(s_probeSlot, probe =>
                {
                    probe.Record();
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    output.Canvas.Use(canvas => canvas.Clear(s_white));
                    session.Publish(output);
                }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.None,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector,
                deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
                resources: [s_probeSlot.Bind(probeResource)])));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
