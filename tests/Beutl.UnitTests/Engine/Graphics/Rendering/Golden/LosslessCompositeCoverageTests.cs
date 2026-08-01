using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// Guards the "rasterize once at the final density" contract: content whose buffer already lands on
/// exact device pixels must be copied, never resampled, and a genuine resample must stay inside the
/// range of the samples it interpolated.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class LosslessCompositeCoverageTests
{
    private static readonly PixelSize s_frame = new(200, 140);
    private static readonly PixelSize s_fractionalFrame = new(800, 400);

    [TestCase(1f)]
    [TestCase(2f)]
    public void EffectFreeCurvedGeometry_MatchesDirectRasterization(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource resource = CreateEllipse(effect: null);
            using Bitmap expected = RenderDirect(resource, density);
            using Bitmap actual = RenderThroughPipeline(resource, density);

            AssertByteIdentical(expected, actual, $"effect-free ellipse at density {density}");
        });
    }

    [TestCase(1f)]
    [TestCase(2f)]
    public void IdentityColorEffect_PreservesEdgeCoverage(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var identity = new Brightness();
            identity.Amount.CurrentValue = 100;
            using Drawable.Resource plain = CreateRectangle(effect: null);
            using Drawable.Resource filtered = CreateRectangle(identity);
            using Bitmap expected = RenderThroughPipeline(plain, density);
            using Bitmap actual = RenderThroughPipeline(filtered, density);

            AssertByteIdentical(expected, actual, $"identity Brightness at density {density}");
        });
    }

    [TestCase(0.25f)]
    [TestCase(0.75f)]
    public void IdentityColorEffect_AtFractionalDevicePosition_IsByteIdenticalToUnfiltered(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var identity = new Brightness();
            identity.Amount.CurrentValue = 100;
            using Drawable.Resource plain = CreateRectangle(
                width: 301,
                height: 201,
                effect: null);
            using Drawable.Resource filtered = CreateRectangle(
                width: 301,
                height: 201,
                identity);
            using Bitmap expected = RenderThroughPipeline(plain, density, s_fractionalFrame);
            using Bitmap actual = RenderThroughPipeline(filtered, density, s_fractionalFrame);

            AssertByteIdentical(
                expected,
                actual,
                $"identity Brightness at fractional device position and density {density}");
        });
    }

    [TestCase(0.25f)]
    [TestCase(0.75f)]
    public void IdentityColorEffect_OnFractionallyPositionedText_IsByteIdenticalToUnfiltered(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var identity = new Brightness();
            identity.Amount.CurrentValue = 100;
            using Drawable.Resource plain = CreateText(effect: null);
            using Drawable.Resource filtered = CreateText(identity);
            using Bitmap expected = RenderThroughPipeline(plain, density, s_fractionalFrame);
            using Bitmap actual = RenderThroughPipeline(filtered, density, s_fractionalFrame);

            AssertByteIdentical(
                expected,
                actual,
                $"identity Brightness on fractionally positioned text at density {density}");
        });
    }

    [TestCase(0.25f)]
    [TestCase(0.75f)]
    public void IdentityTypedShader_AtFractionalDevicePosition_IsByteIdenticalToUnfiltered(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource plain = CreateRectangle(
                width: 301,
                height: 201,
                effect: null);
            using Drawable.Resource filtered = CreateRectangle(
                width: 301,
                height: 201,
                new IdentityTypedShaderEffect());
            using Bitmap expected = RenderThroughPipeline(plain, density, s_fractionalFrame);
            using Bitmap actual = RenderThroughPipeline(filtered, density, s_fractionalFrame);

            AssertByteIdentical(
                expected,
                actual,
                $"identity typed shader at fractional device position and density {density}");
        });
    }

    [TestCase(false, TestName = "FractionalTranslationCacheMove_LegacyFilter_MatchesUncached")]
    [TestCase(true, TestName = "FractionalTranslationCacheMove_TypedShader_MatchesUncached")]
    public void FractionalTranslationCacheMove_MatchesUncached(bool typedShader)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffect.Resource effect = CreateIdentityEffectResource(typedShader);
            using TransformRenderNode cachedRoot = CreateCachedEffectTree(effect, translation: 0.25f);
            using var cachedRenderer = CreateNodeRenderer(cachedRoot, useRenderCache: true);
            using Bitmap warm = RenderNodeRendererToBitmap(cachedRenderer);

            cachedRoot.Update(
                Matrix.CreateTranslation(0.75f, 0.75f),
                TransformOperator.Prepend);
            using Bitmap actual = RenderNodeRendererToBitmap(cachedRenderer);

            using FilterEffect.Resource controlEffect = CreateIdentityEffectResource(typedShader);
            using TransformRenderNode controlRoot = CreateCachedEffectTree(
                controlEffect,
                translation: 0.75f,
                enableCaches: false);
            using var controlRenderer = CreateNodeRenderer(controlRoot, useRenderCache: false);
            using Bitmap expected = RenderNodeRendererToBitmap(controlRenderer);

            AssertByteIdentical(
                expected,
                actual,
                $"{(typedShader ? "typed shader" : "legacy filter")} after a device-phase cache move");
            Assert.That(
                cachedRoot.Children[0].Cache.IsCached,
                Is.False,
                "A subtree rasterized against an ambient device grid must not publish a phase-ambiguous cache.");
        });
    }

    [Test]
    public void IntegralDestinationTranslation_UsesEffectDensityWhenRejectingPhaseAmbiguousCache()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffect.Resource effect = CreateIdentityEffectResource(typedShader: false);
            using TransformRenderNode cachedRoot = CreateCachedEffectTree(effect, translation: 0);
            using var cachedRenderer = CreateNodeRenderer(
                cachedRoot,
                useRenderCache: true,
                maxWorkingScale: 0.75f);
            using Bitmap warm = RenderNodeRendererToBitmap(cachedRenderer, destinationTranslation: 1);
            using Bitmap actual = RenderNodeRendererToBitmap(cachedRenderer, destinationTranslation: 2);

            using FilterEffect.Resource controlEffect = CreateIdentityEffectResource(typedShader: false);
            using TransformRenderNode controlRoot = CreateCachedEffectTree(
                controlEffect,
                translation: 0,
                enableCaches: false);
            using var controlRenderer = CreateNodeRenderer(
                controlRoot,
                useRenderCache: false,
                maxWorkingScale: 0.75f);
            using Bitmap expected = RenderNodeRendererToBitmap(
                controlRenderer,
                destinationTranslation: 2);

            Assert.Multiple(() =>
            {
                AssertByteIdentical(
                    expected,
                    actual,
                    "identity effect after an integral destination move at density 0.75");
                Assert.That(
                    cachedRoot.Children[0].Cache.IsCached,
                    Is.False,
                    "The logical destination offset must be evaluated at the effect's working density.");
            });
        });
    }

    [Test]
    public void CancellingTransformChain_DoesNotPublishPhaseAmbiguousEffectCache()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffect.Resource effect = CreateIdentityEffectResource(typedShader: true);
            var source = new RectangleRenderNode(
                new Rect(20, 20, 101, 81),
                Brushes.Resource.White,
                pen: null);
            var filter = new FilterEffectRenderNode(effect);
            filter.AddChild(source);
            var inner = new TransformRenderNode(
                Matrix.CreateScale(new Vector(0.5f, 0.5f))
                    .Append(Matrix.CreateTranslation(0.75f, 0.75f)),
                TransformOperator.Prepend);
            inner.AddChild(filter);
            using var root = new TransformRenderNode(
                Matrix.CreateScale(new Vector(2, 2)),
                TransformOperator.Prepend);
            root.AddChild(inner);
            source.Cache.ReportRenderCount(RenderNodeCache.Count);
            filter.Cache.ReportRenderCount(RenderNodeCache.Count);
            inner.Cache.ReportRenderCount(RenderNodeCache.Count);
            root.Cache.ReportRenderCount(RenderNodeCache.Count);
            using var renderer = CreateNodeRenderer(root, useRenderCache: true);

            using Bitmap first = RenderNodeRendererToBitmap(renderer);
            using Bitmap second = RenderNodeRendererToBitmap(renderer);

            Assert.Multiple(() =>
            {
                Assert.That(
                    filter.Cache.IsCached,
                    Is.False,
                    "The composed transform is translation-only even though neither transform is.");
                AssertByteIdentical(first, second, "repeated cancelling-transform render");
            });
        });
    }

    [Test]
    public void FusedShaderStages_ReportTheSameGridAwareFootprintsAsStandaloneStages()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var fusedChain = new FootprintObservingShaderChainNode();
            using TransformRenderNode fusedRoot = WrapInFractionalTranslation(fusedChain);
            using var fusedRenderer = CreateNodeRenderer(
                fusedRoot,
                useRenderCache: false,
                fusionMode: FusionMode.Enabled);
            using Bitmap fusedBitmap = RenderNodeRendererToBitmap(fusedRenderer);

            using var standaloneChain = new FootprintObservingShaderChainNode();
            using TransformRenderNode standaloneRoot = WrapInFractionalTranslation(standaloneChain);
            using var standaloneRenderer = CreateNodeRenderer(
                standaloneRoot,
                useRenderCache: false,
                fusionMode: FusionMode.Disabled);
            using Bitmap standaloneBitmap = RenderNodeRendererToBitmap(standaloneRenderer);

            Assert.That(fusedChain.Observations, Has.Count.EqualTo(2));
            Assert.That(standaloneChain.Observations, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(fusedChain.Observations, Is.EqualTo(standaloneChain.Observations));
                foreach (ShaderFootprintObservation observation in fusedChain.Observations)
                {
                    Assert.That(
                        observation.DeviceBounds
                            .ToRect(observation.WorkingScale)
                            .Translate(-observation.DeviceGridOffset)
                            .Position,
                        Is.EqualTo(observation.LogicalOrigin));
                }
            });
        });
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void MosaicClamp_PreservesConstantOpaqueSourceAndFarEdgeAlpha(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var mosaic = new MosaicEffect();
            mosaic.TileSize.CurrentValue = new Size(20, 20);
            mosaic.Origin.CurrentValue = RelativePoint.Center;
            var frame = new PixelSize(180, 180);
            using Drawable.Resource plain = CreateRectangle(
                width: 180,
                height: 180,
                effect: null,
                alignmentX: AlignmentX.Left,
                alignmentY: AlignmentY.Top);
            using Drawable.Resource filtered = CreateRectangle(
                width: 180,
                height: 180,
                mosaic,
                AlignmentX.Left,
                AlignmentY.Top);
            using Bitmap expected = RenderThroughPipeline(plain, density, frame);
            using Bitmap actual = RenderThroughPipeline(filtered, density, frame);

            ReadOnlySpan<ushort> expectedPixels = expected.GetPixelSpan<ushort>();
            ReadOnlySpan<ushort> actualPixels = actual.GetPixelSpan<ushort>();
            int differingChannels = 0;
            float minimumFarEdgeAlpha = 1;
            for (int y = 0; y < actual.Height; y++)
            {
                for (int x = 0; x < actual.Width; x++)
                {
                    int pixelOffset = ((y * actual.Width) + x) * 4;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        if (actualPixels[pixelOffset + channel] != expectedPixels[pixelOffset + channel])
                            differingChannels++;
                    }

                    if (x == actual.Width - 1 || y == actual.Height - 1)
                    {
                        minimumFarEdgeAlpha = Math.Min(
                            minimumFarEdgeAlpha,
                            (float)BitConverter.UInt16BitsToHalf(actualPixels[pixelOffset + 3]));
                    }
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    differingChannels,
                    Is.Zero,
                    "Mosaic over a constant opaque source must preserve every premultiplied channel.");
                Assert.That(
                    minimumFarEdgeAlpha,
                    Is.EqualTo(1).Within(0.001f),
                    "Clamp sampling must preserve opaque alpha at the source's right and bottom edges.");
            });
        });
    }

    [Test]
    public void ScaledComposite_StaysInsideTheSourceRange()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create(16, 16)
                                        ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
            using (var sourceCanvas = new ImmediateCanvas(source, 1f, logicalSize: new Size(16, 16)))
            {
                sourceCanvas.Clear();
                using var dark = new SKPaint { IsAntialias = false, Color = new SKColor(64, 64, 64) };
                using var bright = new SKPaint { IsAntialias = false, Color = new SKColor(192, 192, 192) };
                sourceCanvas.Canvas.DrawRect(SKRect.Create(0, 0, 16, 8), dark);
                sourceCanvas.Canvas.DrawRect(SKRect.Create(0, 8, 16, 8), bright);
            }

            using RenderTarget destination = RenderTarget.Create(64, 64)
                                             ?? throw new InvalidOperationException(
                                                 "RenderTarget.Create returned null.");
            using (var canvas = new ImmediateCanvas(destination, 1f, logicalSize: new Size(64, 64)))
            {
                canvas.Clear();
                canvas.DrawRenderTargetScaled(source, new Rect(4, 4, 40, 40));
            }

            using Bitmap result = destination.Snapshot();
            double darkPlateau = ReadRed(result, 22, 10);
            double brightPlateau = ReadRed(result, 22, 38);
            double minimum = double.PositiveInfinity;
            double maximum = double.NegativeInfinity;
            for (int y = 6; y < 42; y++)
            {
                for (int x = 6; x < 42; x++)
                {
                    double value = ReadRed(result, x, y);
                    minimum = Math.Min(minimum, value);
                    maximum = Math.Max(maximum, value);
                }
            }

            // One RgbaF16 step near the bright plateau is ~4.9e-4, so only a real kernel lobe clears this.
            const double halfFloatTolerance = 1e-3;
            TestContext.WriteLine(
                $"plateaus [{darkPlateau:F6}, {brightPlateau:F6}], resampled [{minimum:F6}, {maximum:F6}]");
            Assert.Multiple(() =>
            {
                Assert.That(darkPlateau, Is.GreaterThan(0),
                    "The scaled composite must retain the nonzero dark source plateau.");
                Assert.That(brightPlateau, Is.GreaterThan(darkPlateau + halfFloatTolerance),
                    "The scaled composite must retain two distinct source plateaus.");
                Assert.That(maximum, Is.LessThanOrEqualTo(brightPlateau + halfFloatTolerance),
                    "The resample kernel overshot the brightest sample it interpolated.");
                Assert.That(minimum, Is.GreaterThanOrEqualTo(darkPlateau - halfFloatTolerance),
                    "The resample kernel undershot the darkest sample it interpolated.");
            });
        });
    }

    private static double ReadRed(Bitmap bitmap, int x, int y)
        => (double)BitConverter.UInt16BitsToHalf(bitmap.GetPixelSpan<ushort>()[((y * bitmap.Width) + x) * 4]);

    private static PixelRect MeasureAlphaBounds(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int left = bitmap.Width;
        int top = bitmap.Height;
        int right = 0;
        int bottom = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                int pixelOffset = ((y * bitmap.Width) + x) * 4;
                float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[pixelOffset + 3]);
                Assert.That(
                    float.IsFinite(alpha),
                    Is.True,
                    $"The footprint alpha at ({x}, {y}) must be finite.");
                if (alpha <= 0)
                    continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        Assert.That(right, Is.GreaterThan(left), "The footprint fixture must render non-transparent pixels.");
        Assert.That(bottom, Is.GreaterThan(top), "The footprint fixture must render non-transparent pixels.");
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static void AssertByteIdentical(Bitmap expected, Bitmap actual, string scenario)
    {
        int differing = 0;
        double maximum = 0;
        ReadOnlySpan<ushort> a = expected.GetPixelSpan<ushort>();
        ReadOnlySpan<ushort> b = actual.GetPixelSpan<ushort>();
        for (int index = 0; index < a.Length; index++)
        {
            double left = (double)BitConverter.UInt16BitsToHalf(a[index]);
            double right = (double)BitConverter.UInt16BitsToHalf(b[index]);
            if (a[index] != b[index])
                differing++;
            maximum = Math.Max(maximum, Math.Abs(left - right));
        }

        Assert.Multiple(() =>
        {
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
            Assert.That(differing, Is.Zero,
                $"{scenario}: {differing} channels differ, maximum delta {maximum:F6}.");
        });
    }

    private static Drawable.Resource CreateEllipse(FilterEffect? effect)
    {
        var shape = new EllipseShape();
        shape.Width.CurrentValue = 120;
        shape.Height.CurrentValue = 80;
        return Configure(shape, effect);
    }

    private static Drawable.Resource CreateRectangle(FilterEffect? effect)
        => CreateRectangle(120, 80, effect);

    private static Drawable.Resource CreateRectangle(
        float width,
        float height,
        FilterEffect? effect,
        AlignmentX alignmentX = AlignmentX.Center,
        AlignmentY alignmentY = AlignmentY.Center)
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = width;
        shape.Height.CurrentValue = height;
        shape.AlignmentX.CurrentValue = alignmentX;
        shape.AlignmentY.CurrentValue = alignmentY;
        return Configure(shape, effect);
    }

    private static Drawable.Resource CreateText(FilterEffect? effect)
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var text = new TextBlock();
        text.AlignmentX.CurrentValue = AlignmentX.Center;
        text.AlignmentY.CurrentValue = AlignmentY.Center;
        text.FontFamily.CurrentValue = typeface.FontFamily;
        text.FontStyle.CurrentValue = typeface.Style;
        text.FontWeight.CurrentValue = typeface.Weight;
        text.Size.CurrentValue = 160;
        text.Fill.CurrentValue = Brushes.White;
        text.Text.CurrentValue = "Phase";
        if (effect is not null)
            text.FilterEffect.CurrentValue = effect;
        return text.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource Configure(Shape shape, FilterEffect? effect)
    {
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Fill.CurrentValue = Brushes.White;
        if (effect is not null)
            shape.FilterEffect.CurrentValue = effect;
        return shape.ToResource(CompositionContext.Default);
    }

    private static FilterEffect.Resource CreateIdentityEffectResource(bool typedShader)
    {
        FilterEffect effect;
        if (typedShader)
        {
            effect = new IdentityTypedShaderEffect();
        }
        else
        {
            var brightness = new Brightness();
            brightness.Amount.CurrentValue = 100;
            effect = brightness;
        }

        return effect.ToResource(CompositionContext.Default);
    }

    private static TransformRenderNode CreateCachedEffectTree(
        FilterEffect.Resource effect,
        float translation,
        bool enableCaches = true)
    {
        var source = new RectangleRenderNode(
            new Rect(20, 20, 101, 81),
            Brushes.Resource.White,
            pen: null);
        var filter = new FilterEffectRenderNode(effect);
        filter.AddChild(source);
        var transform = new TransformRenderNode(
            Matrix.CreateTranslation(translation, translation),
            TransformOperator.Prepend);
        transform.AddChild(filter);
        if (enableCaches)
        {
            source.Cache.ReportRenderCount(RenderNodeCache.Count);
            filter.Cache.ReportRenderCount(RenderNodeCache.Count);
            transform.Cache.ReportRenderCount(RenderNodeCache.Count);
        }

        return transform;
    }

    private static RenderNodeRenderer CreateNodeRenderer(
        RenderNode root,
        bool useRenderCache,
        FusionMode fusionMode = FusionMode.Enabled,
        float maxWorkingScale = 1)
        => new(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Delivery,
                    TargetDomain = new Rect(0, 0, 180, 140),
                    OutputScale = 1,
                    MaxWorkingScale = maxWorkingScale,
                    CacheOptions = new Beutl.Graphics.Rendering.Cache.RenderCacheOptions(useRenderCache, Beutl.Graphics.Rendering.Cache.RenderCacheRules.Default),
                    FusionMode = fusionMode,
                    Purpose = RenderRequestPurpose.Frame,
                },
            });

    private static Bitmap RenderNodeRendererToBitmap(
        RenderNodeRenderer renderer,
        float destinationTranslation = 0)
    {
        using RenderTarget target = RenderTarget.Create(180, 140)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, logicalSize: new Size(180, 140));
        canvas.Clear();
        using (canvas.PushTransform(
                   Matrix.CreateTranslation(destinationTranslation, destinationTranslation)))
        {
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    private static TransformRenderNode WrapInFractionalTranslation(RenderNode child)
    {
        var transform = new TransformRenderNode(
            Matrix.CreateTranslation(0.75f, 0.75f),
            TransformOperator.Prepend);
        transform.AddChild(child);
        return transform;
    }

    private readonly record struct ShaderFootprintObservation(
        PixelRect DeviceBounds,
        PixelSize DeviceSize,
        Point LogicalOrigin,
        Vector DeviceGridOffset,
        float WorkingScale);

    private sealed class FootprintObservingShaderChainNode : ContainerRenderNode
    {
        public FootprintObservingShaderChainNode()
        {
            AddChild(new RectangleRenderNode(
                new Rect(20, 20, 101, 81),
                Brushes.Resource.White,
                pen: null));
        }

        public List<ShaderFootprintObservation> Observations { get; } = [];

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle current = context.Inputs.Single();
            for (int stage = 0; stage < 2; stage++)
            {
                int capturedStage = stage;
                ShaderDescription description = ShaderDescription.CurrentPixel(
                    "uniform float gain; half4 apply(half4 color) { return color * gain; }",
                    bindings => bindings.Uniform(
                        "gain",
                        1f,
                        (writer, value, execution) =>
                        {
                            Observations.Add(new ShaderFootprintObservation(
                                execution.DeviceBounds,
                                execution.DeviceSize,
                                execution.LogicalOrigin,
                                execution.DeviceGridOffset,
                                execution.WorkingScale));
                            writer.Set(value);
                        },
                        structuralKey: (typeof(FootprintObservingShaderChainNode), capturedStage)));
                current = context.Shader(current, description);
            }

            context.Publish(current);
        }
    }

    private static Bitmap RenderDirect(Drawable.Resource resource, float density)
    {
        var shape = (Shape)resource.GetOriginal();
        var shapeResource = (Shape.Resource)resource;
        Size frameSize = s_frame.ToSize(1);
        Size shapeSize = shape.MeasureInternal(frameSize, resource);
        Matrix transform = shape.GetTransformMatrix(frameSize, shapeSize, resource);
        Geometry.Resource geometry = shapeResource.GetGeometry()
                                     ?? throw new InvalidOperationException("The shape produced no geometry.");

        using RenderTarget target = CreateFrameTarget(density);
        using var canvas = new ImmediateCanvas(target, density, logicalSize: frameSize);
        canvas.Clear();
        using (canvas.PushTransform(transform))
        {
            canvas.DrawGeometry(geometry, shapeResource.Fill, shapeResource.Pen);
        }

        return target.Snapshot();
    }

    private static Bitmap RenderThroughPipeline(Drawable.Resource resource, float density)
        => RenderThroughPipeline(resource, density, s_frame);

    private static Bitmap RenderThroughPipeline(
        Drawable.Resource resource,
        float density,
        PixelSize frame)
    {
        using var node = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(node, frame.ToSize(1), density))
        {
            resource.GetOriginal().Render(context, resource);
        }

        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Delivery,
                    TargetDomain = new Rect(default, frame.ToSize(1)),
                    OutputScale = density,
                    MaxWorkingScale = density,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
            });

        using RenderTarget target = CreateFrameTarget(density, frame);
        using var canvas = new ImmediateCanvas(target, density, logicalSize: frame.ToSize(1));
        canvas.Clear();
        renderer.Render(canvas);
        return target.Snapshot();
    }

    private static RenderTarget CreateFrameTarget(float density)
        => CreateFrameTarget(density, s_frame);

    private static RenderTarget CreateFrameTarget(float density, PixelSize frame)
        => RenderTarget.Create(
               (int)MathF.Ceiling(frame.Width * density),
               (int)MathF.Ceiling(frame.Height * density))
           ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
}

[SuppressResourceClassGeneration]
internal sealed partial class IdentityTypedShaderEffect : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        => context.Shader(ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }"));

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource;
}
