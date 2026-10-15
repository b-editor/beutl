using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class OpaqueOutputPublicationContractTests
{
    private static readonly Rect s_domain = new(0, 0, 32, 32);
    private static readonly Rect s_sourceBounds = new(0, 0, 32, 32);

    private static TargetCommandDescription RecordingCommand(PublishedBoundsRecorder recorder)
        => TargetCommandDescription.Create(
            recorder,
            static (session, current) =>
            {
                foreach (RenderExecutionInput input in session.Inputs)
                    current.Record(input.Bounds);
            },
            TargetRegion.Region(s_domain),
            s_domain,
            RenderHitTestContract.OutputBounds);

    // A zero-extent rectangle at an integer origin is zero device pixels wide, while one at a fractional
    // origin covers a device pixel, because a device rectangle floors its left edge and ceils its right.
    [TestCase(8f, 8f)]
    [TestCase(10.5f, 20.5f)]
    public void AnOutputShrunkToNothing_IsDroppedWhereTheOperationMayPublishNothing(float x, float y)
    {
        var recorder = new PublishedBoundsRecorder();
        using var node = new SourceNode(
            new PublishPlan(new Rect(x, y, 0, 0)),
            RenderValueCardinality.ZeroOrOne,
            recorder);

        Assert.That(() => RenderOnce(node), Throws.Nothing);
        Assert.That(recorder.Records, Is.Empty, "an output shrunk to nothing carries no value downstream");
    }

    [Test]
    public void AnOutputShrunkToARealRectangle_ReachesTheNextOperationCroppedToIt()
    {
        var selection = new Rect(4, 4, 16, 16);
        var recorder = new PublishedBoundsRecorder();
        using var node = new SourceNode(
            new PublishPlan(selection),
            RenderValueCardinality.ZeroOrOne,
            recorder);

        RenderOnce(node);

        Assert.That(recorder.Records, Is.EqualTo(new[] { selection }));
    }

    private static void RenderOnce(RenderNode node)
    {
        using RenderNodeRenderer renderer = new(
            node,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = s_domain,
                OutputScale = 1,
                MaxWorkingScale = 1,
                CacheOptions = RenderCacheOptions.Disabled,
                Purpose = RenderRequestPurpose.Frame,
            });
        renderer.Rasterize().Dispose();
    }

    /// <param name="Selection">The rectangle to shrink the output to before publishing it.</param>
    private sealed record PublishPlan(Rect Selection)
    {
        public void Execute(OpaqueRenderSession session)
        {
            using OpaqueRenderOutput output = session.CreateOutput(s_sourceBounds);
            output.SetOutputBounds(Selection);
            session.Publish(output);
        }
    }

    private sealed class PublishedBoundsRecorder
    {
        private readonly List<Rect> _records = [];

        public IReadOnlyList<Rect> Records => _records;

        public void Record(Rect bounds) => _records.Add(bounds);
    }

    private sealed class SourceNode(
        PublishPlan plan,
        RenderValueCardinality cardinality,
        PublishedBoundsRecorder recorder) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription source = OpaqueRenderDescription.Create(
                plan,
                static (session, current) => current.Execute(session),
                OpaqueRenderBoundsContract.Source(s_sourceBounds),
                RenderHitTestContract.OutputBounds,
                cardinality,
                RenderScaleContract.MaterializeAtWorkingScale);
            context.Publish(context.TargetCommand(
                [context.OpaqueSource(source)],
                RecordingCommand(recorder)));
        }
    }
}
