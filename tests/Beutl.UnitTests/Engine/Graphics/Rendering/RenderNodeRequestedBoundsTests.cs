using Beutl.Graphics;
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
