using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
[NonParallelizable]
public sealed class LegacyFilterTypedSuffixExecutionTests
{
    private const string BlueShader =
        "half4 apply(half4 color) { return half4(0.0, 0.0, color.a, color.a); }";

    [Test]
    public void ShaderAfterUnknownCustomEffect_ExecutesAgainstMaterializedTarget()
    {
        Rect runtimeBounds = new(14, 25, 8, 6);
        Rect observedInput = default;
        Rect observedOutput = default;
        RenderIntent? observedIntent = null;
        RenderRequestPurpose? observedPurpose = null;
        var effect = new LegacySuffixCallbackFilterEffect((context, _) =>
        {
            context.CustomEffect(
                runtimeBounds,
                static (bounds, execution) =>
                {
                    foreach (EffectTarget target in execution.Targets)
                        target.Bounds = bounds;
                });
            context.Shader(ShaderDescription.CurrentPixel(
                "uniform float marker; "
                + "half4 apply(half4 color) { return half4(0.0, 0.0, color.a * marker, color.a); }",
                bindings => bindings.Uniform(
                    "marker",
                    1f,
                    (writer, value, execution) =>
                    {
                        observedInput = execution.InputBounds;
                        observedOutput = execution.OutputBounds;
                        observedIntent = execution.Intent;
                        observedPurpose = execution.Purpose;
                        writer.Set(value);
                    },
                    structuralKey: "legacy-suffix-shader-binder")));
        });
        Rect inputBounds = new(10, 20, 8, 6);

        using EffectTargets targets = CreateSolidTargets(inputBounds, Colors.Red);
        Apply(effect, inputBounds, targets);

        Assert.That(targets, Has.Count.EqualTo(1));
        SKColor color = ReadCenterPixel(targets[0]);
        Assert.Multiple(() =>
        {
            Assert.That(targets[0].Bounds, Is.EqualTo(runtimeBounds));
            Assert.That(observedInput, Is.EqualTo(runtimeBounds));
            Assert.That(observedOutput, Is.EqualTo(runtimeBounds));
            Assert.That(observedIntent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(observedPurpose, Is.EqualTo(RenderRequestPurpose.Auxiliary));
            Assert.That(color.Red, Is.LessThan(16));
            Assert.That(color.Green, Is.LessThan(16));
            Assert.That(color.Blue, Is.GreaterThan(239));
            Assert.That(color.Alpha, Is.GreaterThan(239));
        });
    }

    [Test]
    public void ShaderAfterUnknownCustomEffect_UsesRendererProgramCacheAcrossFrames()
    {
        var effect = new LegacySuffixCallbackFilterEffect(static (context, _) =>
        {
            context.CustomEffect(0, static (_, _) => { });
            context.Shader(ShaderDescription.CurrentPixel(BlueShader));
        });
        Rect bounds = new(0, 0, 8, 6);
        using var root = new FilterEffectRenderNode(
            effect.ToResource(CompositionContext.Default));
        root.AddChild(new RectangleRenderNode(
            bounds,
            Brushes.Resource.Red,
            null));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization cold = renderer.Rasterize();
        ProgramCacheStatistics coldStatistics = renderer.ProgramCacheStatistics;
        using RenderNodeRasterization warm = renderer.Rasterize();
        ProgramCacheStatistics warmStatistics = renderer.ProgramCacheStatistics;
        SKColor coldColor = ReadCenterPixel(cold.Bitmap
            ?? throw new AssertionException("The cold typed-suffix render produced no bitmap."));
        SKColor warmColor = ReadCenterPixel(warm.Bitmap
            ?? throw new AssertionException("The warm typed-suffix render produced no bitmap."));

        Assert.Multiple(() =>
        {
            Assert.That(coldColor.Red, Is.LessThan(16));
            Assert.That(coldColor.Green, Is.LessThan(16));
            Assert.That(coldColor.Blue, Is.GreaterThan(239));
            Assert.That(coldColor.Alpha, Is.GreaterThan(239));
            Assert.That(warmColor.Red, Is.LessThan(16));
            Assert.That(warmColor.Green, Is.LessThan(16));
            Assert.That(warmColor.Blue, Is.GreaterThan(239));
            Assert.That(warmColor.Alpha, Is.GreaterThan(239));
            Assert.That(coldStatistics.Creations, Is.EqualTo(1));
            Assert.That(coldStatistics.Misses, Is.EqualTo(1));
            Assert.That(coldStatistics.Hits, Is.Zero);
            Assert.That(warmStatistics.Creations, Is.EqualTo(1));
            Assert.That(warmStatistics.Misses, Is.EqualTo(1));
            Assert.That(warmStatistics.Hits, Is.EqualTo(1));
            Assert.That(renderer.LastExecutionStatistics.ProgramCacheHits, Is.EqualTo(1));
        });
    }

    [Test]
    public void MaterializedInput_CustomEffect_UsesAmbientGridWithoutReanchoringInput()
    {
        var translation = new Vector(0.25f, 0.75f);
        Vector observedAmbientGrid = default;
        Vector observedInputGrid = default;
        var effect = new LegacySuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(
                0,
                (_, execution) =>
                {
                    observedAmbientGrid = execution.DeviceGridOffset;
                    observedInputGrid = execution.Targets.Single().DeviceGridOffset;
                },
                static (_, bounds) => bounds));

        RenderMaterializedEffect(effect, translation);

        Assert.Multiple(() =>
        {
            Assert.That(observedAmbientGrid, Is.EqualTo(translation));
            Assert.That(observedInputGrid, Is.EqualTo(default(Vector)));
        });
    }

