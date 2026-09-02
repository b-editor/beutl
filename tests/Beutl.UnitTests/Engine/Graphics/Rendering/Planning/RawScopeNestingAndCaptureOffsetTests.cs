using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Failure;
using SkiaSharp;

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

    [Test]
    public void TargetCapture_UnderAScaledTarget_MaterializesAtTheTargetsPixelSupply()
    {
        using var root = new ContainerRenderNode();
        root.AddChild(new MarkNode());
        var scope = new TransformRenderNode(
            Matrix.CreateScale(2, 2),
            TransformOperator.Append);
        // Local (0, 0, 32, 32) covers the whole target, so the round trip must land in place.
        scope.AddChild(new CaptureRoundTripNode(new Rect(0, 0, 32, 32)));
        root.AddChild(scope);
        using var renderer = CreateRenderer(root);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The scaled target capture produced no bitmap.");
        var origin = new PixelPoint((int)rasterization.Bounds.X, (int)rasterization.Bounds.Y);
        var mark = bitmap.SKBitmap.GetPixel(
            (int)s_mark.Center.X - origin.X,
            (int)s_mark.Center.Y - origin.Y);

        // A capture taken below the target's supply comes back through a 2x upsample, which leaves a
        // one-pixel fringe of half the mark's alpha around it.
        int fringe = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.SKBitmap.GetPixel(x, y).Alpha == 0)
                    continue;
                if (!s_mark.Contains(new Point(x + origin.X, y + origin.Y)))
                    fringe++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(mark.Alpha, Is.EqualTo(RoundTripAlpha).Within(8),
                "Replaying a capture of the target back into the same place must reproduce the mark there.");
            Assert.That(fringe, Is.Zero,
                "A capture under a scaled target must materialize at the target's own pixel supply.");
        });
    }

    [Test]
    [TestCase(0f)]
    [TestCase(0.0005f)]
    public void BuiltInBackdropCapture_UnderADegenerateTargetTransform_RendersTheRestOfTheFrame(float scale)
    {
        using var root = new ContainerRenderNode();
        root.AddChild(new MarkNode());
        var scope = new TransformRenderNode(
            Matrix.CreateScale(scale, scale),
            TransformOperator.Append);
        var snapshot = new SnapshotBackdropRenderNode();
        scope.AddChild(snapshot);
        scope.AddChild(new DrawBackdropRenderNode(snapshot, s_domain));
        root.AddChild(scope);
        using var renderer = CreateRenderer(root);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The degenerate backdrop round trip produced no bitmap.");
        var origin = new PixelPoint((int)rasterization.Bounds.X, (int)rasterization.Bounds.Y);
        var mark = bitmap.SKBitmap.GetPixel(
            (int)s_mark.Center.X - origin.X,
            (int)s_mark.Center.Y - origin.Y);

        int ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.SKBitmap.GetPixel(x, y).Alpha == 0)
                    continue;
                if (!s_mark.Contains(new Point(x + origin.X, y + origin.Y)))
                    ink++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(mark.Alpha, Is.EqualTo(s_markColor.A),
                "A backdrop under a degenerate transform must leave the rest of the frame untouched.");
            Assert.That(ink, Is.Zero,
                "A capture with no readable preimage must contribute no pixels.");
        });
    }

    [Test]
    [TestCase(2f, 2f, 2f)]
    [TestCase(4f, 0.25f, 4f)]
    [TestCase(8f, 0.125f, 8f)]
    public void BuiltInBackdropCapture_UnderAnAnisotropicTargetTransform_UsesTheFinerAxis(
        float scaleX,
        float scaleY,
        float maximumSingularValue)
    {
        var domain = new Rect(0, 0, 64, 64);
        Matrix transform = Matrix.CreateScale(scaleX, scaleY);
        using var root = new ContainerRenderNode();
        var scope = new TransformRenderNode(
            transform,
            TransformOperator.Append);
        var probe = new CaptureSizeProbeNode();
        scope.AddChild(probe);
        scope.AddChild(new DrawBackdropRenderNode(probe, domain));
        root.AddChild(scope);
        using var renderer = CreateProbeRenderer(root, domain);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Rect captureBounds = domain.TransformToAABB(transform.Invert());
        PixelRect expectedFootprint = PixelRect.FromRect(captureBounds, maximumSingularValue);

        Assert.Multiple(() =>
        {
            Assert.That(probe.CapturedDensity, Is.EqualTo(maximumSingularValue).Within(1e-4f),
                "A capture preserving the target's supply must retain the affine transform's maximum singular value.");
            Assert.That(probe.CapturedDeviceSize, Is.EqualTo(expectedFootprint.Size));
            Assert.That(expectedFootprint.Width, Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(expectedFootprint.Height, Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
        });
    }

    [Test]
    public void BuiltInBackdropCapture_ClampsTheMaximumSingularValueToTheCaptureFootprint()
    {
        const float maximumSingularValue = 8;
        var domain = new Rect(0, 0, 16, 1024);
        Matrix transform = Matrix.CreateScale(maximumSingularValue, 0.25f);
        using var root = new ContainerRenderNode();
        var scope = new TransformRenderNode(transform, TransformOperator.Append);
        var probe = new CaptureSizeProbeNode();
        scope.AddChild(probe);
        scope.AddChild(new DrawBackdropRenderNode(probe, domain));
        root.AddChild(scope);
        using var renderer = CreateProbeRenderer(root, domain);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        // The preimage is 2x4096, so the maximum singular value alone overflows the ceiling, and the largest
        // density that still fits it is the ceiling over the taller preimage axis.
        Rect captureBounds = domain.TransformToAABB(transform.Invert());
        float expectedDensity = RenderScaleUtilities.MaxBufferDimension / captureBounds.Height;
        PixelRect expectedFootprint = PixelRect.FromRect(captureBounds, expectedDensity);
        PixelRect unclampedFootprint = PixelRect.FromRect(captureBounds, maximumSingularValue);
        Assert.Multiple(() =>
        {
            Assert.That(unclampedFootprint.Height, Is.GreaterThan(RenderScaleUtilities.MaxBufferDimension),
                "The fixture must ask for a footprint the engine ceiling refuses, or nothing is clamped.");
            Assert.That(probe.CapturedDensity, Is.EqualTo(expectedDensity).Within(1e-4f),
                "Only a density taken from the maximum singular value overflows here - the coarser axis and "
                + "the two axes' geometric mean both fit unclamped and would have been reported unreduced.");
            Assert.That(probe.CapturedDensity, Is.LessThan(maximumSingularValue));
            Assert.That(probe.CapturedDeviceSize, Is.EqualTo(expectedFootprint.Size));
            Assert.That(expectedFootprint.Height, Is.EqualTo(RenderScaleUtilities.MaxBufferDimension),
                "The clamp must give up only what the ceiling costs, leaving the limiting axis against it.");
            Assert.That(expectedFootprint.Width, Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
        });
    }

    [Test]
    public void BuiltInBackdropCapture_UnderShear_UsesTheMaximumSingularValue()
    {
        var domain = new Rect(0, 0, 256, 128);
        var transform = new Matrix(1, 1, 0, 1, 0, 0);
        using var root = new ContainerRenderNode();
        var scope = new TransformRenderNode(
            transform,
            TransformOperator.Append);
        var probe = new CaptureSizeProbeNode();
        scope.AddChild(probe);
        scope.AddChild(new DrawBackdropRenderNode(probe, domain));
        root.AddChild(scope);
        using var renderer = CreateProbeRenderer(root, domain);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        float expectedDensity = MathF.Sqrt((3 + MathF.Sqrt(5)) / 2);
        Rect captureBounds = domain.TransformToAABB(transform.Invert());
        PixelSize expectedSize = PixelRect.FromRect(captureBounds, expectedDensity).Size;
        Assert.Multiple(() =>
        {
            Assert.That(probe.CapturedDensity, Is.EqualTo(expectedDensity).Within(1e-4f));
            Assert.That(probe.CapturedDeviceSize, Is.EqualTo(expectedSize));
        });
    }

    [Test]
    public void BuiltInBackdropCapture_UnderPerspective_RejectsBeforeAllocatingTheCapture()
    {
        var domain = new Rect(0, 0, 64, 64);
        using var root = new ContainerRenderNode();
        var scope = new TransformRenderNode(
            new Matrix(
                1, 0, 0.01f,
                0, 1, 0,
                0, 0, 1),
            TransformOperator.Append);
        var probe = new CaptureSizeProbeNode();
        scope.AddChild(probe);
        scope.AddChild(new DrawBackdropRenderNode(probe, domain));
        root.AddChild(scope);
        var factory = new FailureTestTargetFactory();
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = domain,
                    OutputScale = 1,
                    CacheOptions = RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
            });

        NotSupportedException? failure = Assert.Throws<NotSupportedException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("perspective"));
            Assert.That(factory.CreateCalls, Is.EqualTo(1),
                "only the root execution target may be acquired before perspective capture rejection");
            Assert.That(probe.CapturedDeviceSize, Is.EqualTo(default(PixelSize)));
        });
    }

    private sealed class CaptureSizeProbeNode : SnapshotBackdropRenderNode, IBuiltInBackdropCaptureSink
    {
        public PixelSize CapturedDeviceSize { get; private set; }

        public float CapturedDensity { get; private set; }

        bool IBuiltInBackdropCaptureSink.TryCommitBackdropCapture(Bitmap bitmap, float density)
        {
            Record(bitmap, density);
            return true;
        }

        void IBuiltInBackdropCaptureSink.CommitBackdropCapture(Bitmap bitmap, float density)
            => Record(bitmap, density);

        private void Record(Bitmap bitmap, float density)
        {
            CapturedDeviceSize = new PixelSize(bitmap.Width, bitmap.Height);
            CapturedDensity = density;
            bitmap.Dispose();
        }
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

    [Test]
    public void TargetCapture_ReachingPastTheTargetDomain_ReadsTransparentInsteadOfFailing()
    {
        using var root = new ContainerRenderNode();
        root.AddChild(new MarkNode());
        root.AddChild(new CaptureRoundTripNode(s_domain.Inflate(16)));
        using var renderer = CreateRenderer(root);

        Assert.That(() => renderer.Rasterize().Dispose(), Throws.Nothing);
    }

    /// <remarks>
    /// A capture-density probe reads the density planning gave a capture, so the buffer has to be the one
    /// planning asked for. Leaving the allocator implicit measures it against whatever this machine's GPU can
    /// attach instead, and a capture clamped to the engine ceiling is past what several devices allow.
    /// </remarks>
    private static RenderNodeRenderer CreateProbeRenderer(RenderNode node, Rect domain)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = domain,
                    OutputScale = 1,
                    CacheOptions = RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });

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
                "capture-offset-mark",
                static (session, _) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(s_mark);
                    output.Canvas.Use(static canvas => canvas.Clear(s_markColor));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_mark),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale)));
    }

    private sealed class NestedRawCommandNode : RenderNode
    {
        public int NestedExecutions { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle nested = context.RawTargetCommand(RawTargetCommandDescription.CreateRequestLocal(
                session =>
                {
                    NestedExecutions++;
                    session.Canvas.Clear(Colors.Red);
                },
                s_domain,
                RenderHitTestContract.OutputBounds));
            context.Publish(context.RawTargetScope(
                nested,
                RawTargetScopeDescription.CreateRequestLocal(
                    static session => session.ReplayInput(),
                    RenderBoundsContract.FullInput,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply)));
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

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            SKSurface surface = SKSurface.Create(new SKImageInfo(
                    deviceSize.Width,
                    deviceSize.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()))
                ?? throw new InvalidOperationException("Could not create the CPU capture test surface.");
            return new CpuRenderTarget(surface, deviceSize);
        }
    }

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);
}
