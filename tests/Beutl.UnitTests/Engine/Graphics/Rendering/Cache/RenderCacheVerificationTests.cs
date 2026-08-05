using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public sealed class RenderCacheVerificationTests
{
    private static readonly Rect s_bounds = new(0, 0, 16, 12);
    private static readonly PixelSize s_frameSize = new(240, 160);

    [Test]
    public void UnderSpecifiedRuntimeIdentity_IsServedStaleWhenVerificationIsOff()
    {
        using var node = new ColorFillNode(IdentityKind.UnderSpecified);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using RenderNodeRenderer renderer = CreateRenderer(node, diagnostics);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        node.Color = Colors.Blue;
        using RenderNodeRasterization stale = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1));
            Assert.That(node.ExecuteCount, Is.EqualTo(1),
                "the default configuration serves the cached output without re-executing the producer");
            Assert.That(TopLeft(stale), Is.EqualTo(ToPremultipliedHalfBits(Colors.Red)),
                "the under-specified identity silently serves the first frame's pixels");
        });
    }

    [Test]
    public void UnderSpecifiedRuntimeIdentity_FailsVerificationLoudly()
    {
        using var node = new ColorFillNode(IdentityKind.UnderSpecified);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        node.Color = Colors.Blue;
        var exception = Assert.Throws<RenderCacheOutputMismatchException>(
            () => renderer.Rasterize(VerifyingRequest()));

        TestContext.Out.WriteLine(exception!.Message);
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain(nameof(ColorFillNode)));
            Assert.That(exception.Message, Does.Contain("runtime identity"));
            Assert.That(exception.Message, Does.Contain("device pixel"));
        });
    }

    [Test]
    [NonParallelizable]
    public void DefaultStructuralKey_NamesTheDeclaringNodeNotOnlyTheCallbackSignature()
    {
        using var node = new ColorFillNode(IdentityKind.UnderSpecified);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        node.Color = Colors.Blue;
        RenderCacheOutputMismatchException? exception;
        using (RenderCacheVerification.EnableForAllRequests())
        {
            exception = Assert.Throws<RenderCacheOutputMismatchException>(() => renderer.Rasterize());
        }

        TestContext.Out.WriteLine(exception!.Message);
        Assert.That(
            exception.Message,
            Does.Contain($"structural key '{typeof(ColorFillNode).FullName}."),
            "an unlabelled description defaults to its callback method, which only names the node through "
            + "its declaring type");
    }

    [Test]
    public void CompleteRuntimeIdentity_MissesTheCacheWhenTheDrawnValueChanges()
    {
        using var node = new ColorFillNode(IdentityKind.Complete);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using RenderNodeRenderer renderer = CreateRenderer(node, diagnostics);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        node.Color = Colors.Blue;
        using RenderNodeRasterization repainted = renderer.Rasterize(VerifyingRequest(diagnostics));

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.Zero,
                "a complete identity misses when the drawn value changes");
            Assert.That(TopLeft(repainted), Is.EqualTo(ToPremultipliedHalfBits(Colors.Blue)));
        });
    }

    [Test]
    public void UnchangedProducer_PassesVerificationAndStillHitsTheCache()
    {
        using var node = new ColorFillNode(IdentityKind.Complete);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using RenderNodeRenderer renderer = CreateRenderer(node, diagnostics);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        using RenderNodeRasterization verified = renderer.Rasterize(VerifyingRequest(diagnostics));

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1),
                "verification must keep serving the cache hit, not demote it to a miss");
            Assert.That(node.ExecuteCount, Is.EqualTo(2),
                "verification re-executes the producer of every cache hit");
            Assert.That(TopLeft(verified), Is.EqualTo(ToPremultipliedHalfBits(Colors.Red)));
        });
    }

    [Test]
    public void ComposedGraph_PassesVerificationAcrossNestedCacheCandidates()
    {
        using var producer = new ColorFillNode(IdentityKind.Complete);
        producer.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var consumer = new TintingConsumerNode(producer);
        consumer.Cache.ReportRenderCount(RenderNodeCache.Count);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using RenderNodeRenderer renderer = CreateRenderer(consumer, diagnostics);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        using RenderNodeRasterization verified = renderer.Rasterize(VerifyingRequest(diagnostics));

        Assert.Multiple(() =>
        {
            Assert.That(verified.IsEmpty, Is.False);
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.GreaterThan(0));
        });
    }

    [Test]
    [NonParallelizable]
    public void ComposedProductionScene_PassesVerificationOnEveryCacheHit()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            Drawable.Resource[] resources = CreateSceneResources();
            try
            {
                using var root = new DrawableRenderNode(resources[0]);
                using (var context = new GraphicsContext2D(root, s_frameSize.ToSize(1)))
                {
                    context.Clear();
                    foreach (Drawable.Resource resource in resources)
                        context.DrawDrawable(resource);
                }

                foreach (RenderNode node in Descendants(root))
                    node.Cache.ReportRenderCount(RenderNodeCache.Count);

                var diagnostics = new RenderPipelineDiagnosticsState();
                using var renderer = new RenderNodeRenderer(
                    root,
                    new RenderNodeRendererOptions
                    {
                        DefaultRequest = new RenderNodeRenderRequest
                        {
                            TargetDomain = new Rect(default, s_frameSize.ToSize(1)),
                            CacheOptions = RenderCacheOptions.Enabled,
                            Purpose = RenderRequestPurpose.Frame,
                            Diagnostics = diagnostics,
                            VerifyCacheOutputs = true,
                        },
                    });

                long hits = 0;
                for (int frame = 0; frame < 4; frame++)
                {
                    using RenderNodeRasterization rasterization = renderer.Rasterize();
                    Assert.That(rasterization.IsEmpty, Is.False);
                    hits += diagnostics.Latest[RenderPipelineCounter.RenderCacheHits];
                }

                Assert.That(hits, Is.GreaterThan(0),
                    "the composed production scene must exercise verified render-cache hits");
            }
            finally
            {
                foreach (Drawable.Resource resource in resources)
                    resource.Dispose();
            }
        });
    }

    [Test]
    [NonParallelizable]
    public void EnableForAllRequests_TurnsVerificationOnWithoutTouchingTheRequest()
    {
        using var node = new ColorFillNode(IdentityKind.UnderSpecified);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        node.Color = Colors.Blue;
        using (RenderCacheVerification.EnableForAllRequests())
        {
            Assert.That(RenderCacheVerification.IsEnabled, Is.True);
            Assert.Throws<RenderCacheOutputMismatchException>(() => renderer.Rasterize());
        }

        Assert.That(RenderCacheVerification.IsEnabled, Is.False);
    }

    [Test]
    public void UnderSpecifiedRuntimeIdentity_FailsVerificationWithFusionEnabled()
    {
        using var node = new ColorFillNode(IdentityKind.UnderSpecified);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, fusionMode: FusionMode.Enabled);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        node.Color = Colors.Blue;
        var exception = Assert.Throws<RenderCacheOutputMismatchException>(
            () => renderer.Rasterize(VerifyingRequest(fusionMode: FusionMode.Enabled)));

        Assert.That(exception!.Message, Does.Contain(nameof(ColorFillNode)));
    }

    [Test]
    public void OffOriginDefect_IsReportedAtItsDevicePixel()
    {
        using var node = new CornerDefectNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        var exception = Assert.Throws<RenderCacheOutputMismatchException>(
            () => renderer.Rasterize(VerifyingRequest()));

        TestContext.Out.WriteLine(exception!.Message);
        Assert.That(
            exception.Message,
            Does.Contain($"device pixel ({CornerDefectNode.DefectX}, {CornerDefectNode.DefectY})"));
    }

    [Test]
    public void DefectInConsumerNode_NamesTheConsumer()
    {
        using var producer = new ColorFillNode(IdentityKind.Complete);
        producer.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var consumer = new DefectiveConsumerNode(producer);
        consumer.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(consumer);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        var exception = Assert.Throws<RenderCacheOutputMismatchException>(
            () => renderer.Rasterize(VerifyingRequest()));

        TestContext.Out.WriteLine(exception!.Message);
        Assert.That(exception.Message, Does.Contain(nameof(DefectiveConsumerNode)));
    }

    [Test]
    public void NonIdempotentCallback_MessageOffersBothExplanations()
    {
        using var node = new AlternatingColorNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        var exception = Assert.Throws<RenderCacheOutputMismatchException>(
            () => renderer.Rasterize(VerifyingRequest()));

        TestContext.Out.WriteLine(exception!.Message);
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("runtime identity omits"));
            Assert.That(exception.Message, Does.Contain("not idempotent"));
        });
    }

    [Test]
    public void TargetScopeProducer_ReportsItsOwnStructuralKey()
    {
        using var node = new MarkedTargetScopeNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using RenderNodeRenderer renderer = CreateRenderer(node, diagnostics);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        node.MarkerColor = Colors.Lime;
        var exception = Assert.Throws<RenderCacheOutputMismatchException>(
            () => renderer.Rasterize(VerifyingRequest(diagnostics)));

        TestContext.Out.WriteLine(exception!.Message);
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("produced by TargetScope"));
            Assert.That(
                exception.Message,
                Does.Contain($"structural key '{MarkedTargetScopeNode.ScopeStructuralKey}'"),
                "the target-scope payload carries a structural key and the message must report it");
        });
    }

    [Test]
    public void FailedVerificationReexecution_StillServesTheCachedOutput()
    {
        using var node = new ThrowOnReexecutionNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using RenderNodeRenderer renderer = CreateRenderer(node, diagnostics);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        using RenderNodeRasterization served = renderer.Rasterize(VerifyingRequest(diagnostics));

        Assert.Multiple(() =>
        {
            Assert.That(node.ExecuteAttempts, Is.EqualTo(2),
                "verification must have attempted the re-execution that throws");
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1));
            Assert.That(served.IsEmpty, Is.False,
                "a failed verification re-execution must not drop the content the cache could serve");
            Assert.That(TopLeft(served), Is.EqualTo(ToPremultipliedHalfBits(Colors.Red)));
        });
    }

    private static RenderNodeRenderRequest VerifyingRequest(
        RenderPipelineDiagnosticsState? diagnostics = null,
        FusionMode fusionMode = FusionMode.Disabled)
        => new()
        {
            TargetDomain = s_bounds,
            CacheOptions = RenderCacheOptions.Enabled,
            Purpose = RenderRequestPurpose.Frame,
            FusionMode = fusionMode,
            Diagnostics = diagnostics,
            VerifyCacheOutputs = true,
        };

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        RenderPipelineDiagnosticsState? diagnostics = null,
        FusionMode fusionMode = FusionMode.Disabled)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                    FusionMode = fusionMode,
                    Diagnostics = diagnostics,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    private static ulong TopLeft(RenderNodeRasterization rasterization)
    {
        Assert.That(rasterization.IsEmpty, Is.False);
        return ReadFirstPixel(rasterization.Bitmap!);
    }

    private static ulong ToPremultipliedHalfBits(Color color)
    {
        using var target = new CpuRenderTarget(1, 1);
        target.Value.Canvas.Clear(color.ToSKColor());
        using Bitmap snapshot = target.Snapshot();
        return ReadFirstPixel(snapshot);
    }

    // Raw Skia: a Brush.Resource has to be declared on the description to pass callback resource authorization,
    // and these fixtures paint from state the description deliberately does not describe.
    private static void FillRect(ImmediateCanvas canvas, Rect rect, Color color)
    {
        using var paint = new SKPaint { Color = color.ToSKColor(), IsAntialias = false };
        canvas.Canvas.DrawRect(rect.ToSKRect(), paint);
    }

    private static ulong ReadFirstPixel(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        return ((ulong)pixels[0] << 48)
               | ((ulong)pixels[1] << 32)
               | ((ulong)pixels[2] << 16)
               | pixels[3];
    }

    private static Drawable.Resource[] CreateSceneResources()
    {
        var background = new RectShape
        {
            Width = { CurrentValue = s_frameSize.Width },
            Height = { CurrentValue = s_frameSize.Height },
            Fill = { CurrentValue = Brushes.CornflowerBlue },
        };

        var accent = new EllipseShape
        {
            Width = { CurrentValue = 76 },
            Height = { CurrentValue = 76 },
            Fill = { CurrentValue = Brushes.OrangeRed },
            FilterEffect = { CurrentValue = new Brightness { Amount = { CurrentValue = 78 } } },
            Transform = { CurrentValue = new TranslateTransform(44, -18) },
        };

        var label = new TextBlock
        {
            FontFamily = { CurrentValue = FontFamily.Default },
            Size = { CurrentValue = 28 },
            Fill = { CurrentValue = Brushes.White },
            Text = { CurrentValue = "CACHE" },
            Transform = { CurrentValue = new TranslateTransform(-28, 30) },
        };

        CompositionContext context = CompositionContext.Default;
        return
        [
            background.ToResource(context),
            accent.ToResource(context),
            label.ToResource(context),
        ];
    }

    private static IEnumerable<RenderNode> Descendants(RenderNode node)
    {
        yield return node;
        if (node is ContainerRenderNode container)
        {
            foreach (RenderNode child in container.Children)
            {
                foreach (RenderNode descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }

    private enum IdentityKind
    {
        UnderSpecified,
        Complete,
    }

    private sealed class ColorFillNode(IdentityKind identityKind) : RenderNode
    {
        public Color Color { get; set; } = Colors.Red;

        public int ExecuteCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            Color color = Color;
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    ExecuteCount++;
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(color));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.FullInputs(static _ => s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                runtimeIdentity: new RenderRuntimeIdentity(identityKind == IdentityKind.Complete
                    ? (object)color
                    : typeof(ColorFillNode)));
            context.Publish(context.ContributeValues(context.OpaqueCombine([], description)));
        }
    }

    private sealed class CornerDefectNode : RenderNode
    {
        public const int DefectX = 12;
        public const int DefectY = 9;

        private int _executions;

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    bool defective = ++_executions > 1;
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas =>
                    {
                        canvas.Clear(Colors.Red);
                        if (defective)
                        {
                            FillRect(
                                canvas,
                                new Rect(
                                    DefectX,
                                    DefectY,
                                    s_bounds.Width - DefectX,
                                    s_bounds.Height - DefectY),
                                Colors.Blue);
                        }
                    });
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.FullInputs(static _ => s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                runtimeIdentity: new RenderRuntimeIdentity(typeof(CornerDefectNode)));
            context.Publish(context.ContributeValues(context.OpaqueCombine([], description)));
        }
    }

    private sealed class AlternatingColorNode : RenderNode
    {
        private int _executions;

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    Color color = _executions++ % 2 == 0 ? Colors.Red : Colors.Blue;
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(color));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.FullInputs(static _ => s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                runtimeIdentity: new RenderRuntimeIdentity(typeof(AlternatingColorNode)));
            context.Publish(context.ContributeValues(context.OpaqueCombine([], description)));
        }
    }

    private sealed class ThrowOnReexecutionNode : RenderNode
    {
        public int ExecuteAttempts { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    if (++ExecuteAttempts > 1)
                        throw new InvalidOperationException("The verification re-execution fails.");

                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.Red));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.FullInputs(static _ => s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                runtimeIdentity: new RenderRuntimeIdentity(typeof(ThrowOnReexecutionNode)));
            context.Publish(context.ContributeValues(context.OpaqueCombine([], description)));
        }
    }

    // Mirrors ParticleRenderNode: a target command carrying a structural key and no runtime identity, so the
    // colour it paints is invisible to the cache key.
    // A target scope whose structural key and runtime identity are both authored, painting a marker the
    // identity does not describe.
    private sealed class MarkedTargetScopeNode : RenderNode
    {
        public const string ScopeStructuralKey = "marked-target-scope";

        public Color MarkerColor { get; set; } = Colors.Red;

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription sourceDescription = OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.FullInputs(static _ => s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                runtimeIdentity: new RenderRuntimeIdentity(typeof(MarkedTargetScopeNode)));
            RenderFragmentHandle source =
                context.ContributeValues(context.OpaqueCombine([], sourceDescription));

            Color marker = MarkerColor;
            TargetScopeDescription scopeDescription = TargetScopeDescription.Create(
                session => session.Canvas.Use(canvas =>
                {
                    session.ReplayInput();
                    FillRect(canvas, s_bounds, marker);
                }),
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                structuralKey: ScopeStructuralKey,
                runtimeIdentity: new RenderRuntimeIdentity(typeof(MarkedTargetScopeNode)));
            context.Publish(context.Layer([context.TargetScope(source, scopeDescription)], s_bounds));
        }
    }

    private sealed class DefectiveConsumerNode(RenderNode producer) : RenderNode
    {
        private int _executions;

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.RecordNode(producer, []).Single();
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    bool defective = ++_executions > 1;
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas =>
                    {
                        session.Inputs[0].Draw(canvas);
                        if (defective)
                            FillRect(canvas, s_bounds, Colors.Lime);
                    });
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                RenderHitTestContract.AnyInput,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                runtimeIdentity: new RenderRuntimeIdentity(typeof(DefectiveConsumerNode)));
            context.Publish(context.OpaqueMap(input, description));
        }

        protected override void OnDispose(bool disposing)
        {
            producer.Dispose();
            base.OnDispose(disposing);
        }
    }

    private sealed class TintingConsumerNode(RenderNode producer) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.RecordNode(producer, []).Single();
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(session.Inputs[0].Draw);
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                RenderHitTestContract.AnyInput,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                runtimeIdentity: new RenderRuntimeIdentity(typeof(TintingConsumerNode)));
            context.Publish(context.OpaqueMap(input, description));
        }

        protected override void OnDispose(bool disposing)
        {
            producer.Dispose();
            base.OnDispose(disposing);
        }
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