    [Test]
    public void SourceGridReplacement_FlowsIntoFollowingCustomStage()
    {
        var translation = new Vector(0.25f, 0.75f);
        Vector replacementGrid = new(float.NaN, float.NaN);
        Vector followingAmbientGrid = default;
        Vector followingInputGrid = new(float.NaN, float.NaN);
        var effect = new LegacySuffixCallbackFilterEffect((context, _) =>
        {
            context.CustomEffect(
                0,
                (_, execution) =>
                {
                    EffectTarget source = execution.Targets.Single();
                    using RenderTarget replacementBacking = source.RenderTarget!.ShallowCopy();
                    EffectTarget replacement = execution.CreateReplacement(
                        source,
                        replacementBacking);
                    source.Dispose();
                    execution.Targets[0] = replacement;
                    replacementGrid = replacement.DeviceGridOffset;
                },
                static (_, bounds) => bounds);
            context.CustomEffect(
                1,
                (_, execution) =>
                {
                    followingAmbientGrid = execution.DeviceGridOffset;
                    followingInputGrid = execution.Targets.Single().DeviceGridOffset;
                },
                static (_, bounds) => bounds);
        });

        RenderMaterializedEffect(effect, translation);

        Assert.Multiple(() =>
        {
            Assert.That(replacementGrid, Is.EqualTo(default(Vector)));
            Assert.That(followingAmbientGrid, Is.EqualTo(translation));
            Assert.That(followingInputGrid, Is.EqualTo(default(Vector)));
        });
    }

    [Test]
    public void CompatibilityShader_ProgramAcquirerReceivesExecutionDestination()
    {
        Rect bounds = new(0, 0, 8, 6);
        using EffectTargets targets = CreateSolidTargets(bounds, Colors.Red);
        EffectTarget input = targets[0];
        using ProgramCache<CachedSkRuntimeEffect> cache = SkRuntimeEffectProgramCache.Create();
        EffectTarget? observedTarget = null;

        LegacyFilterEffectCompatibilityExecutor.ApplyShader(
            targets,
            ShaderDescription.CurrentPixel(BlueShader),
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            (target, source) =>
            {
                observedTarget = target;
                return SkRuntimeEffectProgramCache.AcquireForDestination(
                    cache,
                    target.RenderTarget!,
                    source);
            });

        Assert.Multiple(() =>
        {
            Assert.That(observedTarget, Is.SameAs(targets[0]));
            Assert.That(observedTarget, Is.Not.SameAs(input));
            Assert.That(observedTarget!.RenderTarget, Is.Not.Null);
            Assert.That(input.IsEmpty, Is.True);
        });
    }

