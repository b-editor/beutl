using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class SymbolicSupplyMappingTests
{
    [Test]
    public void CustomTransform_MapsSupplyAfterSymbolicInputResolution()
    {
        var targetDomain = new Rect(0, 0, 10_000, 100);
        var effect = new SymbolicDomainFilterEffect();
        var filter = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        filter.AddChild(new EllipseRenderNode(
            new Rect(5, 6, 20, 12),
            Brushes.Resource.White,
            null));
        using Transform.Resource scale = new ScaleTransform(50, 50)
            .ToResource(CompositionContext.Default);
        using var root = ScaleRecordingTestHelper.SubtreePipeline(
            filter,
            new HalfInputSupplyRenderNode(),
            new DrawableGroup.CustomTransformRenderNode(
                scale,
                default,
                targetDomain.Size,
                AlignmentX.Left,
                AlignmentY.Top,
                new MemoryNode<Rect>(targetDomain)));

        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.Measure(
            root,
            outputScale: 2,
            targetDomain: targetDomain);
        Rect inputDomain = targetDomain.TransformToAABB(scale.Matrix.Invert());
        float expected = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(inputDomain, 2);

        Assert.Multiple(() =>
        {
            Assert.That(expected, Is.LessThan(2), "The setup must resolve the symbolic input below its recorded scale.");
            Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False);
            Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(expected).Within(1e-6f));
        });
    }

    private sealed class HalfInputSupplyRenderNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            foreach (RenderFragmentHandle input in context.Inputs)
            {
                OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
                    execute: static _ => throw new AssertionException(
                        "Metadata analysis must not execute opaque callbacks."),
                    bounds: OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                    hitTest: RenderHitTestContract.AnyInput,
                    valueCardinality: RenderValueCardinality.Single,
                    scale: RenderScaleContract.MapInputSupplyPreservingDemand(
                        HalfSupply));
                context.Publish(context.OpaqueMap(input, description));
            }
        }

        // EffectiveScale.Unbounded is a get-only property this assembly reads as metadata, where a
        // recording callback cannot be shown what its getter answers; snapshotting it here can be.
        private static readonly EffectiveScale s_unbounded = EffectiveScale.Unbounded;

        private static EffectiveScale HalfSupply(EffectiveScale input)
            => input.IsUnbounded
                ? s_unbounded
                : EffectiveScale.At(input.Value / 2);
    }
}
