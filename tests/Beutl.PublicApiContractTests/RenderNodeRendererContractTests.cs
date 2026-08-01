using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderNodeRendererContractTests
{
    [Test]
    public void RenderNodeCache_PublicSurfaceDoesNotExposeRendererOwnedPayloads()
    {
        Type cacheType = typeof(RenderNodeCache);

        Assert.Multiple(() =>
        {
            Assert.That(cacheType.GetProperty("Density"), Is.Null);
            Assert.That(cacheType.GetMethods().Where(static method => method.Name == "UseCache"), Is.Empty);
            Assert.That(cacheType.GetMethods().Where(static method => method.Name == "StoreCache"), Is.Empty);
        });
    }

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
            DefaultRequest = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Delivery,
                OutputScale = authored,
                MaxWorkingScale = 3,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                Purpose = RenderRequestPurpose.Frame,
            },
        };
        using var renderer = new RenderNodeRenderer(root, supplied);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.Root, Is.SameAs(root));
            Assert.That(renderer.Options, Is.Not.SameAs(supplied));
            Assert.That(renderer.Options.DefaultRequest.Intent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(renderer.Options.DefaultRequest.OutputScale, Is.EqualTo(expected));
            Assert.That(renderer.Options.DefaultRequest.MaxWorkingScale, Is.EqualTo(3));
            Assert.That(renderer.Options.DefaultRequest.CacheOptions, Is.EqualTo(RenderCacheOptions.Disabled));
            Assert.That(renderer.Options.DefaultRequest.Purpose, Is.EqualTo(RenderRequestPurpose.Frame));
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
                new RenderNodeRendererOptions {
                    DefaultRequest = new RenderNodeRenderRequest
                    {
                        MaxWorkingScale = invalid,
                    },
                });
            Assert.That(renderer.Options.DefaultRequest.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
        }

        using (var renderer = new RenderNodeRenderer(
                   root,
                   new RenderNodeRendererOptions {
                       DefaultRequest = new RenderNodeRenderRequest
                       {
                           MaxWorkingScale = float.PositiveInfinity,
                       },
                   }))
        {
            Assert.That(renderer.Options.DefaultRequest.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                () => new RenderNodeRenderer(
                    root,
                    new RenderNodeRendererOptions {
                        DefaultRequest = new RenderNodeRenderRequest
                        {
                            TargetDomain = Rect.Empty,
                        },
                    }),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new RenderNodeRenderer(
                    root,
                    new RenderNodeRendererOptions
                    {
                        DefaultRequest = new RenderNodeRenderRequest
                        {
                            TargetDomain = new Rect(float.NaN, 0, 1, 1),
                        },
                    }),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new RenderNodeRenderer(
                    root,
                    new RenderNodeRendererOptions
                    {
                        DefaultRequest = new RenderNodeRenderRequest
                        {
                            RequestedRegion = new Rect(0, 0, float.PositiveInfinity, 1),
                        },
                    }),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new RenderNodeRenderer(
                    root,
                    new RenderNodeRendererOptions {
                        DefaultRequest = new RenderNodeRenderRequest
                        {
                            Intent = (RenderIntent)12345,
                        },
                    }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new RenderNodeRenderer(
                    root,
                    new RenderNodeRendererOptions {
                        DefaultRequest = new RenderNodeRenderRequest
                        {
                            Purpose = (RenderRequestPurpose)12345,
                        },
                    }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void Rasterize_PropagatesThePublicRequestPurpose()
    {
        var bounds = new Rect(0, 0, 4, 3);
        RenderRequestPurpose observedPurpose = default;
        using var root = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(
                bounds,
                session => observedPurpose = session.Purpose,
                "public-purpose-source"));
            context.Publish(source);
        });
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest { CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled },
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize(
            renderer.Options.DefaultRequest with { Purpose = RenderRequestPurpose.Frame });

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(observedPurpose, Is.EqualTo(RenderRequestPurpose.Frame));
            Assert.That(renderer.Options.DefaultRequest.Purpose, Is.EqualTo(RenderRequestPurpose.Auxiliary));
        });
    }

    [TestCase(RenderRequestPurpose.Bounds)]
    [TestCase(RenderRequestPurpose.HitTest)]
    public void Rasterize_RejectsMetadataOnlyRequestPurposes(RenderRequestPurpose purpose)
    {
        using var root = new DelegateNode(static _ => { });
        using var renderer = new RenderNodeRenderer(root);

        Assert.That(
            () => renderer.Rasterize(new RenderNodeRenderRequest { Purpose = purpose }),
            Throws.TypeOf<ArgumentOutOfRangeException>()
                .With.Property("ParamName").EqualTo("purpose"));
    }

    [Test]
    public void Operations_AcceptCompletePerCallRequestsOnOnePersistentRenderer()
    {
        var factory = new TrackingTargetFactory(static size => new TrackingRenderTarget(size));
        using DelegateNode root = SourceNode(new Rect(0, 0, 8, 6));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest { CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled },
                TargetFactory = factory,
            });
        RenderNodeRenderRequest leftRequest = renderer.Options.DefaultRequest with
        {
            RequestedRegion = new Rect(0, 0, 4, 6),
            OutputScale = 1,
        };
        RenderNodeRenderRequest rightRequest = renderer.Options.DefaultRequest with
        {
            RequestedRegion = new Rect(4, 0, 4, 6),
            OutputScale = 2,
        };

        using RenderNodeRasterization left = renderer.Rasterize(leftRequest);
        using RenderNodeRasterization right = renderer.Rasterize(rightRequest);

        Assert.Multiple(() =>
        {
            Assert.That(left.Bounds, Is.EqualTo(new Rect(0, 0, 4, 6)));
            Assert.That(left.OutputScale, Is.EqualTo(1));
            Assert.That(left.Bitmap, Is.Not.Null);
            Assert.That((left.Bitmap!.Width, left.Bitmap.Height), Is.EqualTo((4, 6)));
            Assert.That(right.Bounds, Is.EqualTo(new Rect(4, 0, 4, 6)));
            Assert.That(right.OutputScale, Is.EqualTo(2));
            Assert.That(right.Bitmap, Is.Not.Null);
            Assert.That((right.Bitmap!.Width, right.Bitmap.Height), Is.EqualTo((8, 12)));
            Assert.That(renderer.Options.DefaultRequest.RequestedRegion, Is.Null);
            Assert.That(renderer.Options.DefaultRequest.OutputScale, Is.EqualTo(1));
            Assert.That(renderer.IsDisposed, Is.False);
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
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Delivery,
                    RequestedRegion = requested,
                    OutputScale = 8,
                    MaxWorkingScale = 3,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
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
            Assert.That(factory.Allocations, Is.Not.Empty);
            Assert.That(factory.Allocations, Has.All.Matches<RenderTargetAllocationDescriptor>(allocation =>
                allocation.PixelFormat == RenderTargetPixelFormat.LinearPremultipliedRgba16Float
                && allocation.GraphicsContext is null
                && allocation.GraphicsContextHandle == 0
                && allocation.GraphicsBackend is null));
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
    public void Render_SingularDestinationTransformIsASuccessfulNoOp()
    {
        var bounds = new Rect(0, 0, 10, 10);
        int recordings = 0;
        int executions = 0;
        var factory = new TrackingTargetFactory(static size => new TrackingRenderTarget(size));
        using var root = new DelegateNode(context =>
        {
            recordings++;
            RenderFragmentHandle source = context.OpaqueSource(
                ExecutingSource(bounds, _ => executions++, "singular-transform-source"));
            context.Publish(source);
        });
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
            });
        using var target = new TrackingRenderTarget(new PixelSize(20, 20));
        using var destination = new ImmediateCanvas(target);

        using (destination.PushTransform(Matrix.CreateScale(0, 1)))
        {
            Matrix transform = destination.Transform;
            Assert.That(() => renderer.Render(destination), Throws.Nothing);
            Assert.That(destination.Transform, Is.EqualTo(transform));
        }

        Assert.Multiple(() =>
        {
            Assert.That(recordings, Is.EqualTo(1));
            Assert.That(executions, Is.Zero);
            Assert.That(factory.Requests, Is.Empty);
            Assert.That(destination.IsDisposed, Is.False);
            Assert.That(target.IsDisposed, Is.False);
        });
    }

    [Test]
    public void Render_SingularDestinationTransformRejectsAFullTargetAccess()
    {
        int executions = 0;
        var factory = new TrackingTargetFactory(static size => new TrackingRenderTarget(size));
        using var root = new DelegateNode(context =>
        {
            RenderFragmentHandle command = context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    _ => executions++,
                    TargetRegion.Full,
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    structuralKey: "singular-transform-full-command"));
            context.Publish(command);
        });
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
            });
        using var target = new TrackingRenderTarget(new PixelSize(20, 20));
        using var destination = new ImmediateCanvas(target);

        using (destination.PushTransform(Matrix.CreateScale(0, 1)))
        {
            Assert.That(
                () => renderer.Render(destination),
                Throws.TypeOf<RenderTargetDomainRequiredException>()
                    .With.Message.Contains("requires a finite owning target domain"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.Zero);
            Assert.That(factory.Requests, Is.Empty);
            Assert.That(destination.IsDisposed, Is.False);
            Assert.That(target.IsDisposed, Is.False);
        });
    }

    [Test]
    public void Render_SingularDestinationTransformPreservesAnEmptyTargetCommand()
    {
        int executions = 0;
        var factory = new TrackingTargetFactory(static size => new TrackingRenderTarget(size));
        using var root = new DelegateNode(context =>
        {
            RenderFragmentHandle command = context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    _ => executions++,
                    TargetRegion.Empty,
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    structuralKey: "singular-transform-empty-command"));
            context.Publish(command);
        });
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
            });
        using var target = new TrackingRenderTarget(new PixelSize(20, 20));
        using var destination = new ImmediateCanvas(target);

        using (destination.PushTransform(Matrix.CreateScale(0, 1)))
        {
            Assert.That(() => renderer.Render(destination), Throws.Nothing);
        }

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(factory.Requests, Is.Empty);
            Assert.That(destination.IsDisposed, Is.False);
            Assert.That(target.IsDisposed, Is.False);
        });
    }

    [TestCase(0, 8, 30, 44)]
    [TestCase(8, 0, 34, 40)]
    [TestCase(0, 0, 30, 40)]
    public void HitTest_DegenerateRequestedRegionHasNoHits(
        float width,
        float height,
        float pointX,
        float pointY)
    {
        var bounds = new Rect(0, 0, 100, 100);
        using var root = SourceNode(bounds);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    RequestedRegion = new Rect(30, 40, width, height),
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        Assert.That(renderer.HitTest(new Point(pointX, pointY)), Is.False);
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
    public void Rasterize_ReportsTheDeviceCoverOfShiftedBoundsAndTransfersBitmapOwnershipToTheResult()
    {
        var bounds = new Rect(10.25f, 20.25f, 3.5f, 2.5f);
        PixelRect expectedDeviceBounds = PixelRect.FromRect(bounds, 2);
        var factory = new TrackingTargetFactory(static size => new TrackingRenderTarget(size));

        using var root = SourceNode(bounds);
        var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    OutputScale = 2,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
            });

        RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap!;

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bounds, Is.EqualTo(expectedDeviceBounds.ToRect(2)));
            Assert.That(rasterization.Bounds.Contains(bounds), Is.True);
            Assert.That(rasterization.OutputScale, Is.EqualTo(2));
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(bitmap.Width, Is.EqualTo(expectedDeviceBounds.Width));
            Assert.That(bitmap.Height, Is.EqualTo(expectedDeviceBounds.Height));
            Assert.That(factory.Requests, Does.Contain(expectedDeviceBounds.Size));
            Assert.That(factory.Allocations, Has.All.Matches<RenderTargetAllocationDescriptor>(allocation =>
                allocation.PixelFormat == RenderTargetPixelFormat.LinearPremultipliedRgba16Float
                && allocation.GraphicsContext is null
                && allocation.GraphicsContextHandle is null or 0
                && allocation.GraphicsBackend is null));
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
                   new RenderNodeRendererOptions {
                       DefaultRequest = new RenderNodeRenderRequest
                       {
                       },
                       TargetFactory = factory,
                   }))
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
                       DefaultRequest = new RenderNodeRenderRequest
                       {
                           RequestedRegion = emptySelection,
                       },
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
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
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
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
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
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
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
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
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
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(100, 200, 10, 20),
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
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
            OpaqueRenderBoundsContract.Source(bounds),
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
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = targetDomain,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
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

        public List<RenderTargetAllocationDescriptor> Allocations { get; } = [];

        public List<TrackingRenderTarget> Targets { get; } = [];

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            Allocations.Add(allocation);
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