    // The compatibility executor had the request intent in scope but opened every canvas on the
    // RenderIntent.Preview default, so a delivery render degraded there instead of failing.
    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void CompatibilityGeometry_CallbackCanvasCarriesTheRequestIntent(RenderIntent intent)
    {
        Rect bounds = new(0, 0, 8, 6);
        using EffectTargets targets = CreateSolidTargets(bounds, Colors.Red);
        RenderIntent? observedIntent = null;

        LegacyFilterEffectCompatibilityExecutor.ApplyGeometry(
            targets,
            GeometryDescription.CreateRequestLocal(
                session => session.Canvas.Use(canvas => observedIntent = canvas.Intent),
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: "legacy-compat-intent-geometry"),
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            intent,
            RenderRequestPurpose.Auxiliary);

        Assert.That(observedIntent, Is.EqualTo(intent));
    }

    [Test]
    public void GeometryAfterUnknownCustomEffect_UsesRuntimeBoundsAndPublishesShrink()
    {
        Rect runtimeBounds = new(30, 40, 8, 6);
        Rect mappedBounds = runtimeBounds.Inflate(new Thickness(2));
        Rect selectedBounds = mappedBounds.Inflate(new Thickness(-1));
        Rect observedInput = default;
        var effect = new LegacySuffixCallbackFilterEffect((context, _) =>
        {
            context.CustomEffect(
                runtimeBounds,
                static (bounds, execution) =>
                {
                    foreach (EffectTarget target in execution.Targets)
                        target.Bounds = bounds;
                });
            context.Geometry(GeometryDescription.CreateRequestLocal(
                session =>
                {
                    observedInput = session.Input.Bounds;
                    session.Canvas.Use(static canvas => canvas.Clear(Colors.Lime));
                    session.SetOutputBounds(session.OutputBounds.Inflate(new Thickness(-1)));
                },
                RenderBoundsContract.Create(
                    static bounds => bounds.Inflate(new Thickness(2)),
                    static bounds => bounds.Inflate(new Thickness(2)),
                    "legacy-suffix-inflate"),
                RenderHitTestContract.AnyInput,
                structuralKey: "legacy-suffix-geometry"));
        });
        Rect recordedBounds = new(10, 20, 8, 6);

        using EffectTargets targets = CreateSolidTargets(recordedBounds, Colors.Red);
        Apply(effect, recordedBounds, targets);

        Assert.That(targets, Has.Count.EqualTo(1));
        SKColor color = ReadCenterPixel(targets[0]);
        Assert.Multiple(() =>
        {
            Assert.That(observedInput, Is.EqualTo(runtimeBounds));
            Assert.That(targets[0].Bounds, Is.EqualTo(selectedBounds));
            Assert.That(color.Red, Is.LessThan(16));
            Assert.That(color.Green, Is.GreaterThan(239));
            Assert.That(color.Blue, Is.LessThan(16));
            Assert.That(color.Alpha, Is.GreaterThan(239));
        });
    }

    [Test]
    public void DelayAnimationEffect_ExecutesTypedChildEffect()
    {
        var child = new LegacySuffixCallbackFilterEffect(static (context, _) =>
            context.Shader(ShaderDescription.CurrentPixel(BlueShader)));
        var delay = new DelayAnimationEffect
        {
            Delay = { CurrentValue = 0 },
            Effect = { CurrentValue = child },
        };
        Rect bounds = new(4, 7, 8, 6);

        using EffectTargets targets = CreateSolidTargets(bounds, Colors.Red);
        Apply(delay, bounds, targets);

        Assert.That(targets, Has.Count.EqualTo(1));
        SKColor color = ReadCenterPixel(targets[0]);
        Assert.Multiple(() =>
        {
            Assert.That(color.Red, Is.LessThan(16));
            Assert.That(color.Blue, Is.GreaterThan(239));
            Assert.That(color.Alpha, Is.GreaterThan(239));
        });
    }

