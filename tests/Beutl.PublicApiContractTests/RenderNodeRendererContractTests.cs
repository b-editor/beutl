using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderNodeRendererContractTests
{
    [TestCase(float.NaN, 1f)]
    [TestCase(0f, 1f)]
    [TestCase(-2f, 1f)]
    [TestCase(float.PositiveInfinity, 1f)]
    [TestCase(2.5f, 2.5f)]
    public void Options_SnapshotAndSanitizeOutputScale(float authored, float expected)
    {
        using var root = new DelegateNode(static _ => { });
        var supplied = new RenderNodeRendererOptions
        {
            Intent = RenderIntent.Delivery,
            OutputScale = authored,
            MaxWorkingScale = 3,
            UseRenderCache = false,
        };
        using var renderer = new RenderNodeRenderer(root, supplied);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.Root, Is.SameAs(root));
            Assert.That(renderer.Options, Is.Not.SameAs(supplied));
            Assert.That(renderer.Options.Intent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(renderer.Options.OutputScale, Is.EqualTo(expected));
            Assert.That(renderer.Options.MaxWorkingScale, Is.EqualTo(3));
            Assert.That(renderer.Options.UseRenderCache, Is.False);
        });
    }

    [Test]
    public void Options_SanitizeMaxWorkingScaleAndRejectInvalidRectangles()
    {
        using var root = new DelegateNode(static _ => { });
        foreach (float invalid in new[] { float.NaN, 0, -1, float.NegativeInfinity })
        {
            using var renderer = new RenderNodeRenderer(
                root,
                new RenderNodeRendererOptions { MaxWorkingScale = invalid });
            Assert.That(renderer.Options.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
        }

        using (var renderer = new RenderNodeRenderer(
                   root,
                   new RenderNodeRendererOptions { MaxWorkingScale = float.PositiveInfinity }))
        {
            Assert.That(renderer.Options.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                () => new RenderNodeRenderer(
                    root,
                    new RenderNodeRendererOptions { TargetDomain = Rect.Empty }),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new RenderNodeRenderer(
                    root,
                    new RenderNodeRendererOptions
                    {
                        TargetDomain = new Rect(float.NaN, 0, 1, 1),
                    }),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new RenderNodeRenderer(
                    root,
                    new RenderNodeRendererOptions
                    {
                        RequestedRegion = new Rect(0, 0, float.PositiveInfinity, 1),
                    }),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new RenderNodeRenderer(
                    root,
                    new RenderNodeRendererOptions { Intent = (RenderIntent)12345 }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void MeasureHitTestAndRender_UseMetadataOrDestinationStateAsRequired()
    {
        var bounds = new Rect(3, 4, 8, 6);
        var requested = new Rect(4, 5, 3, 2);
        int executions = 0;
        float executionOutputScale = 0;
        float executionMaxWorkingScale = 0;
        RenderRequestPurpose executionPurpose = default;
        RenderIntent executionIntent = default;
        var factory = new TrackingTargetFactory(static size => new TrackingRenderTarget(size));

        using var root = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(
                ExecutingSource(
                    bounds,
                    session =>
                    {
                        executions++;
                        executionOutputScale = session.OutputScale;
                        executionMaxWorkingScale = session.MaxWorkingScale;
                        executionPurpose = session.Purpose;
                        executionIntent = session.Intent;
                    },
                    "render-state-source"));
            context.Publish(source);
        });
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                Intent = RenderIntent.Delivery,
                RequestedRegion = requested,
                OutputScale = 8,
                MaxWorkingScale = 3,
                UseRenderCache = false,
                TargetFactory = factory,
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        bool hitInside = renderer.HitTest(new Point(5, 6));
        bool hitOutsideRequested = renderer.HitTest(new Point(3.5f, 4.5f));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.Zero, "Measure and HitTest are metadata-only requests.");
            Assert.That(measurement.OutputBounds, Is.EqualTo(bounds));
            Assert.That(measurement.QueryBounds, Is.EqualTo(bounds));
            Assert.That(measurement.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.HasTargetEffects, Is.False);
            Assert.That(hitInside, Is.True);
            Assert.That(hitOutsideRequested, Is.False);
        });

        using var destinationTarget = new TrackingRenderTarget(new PixelSize(40, 30));
        using var destination = new ImmediateCanvas(
            destinationTarget,
            density: 2,
            maxWorkingScale: 1.5f,
            logicalSize: new Size(20, 15));
        destination.Opacity = 0.4f;
        destination.BlendMode = BlendMode.Multiply;
        using (destination.PushTransform(Matrix.CreateTranslation(2, 1)))
        using (destination.PushClip(new Rect(0, 0, 12, 10)))
        {
            Matrix transform = destination.Transform;
            renderer.Render(destination);

            Assert.Multiple(() =>
            {
                Assert.That(destination.Transform, Is.EqualTo(transform));
                Assert.That(destination.Opacity, Is.EqualTo(0.4f));
                Assert.That(destination.BlendMode, Is.EqualTo(BlendMode.Multiply));
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(executionOutputScale, Is.EqualTo(2), "Render uses the destination density, not Options.OutputScale.");
            Assert.That(executionMaxWorkingScale, Is.EqualTo(1.5f));
            Assert.That(executionPurpose, Is.EqualTo(RenderRequestPurpose.Auxiliary));
            Assert.That(executionIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(destination.IsDisposed, Is.False);
            Assert.That(destinationTarget.IsDisposed, Is.False);
        });
    }

    [Test]
    public void Render_InverseMapsTranslatedDestinationViewportAndIgnoresOptionTargetDomain()
    {
        AssertRenderedTargetDomain(
            Matrix.CreateTranslation(10, 5),
            new Rect(-10, -5, 40, 30));
    }

    [Test]
    public void Render_InverseMapsScaledDestinationViewportAndIgnoresOptionTargetDomain()
    {
        AssertRenderedTargetDomain(
            Matrix.CreateScale(2, 3),
            new Rect(0, 0, 20, 10));
    }

    [Test]
    public void Render_ConservativelyInverseMapsRotatedDestinationViewportAndIgnoresOptionTargetDomain()
    {
        AssertRenderedTargetDomain(
            Matrix.CreateRotation(MathF.PI / 2),
            new Rect(0, -40, 30, 40));
    }

    [Test]
    public void Render_InverseMapsTheLogicalViewportAtTheActiveDestinationDensity()
    {
        AssertRenderedTargetDomain(
            Matrix.CreateTranslation(10, 5),
            new Rect(-10, -5, 35, 25),
            new PixelSize(80, 60),
            density: 2,
            logicalSize: new Size(35, 25));
    }

    [Test]
    public void CommandAndCaptureMeasurements_KeepValueContributionQueryAndTargetEffectsIndependent()
    {
        var domain = new Rect(10, 20, 50, 30);
        var query = new Rect(20, 24, 7, 5);

        using var commandNode = new DelegateNode(context =>
        {
            RenderFragmentHandle command = context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    static _ => throw new AssertionException("Measure must not execute commands."),
                    TargetRegion.Full,
                    query,
                    RenderHitTestContract.OutputBounds,
                    TargetAccess.ReadWrite,
                    structuralKey: "measurement-command"));
            context.Publish(command);
        });
        RenderNodeMeasurement command = Measure(commandNode, targetDomain: domain);

        using var captureNode = new DelegateNode(context =>
        {
            RenderFragmentHandle capture = context.TargetCapture(
                TargetCaptureDescription.Create(
                    TargetRegion.Full,
                    query,
                    RenderHitTestContract.OutputBounds,
                    RenderScaleContract.MaterializeAtWorkingScale));
            context.Publish(capture);
        });
        RenderNodeMeasurement capture = Measure(captureNode, targetDomain: domain);

        Assert.Multiple(() =>
        {
            Assert.That(command.OutputBounds, Is.EqualTo(domain));
            Assert.That(command.QueryBounds, Is.EqualTo(query));
            Assert.That(command.ValueCardinality, Is.EqualTo(RenderValueCardinality.None));
            Assert.That(command.HasFragments, Is.True);
            Assert.That(command.HasContributingValues, Is.False);
            Assert.That(command.HasTargetEffects, Is.True);

            Assert.That(capture.OutputBounds, Is.EqualTo(default(Rect)));
            Assert.That(capture.QueryBounds, Is.EqualTo(default(Rect)));
            Assert.That(capture.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(capture.HasFragments, Is.True);
            Assert.That(capture.HasContributingValues, Is.False);
            Assert.That(capture.HasTargetEffects, Is.True);
        });
    }

    [Test]
    public void Rasterize_PreservesShiftedBoundsAndTransfersBitmapOwnershipToTheResult()
    {
        var bounds = new Rect(10.25f, 20.25f, 3.5f, 2.5f);
        PixelRect expectedDeviceBounds = PixelRect.FromRect(bounds, 2);
        var factory = new TrackingTargetFactory(static size => new TrackingRenderTarget(size));

        using var root = SourceNode(bounds);
        var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                OutputScale = 2,
                UseRenderCache = false,
                TargetFactory = factory,
            });

        RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap!;

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bounds, Is.EqualTo(bounds));
            Assert.That(rasterization.OutputScale, Is.EqualTo(2));
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(bitmap.Width, Is.EqualTo(expectedDeviceBounds.Width));
            Assert.That(bitmap.Height, Is.EqualTo(expectedDeviceBounds.Height));
            Assert.That(factory.Requests, Does.Contain(expectedDeviceBounds.Size));
        });

        renderer.Dispose();
        Assert.That(bitmap.IsDisposed, Is.False, "Renderer disposal does not dispose an already returned rasterization.");
        Assert.That(factory.Targets, Is.Not.Empty);
        Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => target.IsDisposed));

        rasterization.Dispose();
        rasterization.Dispose();
        Assert.That(bitmap.IsDisposed, Is.True);
    }

    [Test]
    public void Rasterize_ReturnsNormalEmptyResultsWithoutAllocatingOrExecuting()
    {
        var factory = new TrackingTargetFactory(static size => new TrackingRenderTarget(size));
        int executions = 0;

        using var emptyRoot = new DelegateNode(static _ => { });
        using (var renderer = new RenderNodeRenderer(
                   emptyRoot,
                   new RenderNodeRendererOptions { TargetFactory = factory }))
        using (RenderNodeRasterization result = renderer.Rasterize())
        {
            Assert.Multiple(() =>
            {
                Assert.That(result.IsEmpty, Is.True);
                Assert.That(result.Bounds, Is.EqualTo(default(Rect)));
                Assert.That(result.Bitmap, Is.Null);
            });
        }

        var authoredBounds = new Rect(0, 0, 10, 10);
        var emptySelection = new Rect(30, 40, 0, 8);
        using var sourceRoot = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(
                ExecutingSource(authoredBounds, _ => executions++, "empty-selection-source"));
            context.Publish(source);
        });
        using (var renderer = new RenderNodeRenderer(
                   sourceRoot,
                   new RenderNodeRendererOptions
                   {
                       RequestedRegion = emptySelection,
                       TargetFactory = factory,
                   }))
        using (RenderNodeRasterization result = renderer.Rasterize())
        {
            Assert.Multiple(() =>
            {
                Assert.That(result.IsEmpty, Is.True);
                Assert.That(result.Bounds, Is.EqualTo(emptySelection));
                Assert.That(result.Bitmap, Is.Null);
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.Zero);
            Assert.That(factory.Requests, Is.Empty);
        });
    }

    [Test]
    public void TargetFactory_InvalidReturnIsOwnedDisposedAndRejected()
    {
        var bounds = new Rect(0, 0, 4, 3);
        TrackingRenderTarget? invalid = null;
        var factory = new TrackingTargetFactory(size =>
        {
            invalid = new TrackingRenderTarget(new PixelSize(size.Width + 1, size.Height));
            return invalid;
        });

        using var root = SourceNode(bounds);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetFactory = factory,
                UseRenderCache = false,
            });

        Assert.That(() => renderer.Rasterize(), Throws.TypeOf<InvalidOperationException>());
        Assert.Multiple(() =>
        {
            Assert.That(invalid, Is.Not.Null);
            Assert.That(invalid!.IsDisposed, Is.True);
            Assert.That(invalid.DisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void TargetFactory_ReusedLiveTargetIsRejectedAndDisposedWithRendererExactlyOnce()
    {
        var bounds = new Rect(0, 0, 4, 3);
        var shared = new TrackingRenderTarget(new PixelSize(4, 3));
        var factory = new TrackingTargetFactory(_ => shared);

        using var root = SourceNode(bounds);
        var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetFactory = factory,
                UseRenderCache = false,
            });

        Assert.That(() => renderer.Rasterize(), Throws.TypeOf<InvalidOperationException>());
        Assert.That(shared.IsDisposed, Is.False,
            "The accepted first allocation remains owned by the renderer pool after request failure.");
        renderer.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(shared.IsDisposed, Is.True);
            Assert.That(shared.DisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void TargetFactory_BorrowedDestinationAliasIsRejectedWithoutDisposingDestination()
    {
        var bounds = new Rect(0, 0, 4, 3);
        using var destinationTarget = new TrackingRenderTarget(new PixelSize(4, 3));
        using var destination = new ImmediateCanvas(destinationTarget);
        var factory = new TrackingTargetFactory(_ => destinationTarget);

        using var root = SourceNode(bounds);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetFactory = factory,
                UseRenderCache = false,
            });

        Assert.That(() => renderer.Render(destination), Throws.TypeOf<InvalidOperationException>());
        Assert.Multiple(() =>
        {
            Assert.That(destinationTarget.IsDisposed, Is.False);
            Assert.That(destinationTarget.DisposeCalls, Is.Zero);
        });
    }

    [Test]
    public void TargetFactory_IncompatibleSurfaceFormatIsOwnedDisposedAndRejected()
    {
        var bounds = new Rect(0, 0, 4, 3);
        TrackingRenderTarget? incompatible = null;
        var factory = new TrackingTargetFactory(size =>
        {
            incompatible = new TrackingRenderTarget(size, SKColorType.Rgba8888);
            return incompatible;
        });

        using var root = SourceNode(bounds);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetFactory = factory,
                UseRenderCache = false,
            });

        Assert.That(() => renderer.Rasterize(), Throws.TypeOf<InvalidOperationException>());
        Assert.Multiple(() =>
        {
            Assert.That(incompatible, Is.Not.Null);
            Assert.That(incompatible!.IsDisposed, Is.True);
            Assert.That(incompatible.DisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void Dispose_IsIdempotentRejectsLaterCallsAndDoesNotDisposeRootOrDestination()
    {
        using var root = new DelegateNode(static _ => { });
        var renderer = new RenderNodeRenderer(root);
        using var target = new TrackingRenderTarget(new PixelSize(2, 2));
        using var destination = new ImmediateCanvas(target);

        renderer.Dispose();
        renderer.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(renderer.IsDisposed, Is.True);
            Assert.That(root.IsDisposed, Is.False);
            Assert.That(destination.IsDisposed, Is.False);
            Assert.That(target.IsDisposed, Is.False);
            Assert.That(() => renderer.Measure(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => renderer.HitTest(default), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => renderer.Rasterize(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => renderer.Render(destination), Throws.TypeOf<ObjectDisposedException>());
        });
    }

    private static DelegateNode SourceNode(Rect bounds)
    {
        return new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds, null, ("source", bounds)));
            context.Publish(source);
        });
    }

    private static void AssertRenderedTargetDomain(
        Matrix transform,
        Rect expected,
        PixelSize deviceSize = default,
        float density = 1,
        Size logicalSize = default)
    {
        if (deviceSize == default)
            deviceSize = new PixelSize(40, 30);
        if (logicalSize.IsDefault)
            logicalSize = new Size(40, 30);

        Rect? observed = null;
        using var root = new DelegateNode(context =>
        {
            RenderFragmentHandle command = context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    session => observed = session.AffectedBounds,
                    TargetRegion.Full,
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    structuralKey: ("render-target-domain", transform)));
            context.Publish(command);
        });
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = new Rect(100, 200, 10, 20),
                UseRenderCache = false,
            });
        using var target = new TrackingRenderTarget(deviceSize);
        using var destination = new ImmediateCanvas(target, density, logicalSize: logicalSize);

        using (destination.PushTransform(transform))
            renderer.Render(destination);

        Assert.That(observed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(observed!.Value.X, Is.EqualTo(expected.X).Within(0.0001f));
            Assert.That(observed.Value.Y, Is.EqualTo(expected.Y).Within(0.0001f));
            Assert.That(observed.Value.Width, Is.EqualTo(expected.Width).Within(0.0001f));
            Assert.That(observed.Value.Height, Is.EqualTo(expected.Height).Within(0.0001f));
        });
    }

    private static OpaqueRenderDescription ExecutingSource(
        Rect bounds,
        Action<OpaqueRenderSession>? observe,
        object structuralKey)
    {
        return OpaqueRenderDescription.Create(
            session =>
            {
                observe?.Invoke(session);
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                session.Publish(output);
            },
            RenderOperationBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey,
            runtimeIdentity: new RenderRuntimeIdentity(("source-runtime", structuralKey)));
    }

    private static RenderNodeMeasurement Measure(RenderNode node, Rect? targetDomain = null)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = targetDomain,
                UseRenderCache = false,
            });
        return renderer.Measure();
    }

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class TrackingTargetFactory(Func<PixelSize, RenderTarget?> create) : IRenderTargetFactory
    {
        public List<PixelSize> Requests { get; } = [];

        public List<TrackingRenderTarget> Targets { get; } = [];

        public RenderTarget? Create(PixelSize deviceSize)
        {
            Requests.Add(deviceSize);
            RenderTarget? result = create(deviceSize);
            if (result is TrackingRenderTarget tracking)
            {
                Targets.Add(tracking);
            }

            return result;
        }
    }

    private sealed class TrackingRenderTarget : RenderTarget
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

        public TrackingRenderTarget(PixelSize size, SKColorType colorType = SKColorType.RgbaF16)
            : base(CreateSurface(size, colorType), size.Width, size.Height)
        {
        }

        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                DisposeCalls++;
            }

            base.Dispose(disposing);
        }

        private static SKSurface CreateSurface(PixelSize size, SKColorType colorType)
        {
            return SKSurface.Create(new SKImageInfo(
                       size.Width,
                       size.Height,
                       colorType,
                       SKAlphaType.Premul,
                       s_colorSpace))
                   ?? throw new InvalidOperationException("Could not create the contract-test render target.");
        }
    }
}
