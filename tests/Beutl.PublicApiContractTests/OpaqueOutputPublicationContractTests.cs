using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Pins what publishing a shrunk opaque output produces, as an external author sees it.
/// </summary>
/// <remarks>
/// <see cref="OpaqueRenderOutput.SetOutputBounds"/> accepts any rectangle the allocation contains, and
/// containment is a pure edge comparison, so a zero-extent rectangle at a point inside the allocation is an
/// accepted answer. That answer means the operation produced nothing, so the request drops the output rather
/// than failing: shrinking to nothing and calling <see cref="OpaqueRenderSession.Publish"/> is the same
/// statement as <see cref="OpaqueRenderOutput.Discard"/>, under either cardinality. A rectangle with real
/// extent is a different statement and still reaches the next operation, cropped to what it selected.
/// </remarks>
[TestFixture]
public sealed class OpaqueOutputPublicationContractTests
{
    private static readonly Rect s_domain = new(0, 0, 32, 32);
    private static readonly Rect s_sourceBounds = new(0, 0, 32, 32);

    private static OpaqueRenderDescription RecordingMap(PublishedBoundsRecorder recorder)
        => OpaqueRenderDescription.Create(
            recorder,
            static (session, current) => current.Record(session.Inputs.Single().Bounds),
            OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
            RenderHitTestContract.AnyInput,
            RenderValueCardinality.ZeroOrOne,
            RenderScaleContract.PreserveInputSupply);

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
    public void AnOutputShrunkToNothing_FailsExactlyAsDiscardDoesWhereAValueIsRequired()
    {
        var recorder = new PublishedBoundsRecorder();
        using var shrunkToNothing = new SourceNode(
            new PublishPlan(new Rect(8, 8, 0, 0)),
            RenderValueCardinality.Single,
            recorder);
        using var discarded = new SourceNode(
            new PublishPlan(Selection: null),
            RenderValueCardinality.Single,
            new PublishedBoundsRecorder());

        InvalidOperationException? fromShrinkToNothing = Assert.Throws<InvalidOperationException>(
            () => RenderOnce(shrunkToNothing));
        InvalidOperationException? fromDiscard = Assert.Throws<InvalidOperationException>(
            () => RenderOnce(discarded));

        Assert.Multiple(() =>
        {
            Assert.That(
                fromShrinkToNothing!.Message,
                Does.Contain("published 0 values outside its declared cardinality [1, 1]"));
            Assert.That(fromShrinkToNothing.Message, Is.EqualTo(fromDiscard!.Message));
            Assert.That(recorder.Records, Is.Empty);
        });
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
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_domain,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
            });
        renderer.Rasterize().Dispose();
    }

    /// <param name="Selection">
    /// The rectangle to shrink the output to before publishing it, or <see langword="null"/> to discard it.
    /// </param>
    private sealed record PublishPlan(Rect? Selection)
    {
        public void Execute(OpaqueRenderSession session)
        {
            using OpaqueRenderOutput output = session.CreateOutput(s_sourceBounds);
            if (Selection is not { } selection)
            {
                output.Discard();
                return;
            }

            output.SetOutputBounds(selection);
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
            context.Publish(context.OpaqueMap(
                context.OpaqueSource(source),
                RecordingMap(recorder)));
        }
    }
}
