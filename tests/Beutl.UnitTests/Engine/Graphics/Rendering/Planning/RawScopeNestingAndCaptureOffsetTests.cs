using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class RawScopeNestingAndCaptureOffsetTests
{
    private static readonly Rect s_domain = new(0, 0, 64, 64);
    private static readonly Rect s_mark = new(8, 8, 8, 8);
    private const float Shift = 10;

    // Half-transparent so a capture drawn back over the mark is observable: a direct draw alone
    // leaves alpha 128, an in-place round trip composites 128 over 128 and leaves alpha ~192.
    private static readonly Color s_markColor = Color.FromArgb(128, 255, 0, 0);
    private const byte RoundTripAlpha = 192;
    // Well above the fringe a resampled round trip leaves around the mark, well below a real copy.
    private const byte StrayAlpha = 64;

    [Test]
    public void RawTargetScope_ExecutesNestedTargetWorkInsideItsReplay()
    {
        using var node = new NestedRawCommandNode();
        using var renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The nested raw target work produced no bitmap.");
        var sample = bitmap.SKBitmap.GetPixel(
            (int)(s_mark.Center.X - rasterization.Bounds.X),
            (int)(s_mark.Center.Y - rasterization.Bounds.Y));

        Assert.Multiple(() =>
        {
            Assert.That(node.NestedExecutions, Is.EqualTo(1),
                "A raw target scope must let its replayed subtree perform nested target work.");
            Assert.That(sample.Red, Is.EqualTo(byte.MaxValue));
            Assert.That(sample.Alpha, Is.EqualTo(byte.MaxValue));
        });
    }

    [Test]
    public void RawTargetScope_RemainsAnOpaqueExternalBarrierWhileNestingIsAllowed()
    {
        using var node = new NestedRawCommandNode();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_domain));

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        RenderFragmentReference scope = graph.Fragments
            .Select(static fragment => (RenderFragmentReference)fragment.Payload!)
            .Single(static reference => reference.Kind == RenderFragmentKind.RawTargetScope);

        Assert.That(scope.HasOpaqueExternalWork, Is.True,
            "Nested target work must not relax the raw scope's opaque-external barrier.");
    }

    [Test]
    public void TargetCapture_UnderANonZeroDeviceGridOffset_CopiesTheTargetWithoutDisplacingIt()
    {
        using var root = new ContainerRenderNode();
        root.AddChild(new MarkNode());
        var scope = new TransformRenderNode(
            Matrix.CreateTranslation(Shift, 0),
            TransformOperator.Append);
        // Local (-10, 0, 64, 64) covers the whole target, so the round trip must land in place.
        scope.AddChild(new CaptureRoundTripNode(new Rect(-Shift, 0, s_domain.Width, s_domain.Height)));
        root.AddChild(scope);
        using var renderer = CreateRenderer(root);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        AssertCaptureLandedOnTheMark(rasterization);
    }

    [Test]
    public void TargetCapture_UnderAScaledAndTranslatedTarget_CopiesTheTargetWithoutDisplacingIt()
    {
        using var root = new ContainerRenderNode();
        root.AddChild(new MarkNode());
        var scope = new TransformRenderNode(
            Matrix.CreateScale(2, 2) * Matrix.CreateTranslation(Shift, 0),
            TransformOperator.Append);
        // Local (-5, 0, 32, 32) covers the whole target, so the round trip must land in place.
        scope.AddChild(new CaptureRoundTripNode(new Rect(-5, 0, 32, 32)));
        root.AddChild(scope);
        using var renderer = CreateRenderer(root);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        AssertCaptureLandedOnTheMark(rasterization);
    }

    private static void AssertCaptureLandedOnTheMark(RenderNodeRasterization rasterization)
    {
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The offset target capture produced no bitmap.");
        var origin = new PixelPoint((int)rasterization.Bounds.X, (int)rasterization.Bounds.Y);
        var mark = bitmap.SKBitmap.GetPixel(
            (int)s_mark.Center.X - origin.X,
            (int)s_mark.Center.Y - origin.Y);

        // A resampled round trip bleeds a pixel or so past the mark.
        var tolerated = s_mark.Inflate(2);
        int strays = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.SKBitmap.GetPixel(x, y).Alpha < StrayAlpha)
                    continue;
                if (!tolerated.Contains(new Point(x + origin.X, y + origin.Y)))
                    strays++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(mark.Alpha, Is.EqualTo(RoundTripAlpha).Within(8),
                "Replaying a capture of the target back into the same place must reproduce the mark there.");
            Assert.That(strays, Is.Zero,
                "The captured copy must not land displaced from the region it was read from.");
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
                    CacheOptions = RenderCacheOptions.Disabled,
                },
            });

    private sealed class MarkNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                static session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(s_mark);
                    output.Canvas.Use(static canvas => canvas.Clear(s_markColor));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_mark),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "capture-offset-mark",
                runtimeIdentity: new RenderRuntimeIdentity("capture-offset-mark"))));
    }

    private sealed class NestedRawCommandNode : RenderNode
    {
        public int NestedExecutions { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle nested = context.RawTargetCommand(RawTargetCommandDescription.Create(
                session =>
                {
                    NestedExecutions++;
                    session.Canvas.Clear(Colors.Red);
                },
                s_domain,
                RenderHitTestContract.OutputBounds,
                structuralKey: "raw-scope-nested-command"));
            context.Publish(context.RawTargetScope(
                nested,
                RawTargetScopeDescription.Create(
                    static session => session.ReplayInput(),
                    RenderBoundsContract.FullInput,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    structuralKey: "raw-scope-nesting")));
        }
    }

    private sealed class CaptureRoundTripNode(Rect captureBounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle capture = context.TargetCapture(TargetCaptureDescription.Create(
                TargetRegion.Region(captureBounds),
                captureBounds,
                RenderHitTestContract.OutputBounds,
                TargetCaptureScaleContract.PreserveTargetSupply));
            context.Publish(context.ContributeValues(capture));
        }
    }
}
