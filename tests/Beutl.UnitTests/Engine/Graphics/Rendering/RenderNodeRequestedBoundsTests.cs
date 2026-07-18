using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class RenderNodeRequestedBoundsTests
{
    [Test]
    public void TransformRenderNode_MapsRequestedBoundsIntoChildSpace()
    {
        var child = new RequestedBoundsProbeNode(new Rect(0, 0, 3840, 2160));
        using var transform = new TransformRenderNode(
            Matrix.CreateTranslation(-960, -540),
            TransformOperator.Prepend);
        transform.AddChild(child);

        var requested = new Rect(0, 0, 1920, 1080);
        var processor = new RenderNodeProcessor(
            transform,
            useRenderCache: false,
            RenderIntent.Delivery)
        {
            RequestedBounds = requested,
        };

        RenderNodeOperation[] operations = processor.PullToRoot();
        try
        {
            Assert.That(
                child.ObservedRequestedBounds,
                Is.EqualTo(new Rect(960, 540, 1920, 1080)),
                "The root-frame ROI must be inverse-transformed before an effect below the transform resolves its local ROI.");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(operations);
        }
    }

    [TestCase(TransformOperator.Append)]
    [TestCase(TransformOperator.Set)]
    public void TransformRenderNode_NonPrependOperator_DisablesBackwardRoi(TransformOperator transformOperator)
    {
        using var transform = new TransformRenderNode(
            Matrix.CreateTranslation(-960, -540),
            transformOperator);

        Rect mapped = transform.MapRequestedBoundsToChild(new Rect(0, 0, 1920, 1080));

        Assert.That(mapped.IsInvalid, Is.True,
            "Append and Set depend on the incoming canvas transform, so they cannot safely map root-frame ROI bounds.");
    }

    [Test]
    public void FilterEffectRenderNode_DoesNotPreCropChildBeforeResolvingItsOwnBackwardRoi()
    {
        var child = new RequestedBoundsProbeNode(new Rect(0, 0, 3840, 2160));
        var blur = new Blur { IsEnabled = false };
        using var resource = (FilterEffect.Resource)blur.ToResource(CompositionContext.Default);
        using var effect = new PlanFilterEffectRenderNode(resource);
        effect.AddChild(child);

        var processor = new RenderNodeProcessor(
            effect,
            useRenderCache: false,
            RenderIntent.Delivery)
        {
            RequestedBounds = new Rect(960, 540, 320, 180),
        };

        RenderNodeOperation[] operations = processor.PullToRoot();
        try
        {
            Assert.That(child.ObservedRequestedBounds.IsInvalid, Is.True,
                "the effect must pull its complete input before its graph expands and resolves the requested ROI");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(operations);
        }
    }

    private sealed class RequestedBoundsProbeNode(Rect bounds) : RenderNode
    {
        public Rect ObservedRequestedBounds { get; private set; } = Rect.Invalid;

        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            ObservedRequestedBounds = context.RequestedBounds;
            return [RenderNodeOperation.CreateLambda(bounds, _ => { })];
        }
    }
}