    [Test]
    public void DelayAnimationEffect_ChildRollbackPreservesPrimaryFailureOverCleanupFailure()
    {
        var primary = new InvalidOperationException("delay-child-primary");
        var cleanup = new ThrowingDisposable();
        var child = new LegacySuffixCallbackFilterEffect((context, _) =>
        {
            context.Own(cleanup, "delay-child-owned", 1);
            context.Shader(ShaderDescription.CurrentPixel(BlueShader));
            throw primary;
        });
        var delay = new DelayAnimationEffect
        {
            Delay = { CurrentValue = 0 },
            Effect = { CurrentValue = child },
        };
        Rect bounds = new(0, 0, 8, 6);
        using FilterEffect.Resource resource = delay.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(delay, resource);
        using EffectTargets targets = CreateSolidTargets(bounds, Colors.Red);
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(
            () => activator.Apply(context));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(primary));
            Assert.That(cleanup.DisposeCount, Is.EqualTo(1));
            Assert.That(
                primary.Data["FilterEffectResourceRollbackFailure"],
                Is.TypeOf<AggregateException>());
        });
    }

    [Test]
    public void UnknownCustomEffect_FinalValueIsCroppedToOwningDomainAfterInternalAllocation()
    {
        Rect domain = new(0, 0, 20, 10);
        Rect expandedBounds = new(-5, -3, 30, 16);
        PixelSize observedInternalAllocation = default;
        Rect observedDownstreamInput = default;
        var expandingEffect = new LegacySuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(
                0,
                (_, execution) => execution.ForEach((_, _) =>
                {
                    EffectTarget expanded = execution.CreateTarget(expandedBounds);
                    observedInternalAllocation = expanded.DeviceBounds.Size;
                    using ImmediateCanvas canvas = execution.Open(expanded);
                    canvas.Clear(Colors.Magenta);
                    return expanded;
                })));
        var downstreamEffect = new LegacySuffixCallbackFilterEffect((context, _) =>
            context.Geometry(GeometryDescription.CreateRequestLocal(
                session =>
                {
                    observedDownstreamInput = session.Input.Bounds;
                    session.Canvas.Use(session.Input.Draw);
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: "legacy-final-domain-observer")));
        var inner = new FilterEffectRenderNode(expandingEffect.ToResource(CompositionContext.Default));
        inner.AddChild(new RectangleRenderNode(
            new Rect(4, 2, 6, 4),
            Brushes.Resource.White,
            null));
        using var root = new FilterEffectRenderNode(
            downstreamEffect.ToResource(CompositionContext.Default));
        root.AddChild(inner);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = domain,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap!;
        SKColor center = bitmap.SKBitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);

        Assert.Multiple(() =>
        {
            Assert.That(observedInternalAllocation, Is.EqualTo(new PixelSize(30, 16)));
            Assert.That(observedDownstreamInput, Is.EqualTo(domain));
            Assert.That(rasterization.Bounds, Is.EqualTo(domain));
            Assert.That(bitmap.Width, Is.EqualTo(20));
            Assert.That(bitmap.Height, Is.EqualTo(10));
            Assert.That(center.Red, Is.GreaterThan(239));
            Assert.That(center.Blue, Is.GreaterThan(239));
            Assert.That(center.Alpha, Is.GreaterThan(239));
        });
    }

    [Test]
    public void UnknownCustomEffect_OwningDomainNarrowingKeepsLegacyRasterPlacement()
    {
        var domain = new Rect(0, 0, 20, 14);
        var expandedBounds = new Rect(-3.5f, -2.25f, 26, 18.5f);
        RenderTarget? retainedAllocation = null;
        var expandingEffect = new LegacySuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(
                0,
                (_, execution) => execution.ForEach((_, _) =>
                {
                    EffectTarget expanded = execution.CreateTarget(expandedBounds);
                    using (ImmediateCanvas canvas = execution.Open(expanded))
                    {
                        canvas.Clear(Colors.Magenta);
                        canvas.DrawRectangle(
                            new Rect(7.5f, 5.25f, 4, 3),
                            Brushes.Resource.White,
                            null);
                    }

                    retainedAllocation = expanded.RenderTarget!.ShallowCopy();
                    return expanded;
                })));
        using var root = new FilterEffectRenderNode(
            expandingEffect.ToResource(CompositionContext.Default));
        root.AddChild(new RectangleRenderNode(
            new Rect(4, 2, 6, 4),
            Brushes.Resource.Red,
            null));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = domain,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using var actualTarget = new CpuRenderTarget((int)domain.Width, (int)domain.Height);
        using (var actualCanvas = new ImmediateCanvas(actualTarget, logicalSize: domain.Size))
        {
            actualCanvas.Clear();
            renderer.Render(actualCanvas);
        }

        using RenderTarget allocation = retainedAllocation
            ?? throw new AssertionException("The custom effect did not allocate an expanded target.");
        using var expectedTarget = new CpuRenderTarget((int)domain.Width, (int)domain.Height);
        using (var expectedCanvas = new ImmediateCanvas(expectedTarget, logicalSize: domain.Size))
        {
            expectedCanvas.Clear();
            expectedCanvas.DrawRenderTarget(allocation, expandedBounds.Position);
        }

        using Bitmap actual = actualTarget.Snapshot();
        using Bitmap expected = expectedTarget.Snapshot();
        Assert.That(
            actual.GetPixelSpan<ushort>().SequenceEqual(expected.GetPixelSpan<ushort>()),
            Is.True,
            "narrowing a legacy raster-placement value to the owning domain must relabel its bounds "
            + "instead of re-allocating and re-anchoring its pixels");
    }

    [Test]
    public void FirstCustomEffect_MovedSemanticBoundsRetainPreCallbackBacking()
    {
        var inputBounds = new Rect(0, 0, 12, 10);
        var movedBounds = new Rect(4.5f, 3.5f, 4, 3);
        Rect observedBounds = default;
        Rect observedRasterBounds = default;
        PixelRect observedDeviceBounds = default;
        var movingEffect = new LegacySuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(
                movedBounds,
                static (bounds, execution) =>
                {
                    foreach (EffectTarget target in execution.Targets)
                        target.Bounds = bounds;
                },
                static (bounds, _) => bounds));
        var observingEffect = new LegacySuffixCallbackFilterEffect((context, _) =>
            context.Geometry(GeometryDescription.CreateRequestLocal(
                session =>
                {
                    observedBounds = session.Input.Bounds;
                    observedDeviceBounds = session.Input.DeviceBounds;
                    observedRasterBounds = session.Input.RasterBounds;
                    session.Canvas.Use(session.Input.Draw);
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: "legacy-retained-backing-observer")));
        using var source = new CpuRenderTarget((int)inputBounds.Width, (int)inputBounds.Height);
        source.Value.Canvas.Clear(SKColors.White);
        source.Value.Flush();
        var movingNode = new FilterEffectRenderNode(
            movingEffect.ToResource(CompositionContext.Default));
        movingNode.AddChild(new MaterializedInputNode(source, inputBounds));
        using var root = new FilterEffectRenderNode(
            observingEffect.ToResource(CompositionContext.Default));
        root.AddChild(movingNode);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(0, 0, 24, 20),
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Rect movedInputRaster = inputBounds.Translate(movedBounds.Position - inputBounds.Position);
        var expectedDeviceBounds = new PixelRect(
            default,
            new PixelSize((int)inputBounds.Width, (int)inputBounds.Height));

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(observedBounds, Is.EqualTo(movedBounds));
            Assert.That(observedDeviceBounds, Is.EqualTo(expectedDeviceBounds));
            Assert.That(observedRasterBounds, Is.EqualTo(movedInputRaster));
            Assert.That(observedDeviceBounds.Size, Is.EqualTo(new PixelSize(12, 10)));
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public void FractionalLegacyTarget_ReplaysExactlyLikeDirectPointComposite(int customCount)
    {
        var domain = new Rect(0, 0, 20, 14);
        var legacyBounds = new Rect(2.5f, 1.5f, 9.75f, 7.25f);
        var effect = new LegacySuffixCallbackFilterEffect((context, _) =>
        {
            context.CustomEffect(
                legacyBounds,
                static (bounds, execution) => execution.ForEach((_, _) =>
                {
                    EffectTarget output = execution.CreateTarget(bounds);
                    using ImmediateCanvas canvas = execution.Open(output);
                    DrawLegacyPattern(canvas);
                    return output;
                }),
                static (bounds, _) => bounds);
            if (customCount == 2)
            {
                context.CustomEffect(
                    0,
                    static (_, _) => { },
                    static (_, bounds) => bounds);
            }
        });
        using var root = new FilterEffectRenderNode(
            effect.ToResource(CompositionContext.Default));
        root.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 1, 1),
            Brushes.Resource.White,
            null));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = domain,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });
        using var actualTarget = new CpuRenderTarget((int)domain.Width, (int)domain.Height);
        using (var actualCanvas = new ImmediateCanvas(actualTarget, logicalSize: domain.Size))
        {
            actualCanvas.Clear();
            renderer.Render(actualCanvas);
        }

        using var localTarget = new CpuRenderTarget(9, 7);
        using (var localCanvas = new ImmediateCanvas(localTarget, logicalSize: legacyBounds.Size))
        {
            DrawLegacyPattern(localCanvas);
        }

        using var expectedTarget = new CpuRenderTarget((int)domain.Width, (int)domain.Height);
        using (var expectedCanvas = new ImmediateCanvas(expectedTarget, logicalSize: domain.Size))
        {
            expectedCanvas.Clear();
            expectedCanvas.DrawRenderTarget(localTarget, legacyBounds.Position);
        }

        using Bitmap actual = actualTarget.Snapshot();
        using Bitmap expected = expectedTarget.Snapshot();
        Assert.That(
            actual.GetPixelSpan<ushort>().SequenceEqual(expected.GetPixelSpan<ushort>()),
            Is.True,
            $"{customCount} legacy CustomEffect boundary/boundaries changed direct point-blit pixels");
    }

    [Test]
    public void FractionalLegacyInput_RetainsDirectPlacementWhenCallbackMovesBounds()
    {
        var domain = new Rect(0, 0, 20, 14);
        var sourceBounds = new Rect(2.5f, 1.5f, 9.75f, 7.25f);
        var movedBounds = new Rect(5.25f, 3.75f, sourceBounds.Width, sourceBounds.Height);
        RenderTarget? retainedInput = null;
        var effect = new LegacySuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(
                movedBounds,
                (bounds, execution) => execution.ForEach((_, target) =>
                {
                    retainedInput = target.RenderTarget!.ShallowCopy();
                    target.Bounds = bounds;
                    return target;
                }),
                static (bounds, _) => bounds));
        using var root = new FilterEffectRenderNode(
            effect.ToResource(CompositionContext.Default));
        root.AddChild(new RectangleRenderNode(
            sourceBounds,
            Brushes.Resource.OrangeRed,
            null));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = domain,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });
        using var actualTarget = new CpuRenderTarget((int)domain.Width, (int)domain.Height);
        using (var actualCanvas = new ImmediateCanvas(actualTarget, logicalSize: domain.Size))
        {
            actualCanvas.Clear();
            renderer.Render(actualCanvas);
        }

        using RenderTarget localTarget = retainedInput
            ?? throw new AssertionException("The legacy callback did not receive a materialized input.");
        Assert.That(localTarget.Width, Is.EqualTo(9));
        Assert.That(localTarget.Height, Is.EqualTo(7));

        using var expectedTarget = new CpuRenderTarget((int)domain.Width, (int)domain.Height);
        using (var expectedCanvas = new ImmediateCanvas(expectedTarget, logicalSize: domain.Size))
        {
            expectedCanvas.Clear();
            expectedCanvas.DrawRenderTarget(localTarget, movedBounds.Position);
        }

        using Bitmap actual = actualTarget.Snapshot();
        using Bitmap expected = expectedTarget.Snapshot();
        Assert.That(
            actual.GetPixelSpan<ushort>().SequenceEqual(expected.GetPixelSpan<ushort>()),
            Is.True,
            "moving retained legacy input pixels must not insert a canonical normalization pass");
    }

    [Test]
    public void PolicyBearingNoOpSkiaItem_MaterializesAtResolvedWorkingScale()
    {
        const float inputDensity = 0.5f;
        var bounds = new Rect(0, 0, 12, 10);
        EffectiveScale observedScale = default;
        PixelRect observedDeviceBounds = default;
        var noOpSkiaEffect = new LegacySuffixCallbackFilterEffect((context, _) =>
            context.AppendSkiaFilter(
                0,
                static (_, _, _) => null,
                static (_, current) => current));
        var observingEffect = new LegacySuffixCallbackFilterEffect((context, _) =>
            context.Geometry(GeometryDescription.CreateRequestLocal(
                session =>
                {
                    observedScale = session.Input.EffectiveScale;
                    observedDeviceBounds = session.Input.DeviceBounds;
                    session.Canvas.Use(session.Input.Draw);
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: "legacy-policy-materialization-observer")));
        PixelRect inputDeviceBounds = PixelRect.FromRect(bounds, inputDensity);
        using var source = new CpuRenderTarget(inputDeviceBounds.Width, inputDeviceBounds.Height);
        source.Value.Canvas.Clear(SKColors.White);
        source.Value.Flush();
        var noOpNode = new FilterEffectRenderNode(
            noOpSkiaEffect.ToResource(CompositionContext.Default));
        noOpNode.AddChild(new MaterializedInputNode(source, bounds, inputDensity));
        using var root = new FilterEffectRenderNode(
            observingEffect.ToResource(CompositionContext.Default));
        root.AddChild(noOpNode);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(observedScale, Is.EqualTo(EffectiveScale.At(1)));
            Assert.That(observedDeviceBounds, Is.EqualTo(PixelRect.FromRect(bounds, 1)));
        });
    }

    private static void Apply(FilterEffect effect, Rect bounds, EffectTargets targets)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(effect, resource);
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);
        activator.Apply(context);
        activator.Flush(false);
    }

    private static EffectTargets CreateSolidTargets(Rect bounds, Color color)
    {
        using RenderTarget renderTarget = RenderTarget.Create((int)bounds.Width, (int)bounds.Height)
            ?? throw new InvalidOperationException("A CPU render target is required for this test.");
        using (var canvas = new ImmediateCanvas(
                   renderTarget,
                   density: 1,
                   maxWorkingScale: 1,
                   logicalSize: bounds.Size))
        {
            canvas.Clear(color);
        }

        return new EffectTargets
        {
            new EffectTarget(renderTarget, bounds, EffectiveScale.At(1)),
        };
    }

    private static SKColor ReadCenterPixel(EffectTarget target)
    {
        using Bitmap bitmap = target.RenderTarget!.Snapshot();
        return bitmap.SKBitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
    }

    private static SKColor ReadCenterPixel(Bitmap bitmap)
        => bitmap.SKBitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);

    private static void DrawLegacyPattern(ImmediateCanvas canvas)
    {
        canvas.Clear();
        canvas.DrawRectangle(
            new Rect(0.25f, 0.25f, 8.25f, 6.25f),
            Brushes.Resource.OrangeRed,
            null);
        canvas.DrawRectangle(
            new Rect(2.25f, 1.75f, 3.5f, 2.5f),
            Brushes.Resource.White,
            null);
    }

    private static void RenderMaterializedEffect(FilterEffect effect, Vector translation)
    {
        var bounds = new Rect(8, 6, 12, 10);
        using var source = new CpuRenderTarget(12, 10);
        source.Value.Canvas.Clear(SKColors.White);
        source.Value.Flush();
        using var root = new FilterEffectRenderNode(
            effect.ToResource(CompositionContext.Default));
        root.AddChild(new MaterializedInputNode(source, bounds));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(0, 0, 32, 24),
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                },
                TargetFactory = new CpuTargetFactory(),
            });
        using var destination = new CpuRenderTarget(32, 24);
        using var canvas = new ImmediateCanvas(
            destination,
            logicalSize: new Size(32, 24));
        canvas.Clear();
        using (canvas.PushTransform(Matrix.CreateTranslation(translation)))
        {
            renderer.Render(canvas);
        }
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            throw new InvalidOperationException("delay-child-cleanup");
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

    private sealed class MaterializedInputNode(
        RenderTarget source,
        Rect bounds,
        float density = 1) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<RenderTarget> target = context.Borrow(
                source,
                "legacy-custom-materialized-input",
                version: 1);
            context.Publish(context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    target,
                    bounds,
                    EffectiveScale.At(density),
                    PixelRect.FromRect(bounds, density),
                    default,
                    RenderHitTestContract.OutputBounds)));
        }
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class LegacySuffixCallbackFilterEffect(
    Action<FilterEffectContext, FilterEffect.Resource> apply) : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        => apply(context, resource);

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource;
}
