using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Pins what an opaque operation that shrinks its output to nothing produces.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OpaqueRenderOutput.SetOutputBounds"/> admits an empty rectangle: its containment check is a pure
/// edge comparison, so a zero-extent rectangle at a point inside the allocation is contained by it. Publishing
/// that output is how an operation says it produced nothing, so the executor drops it - the same answer
/// <see cref="OpaqueRenderOutput.Discard"/> gives, and the same one the geometry path reaches when its output
/// leaves the required region.
/// </para>
/// <para>
/// The drop is decided on logical bounds rather than device bounds, because the two disagree. A device
/// rectangle floors the left edge and ceils the right, so an empty logical rectangle at an integer origin is
/// zero device pixels wide while one at a fractional origin is one pixel wide. Only the logical test catches
/// both.
/// </para>
/// <para>
/// Each case reads what the source published through a downstream map, whose callback runs once per input
/// value: no invocation means nothing was published, and one invocation carries the bounds the published value
/// ended up with.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EmptyOpaquePublishTests
{
    private static readonly Rect s_domain = new(0, 0, 32, 32);
    private static readonly Rect s_sourceBounds = new(0, 0, 32, 32);
    private static readonly Rect s_emptyAtIntegerOrigin = new(8, 8, 0, 0);

    [Test]
    public void AnEmptyPublishUnderZeroOrOne_RendersCleanlyAndPublishesNothing()
    {
        using var node = new ShrinkingOpaqueSourceNode(
            s_emptyAtIntegerOrigin,
            RenderValueCardinality.ZeroOrOne);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        Assert.That(() => renderer.Rasterize().Dispose(), Throws.Nothing);

        Assert.Multiple(() =>
        {
            Assert.That(node.SourceCallbackEntries, Is.EqualTo(1));
            Assert.That(
                node.PublishedInputBounds.Records,
                Is.Empty,
                "an output shrunk to nothing carries no value downstream");
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void AnEmptyPublishUnderSingle_FailsTheCardinalityCheckExactlyAsDiscardDoes()
    {
        using var empty = new ShrinkingOpaqueSourceNode(
            s_emptyAtIntegerOrigin,
            RenderValueCardinality.Single);
        using var discarded = new ShrinkingOpaqueSourceNode(
            publishBounds: null,
            RenderValueCardinality.Single);
        using RenderNodeRenderer emptyRenderer = CreateRenderer(empty);
        using RenderNodeRenderer discardedRenderer = CreateRenderer(discarded);

        InvalidOperationException? fromEmptyPublish = Assert.Throws<InvalidOperationException>(
            () => emptyRenderer.Rasterize().Dispose());
        InvalidOperationException? fromDiscard = Assert.Throws<InvalidOperationException>(
            () => discardedRenderer.Rasterize().Dispose());

        Assert.Multiple(() =>
        {
            Assert.That(
                fromEmptyPublish!.Message,
                Does.Contain("The deferred callback published 0 values outside its declared cardinality [1, 1]"));
            Assert.That(
                fromEmptyPublish.Message,
                Is.EqualTo(fromDiscard!.Message),
                "an operation that shrinks to nothing and one that discards answer the same way");
            Assert.That(empty.PublishedInputBounds.Records, Is.Empty);
            Assert.That(emptyRenderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(discardedRenderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void ANonEmptyShrink_StillCropsAndPublishesTheShrunkBounds()
    {
        var shrunk = new Rect(4, 4, 16, 16);
        using var node = new ShrinkingOpaqueSourceNode(shrunk, RenderValueCardinality.ZeroOrOne);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        renderer.Rasterize().Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(node.SourceCallbackEntries, Is.EqualTo(1));
            Assert.That(
                node.PublishedInputBounds.Records,
                Is.EqualTo(new[] { shrunk }),
                "a real shrink is still cropped to the rectangle the operation selected");
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void AnEmptyPublishAtAFractionalOrigin_PublishesNothing()
    {
        // This rectangle covers one device pixel at a working scale of 1, so a drop decided on device bounds
        // would admit it and publish a value whose logical bounds are empty.
        using var node = new ShrinkingOpaqueSourceNode(
            new Rect(10.5f, 20.5f, 0, 0),
            RenderValueCardinality.ZeroOrOne);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        Assert.That(() => renderer.Rasterize().Dispose(), Throws.Nothing);

        Assert.Multiple(() =>
        {
            Assert.That(node.SourceCallbackEntries, Is.EqualTo(1));
            Assert.That(node.PublishedInputBounds.Records, Is.Empty);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(
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
                TargetFactory = new CpuTargetFactory(),
            });

    /// <summary>
    /// A source that shrinks its output before publishing it, read by a map that records what arrived.
    /// </summary>
    /// <param name="publishBounds">
    /// The rectangle to shrink to before publishing, or <see langword="null"/> to discard the output instead.
    /// </param>
    private sealed class ShrinkingOpaqueSourceNode(
        Rect? publishBounds,
        RenderValueCardinality cardinality) : RenderNode
    {
        public int SourceCallbackEntries { get; private set; }

        public RecordingProbe<Rect> PublishedInputBounds { get; } = new();

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.CreateRequestLocal(
                Execute,
                OpaqueRenderBoundsContract.Source(s_sourceBounds),
                RenderHitTestContract.OutputBounds,
                cardinality,
                RenderScaleContract.MaterializeAtWorkingScale));
            context.Publish(context.OpaqueMap(
                source,
                OpaqueRenderDescription.CreateRequestLocal(
                    RecordInput,
                    OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                    RenderHitTestContract.AnyInput,
                    RenderValueCardinality.ZeroOrOne,
                    RenderScaleContract.PreserveInputSupply)));
        }

        private void Execute(OpaqueRenderSession session)
        {
            SourceCallbackEntries++;
            using OpaqueRenderOutput output = session.CreateOutput(s_sourceBounds);
            output.Canvas.Use(static canvas => canvas.Clear(Colors.CornflowerBlue));
            if (publishBounds is not { } bounds)
            {
                output.Discard();
                return;
            }

            output.SetOutputBounds(bounds);
            session.Publish(output);
        }

        private void RecordInput(OpaqueRenderSession session)
            => PublishedInputBounds.Record(session.Inputs.Single().Bounds);
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
