using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class RecordingSideEffectTests
{
    [Test]
    public void RecordingDeferredShapes_LeavesEveryExecutionProbeAtZero()
    {
        var tripwire = new SideEffectTripwire();
        var targetFactory = new CountingTargetFactory();
        using var node = new DeferredShapeProbeNode(tripwire);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = DeferredShapeProbeNode.Bounds,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        }, targetFactory);

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(tripwire.Touches, Is.Zero,
                "Recording and metadata resolution must not execute any deferred callback.");
            Assert.That(targetFactory.CreateCalls, Is.Zero);
        });
    }

    private sealed class DeferredShapeProbeNode(SideEffectTripwire tripwire) : RenderNode
    {
        public static Rect Bounds { get; } = new(0, 0, 64, 36);

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(CreateOpaque(
                OpaqueRenderBoundsContract.Source(Bounds),
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                inputCount: 0));
            RenderFragmentHandle mapped = context.OpaqueMap(
                source,
                CreateOpaque(
                    OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                    RenderValueCardinality.Single,
                    RenderScaleContract.PreserveInputSupply,
                    inputCount: 1));
            RenderFragmentHandle combined = context.OpaqueCombine(
                [source, mapped],
                CreateOpaque(
                    OpaqueRenderBoundsContract.Combine(
                        static inputs => inputs.Aggregate(static (left, right) => left.Union(right)),
                        static (output, inputs) => inputs.Select(_ => output).ToArray()),
                    RenderValueCardinality.Single,
                    RenderScaleContract.MaterializeAtWorkingScale,
                    inputCount: 2));
            RenderFragmentHandle expanded = context.OpaqueExpand(
                [source],
                CreateOpaque(
                    OpaqueRenderBoundsContract.FullInputs(
                        static inputs => inputs.Aggregate(static (left, right) => left.Union(right))),
                    RenderValueCardinality.Dynamic,
                    RenderScaleContract.MaterializeAtWorkingScale,
                    inputCount: 1));
            RenderFragmentHandle shader = context.Shader(
                source,
                ShaderDescription.CurrentPixel(
                    "half4 apply(half4 color) { return color; }"));
            RenderFragmentHandle geometry = context.Geometry(
                source,
                GeometryDescription.CreateRequestLocal(
                    _ => tripwire.TouchAll(),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    requiresReadback: true));
            RenderFragmentHandle capture = context.TargetCapture(
                TargetCaptureDescription.Create(
                    TargetRegion.Full,
                    Bounds,
                    RenderHitTestContract.OutputBounds,
                    TargetCaptureScaleContract.MaterializeAtWorkingScale));
            RenderFragmentHandle command = context.TargetCommand(
                [source],
                TargetCommandDescription.CreateRequestLocal(
                    _ => tripwire.TouchAll(),
                    TargetRegion.Full,
                    Bounds,
                    RenderHitTestContract.OutputBounds,
                    TargetAccess.Readback,
                    inputReadbacks: [RenderInputReadback.All]));
            RenderFragmentHandle scope = context.TargetScope(
                source,
                TargetScopeDescription.CreateRequestLocal(
                    _ => tripwire.TouchAll(),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    deviceGridSensitivity: RenderDeviceGridSensitivity.PhaseDependent));
            RenderFragmentHandle rawScope = context.RawTargetScope(
                source,
                RawTargetScopeDescription.CreateRequestLocal(
                    _ => tripwire.TouchAll(),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply));
            RenderFragmentHandle rawCommand = context.RawTargetCommand(
                RawTargetCommandDescription.CreateRequestLocal(
                    _ => tripwire.TouchAll(),
                    Bounds,
                    RenderHitTestContract.OutputBounds));

            context.PublishRange(
                [source, mapped, combined, expanded, shader, geometry, capture, command, scope, rawScope, rawCommand]);
        }

        private OpaqueRenderDescription CreateOpaque(
            OpaqueRenderBoundsContract bounds,
            RenderValueCardinality cardinality,
            RenderScaleContract scale,
            int inputCount)
        {
            return OpaqueRenderDescription.CreateRequestLocal(
                _ => tripwire.TouchAll(),
                bounds,
                RenderHitTestContract.OutputBounds,
                cardinality,
                scale,
                inputReadbacks: Enumerable.Repeat(RenderInputReadback.All, inputCount));
        }
    }

    private sealed class CountingTargetFactory : IRenderTargetFactory
    {
        public int CreateCalls { get; private set; }

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            CreateCalls++;
            throw new AssertionException(
                $"Recording unexpectedly requested a {deviceSize.Width}x{deviceSize.Height} render target.");
        }
    }

    private sealed class SideEffectTripwire
    {
        public int Touches { get; private set; }

        public void TouchAll() => Touches++;
    }
}
