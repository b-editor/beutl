using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture, NonParallelizable]
public sealed class SrgbCompositionTests
{
    private static readonly Rect Frame = new(0, 0, 240, 80);

    [Test]
    public void FractionalRectangle_ComposesCoverageInSrgb()
    {
        using RenderTarget target = RenderTarget.Create(240, 80)!;
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery))
        {
            canvas.Clear(Colors.Black);
            canvas.DrawRectangle(new Rect(60.8f, 0, 120, 80), Brushes.Resource.White, null);
        }

        using Bitmap snapshot = target.Snapshot();
        using Bitmap encoded = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(snapshot.ColorSpace, Is.EqualTo(BitmapColorSpace.Srgb));
            Assert.That(Red(encoded, 60, 40), Is.EqualTo(51));
        });
    }

    [Test]
    public void GpuFractionalRectangle_ComposesCoverageInSrgb()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget target = RenderTarget.Create(240, 80)!;
            using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery))
            {
                canvas.Clear(Colors.Black);
                canvas.DrawRectangle(new Rect(60.8f, 0, 120, 80), Brushes.Resource.White, null);
            }
            using Bitmap snapshot = target.Snapshot();
            using Bitmap encoded = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
            // GPU coverage and the 8-bit dither can differ by one code from the analytic 20% value.
            Assert.That(Red(encoded, 60, 40), Is.EqualTo(51).Within(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LinearDestination_CompositesInLinearWhetherOrNotExecutionExpands(bool expanded)
    {
        var node = new ContainerRenderNode();
        node.AddChild(new RectangleRenderNode(new Rect(60.8f, 0, 120, 80), Brushes.Resource.White, null));
        // Reading the whole frame back expands execution past a narrower requested region.
        node.AddChild(new FrameReadbackNode());
        using RenderTarget target = RenderTarget.Create(240, 80, RenderTargetPixelFormat.LinearPremultipliedRgba16Float)!;
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery))
        {
            canvas.Clear(Colors.Black);
            using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = Frame,
                RequestedRegion = expanded ? new Rect(40, 0, 160, 80) : null,
                OutputScale = 1,
                CacheOptions = RenderCacheOptions.Disabled,
            }, new CpuTargetFactory());
            renderer.Render(canvas);
        }

        using Bitmap snapshot = target.Snapshot();
        using Bitmap encoded = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        // 20% coverage composited in linear light encodes to ~124; composited in sRGB it stays 51.
        Assert.That(Red(encoded, 60, 40), Is.EqualTo(124).Within(1));
    }

    [Test]
    public void IdentityFilter_PreservesSrgbCoverageWhenCompositedOverBlack()
    {
        using FilterEffect.Resource resource = new ColorGrading().ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new RectangleRenderNode(new Rect(60.8f, 0, 120, 80), Brushes.Resource.White, null));
        using Bitmap frame = Render(node);
        Assert.That(Red(frame, 60, 40), Is.EqualTo(51));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ColorGrading_ExposureRunsInLinearSpace(bool fused)
    {
        var grading = new ColorGrading();
        grading.Exposure.CurrentValue = 1;
        using FilterEffect.Resource resource = grading.ToResource(CompositionContext.Default);
        using Brush.Resource gray = new SolidColorBrush(Colors.Gray).ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new RectangleRenderNode(Frame, gray, null));
        using Bitmap frame = Render(node, fused ? FusionMode.Enabled : FusionMode.Disabled);
        Assert.That(Red(frame, 120, 40), Is.EqualTo(176).Within(1));
    }

    [Test]
    public void DirectShaderRun_IntoSrgbComposition_ComputesInLinearSpace()
    {
        var grading = new ColorGrading();
        grading.Exposure.CurrentValue = 1;
        using FilterEffect.Resource resource = grading.ToResource(CompositionContext.Default);
        using Brush.Resource gray = new SolidColorBrush(Colors.Gray).ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new RectangleRenderNode(Frame, gray, null));
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            TargetDomain = Frame,
            OutputScale = 1,
            CacheOptions = RenderCacheOptions.Disabled,
        }, new CpuTargetFactory());
        using RenderTarget target = RenderTarget.Create(240, 80)!;
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery))
        {
            canvas.Clear(Colors.Black);
            renderer.Render(canvas);
        }

        // Only the vector input materializes; the shader run draws straight into the sRGB target.
        Assert.That(renderer.LastExecutionStatistics.IntermediateTargetAcquisitions, Is.EqualTo(1));

        using Bitmap snapshot = target.Snapshot();
        using Bitmap frame = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        // Doubling linear gray 128 gives sRGB 176; doubling the encoded value would clip to 255.
        Assert.That(Red(frame, 120, 40), Is.EqualTo(176).Within(1));
    }

    [Test]
    public void Blur_AveragesOpaqueBlackAndWhiteInLinearSpace()
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(8, 0);
        using FilterEffect.Resource resource = blur.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        var input = new LayerRenderNode(Frame);
        input.AddChild(new RectangleRenderNode(Frame, Brushes.Resource.Black, null));
        input.AddChild(new RectangleRenderNode(new Rect(120, 0, 120, 80), Brushes.Resource.White, null));
        node.AddChild(input);
        using Bitmap frame = Render(node);
        // A gamma-space blur produces ~134 here. Linear averaging produces ~192.
        Assert.That(Red(frame, 120, 40), Is.EqualTo(192).Within(2));
    }

    [Test]
    public void StandaloneBlur_ConvertsSrgbInputBeforeFiltering()
    {
        using RenderTarget source = RenderTarget.Create(240, 80)!;
        using (var canvas = new ImmediateCanvas(source, RenderIntent.Delivery))
        {
            canvas.Clear(Colors.Black);
            canvas.DrawRectangle(new Rect(120, 0, 120, 80), Brushes.Resource.White, null);
        }

        using var targets = new EffectTargets { new EffectTarget(source, Frame, EffectiveScale.At(1)) };
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(targets, builder, RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary, drawableBrushMaterializer: null);
        using var context = new FilterEffectContext(Frame);
        context.Blur(new Size(8, 0));
        executor.Apply(context);
        executor.Flush();
        EffectTarget result = executor.CurrentTargets.Single();
        using Bitmap linear = result.RenderTarget!.Snapshot();
        using Bitmap gamma = linear.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        int x = (int)(120 - result.Bounds.X);
        int y = (int)(40 - result.Bounds.Y);
        Assert.Multiple(() =>
        {
            Assert.That(linear.ColorSpace, Is.EqualTo(BitmapColorSpace.LinearSrgb));
            Assert.That(Red(gamma, x, y), Is.EqualTo(192).Within(2));
        });
    }

    [Test]
    public void DirectShadow_DecodesItsAuthoredColorBeforeLinearOperations()
    {
        var shadow = new DropShadow();
        shadow.ShadowOnly.CurrentValue = true;
        shadow.Sigma.CurrentValue = Size.Empty;
        shadow.Color.CurrentValue = Colors.Gray;
        using FilterEffect.Resource resource = shadow.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new RectangleRenderNode(Frame, Brushes.Resource.White, null));
        using Bitmap frame = Render(node);
        Assert.That(Red(frame, 120, 40), Is.EqualTo(128).Within(1));
    }

    [Test]
    public void Gradient_InterpolatesInTheCompositionSpace()
    {
        var gradient = new LinearGradientBrush();
        gradient.GradientStops.Add(new GradientStop(Colors.Black, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.White, 1));
        using Brush.Resource brush = gradient.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(Frame, brush, null);
        using Bitmap frame = Render(node);
        // Linear-sRGB interpolation encodes this midpoint as 188 rather than 128.
        Assert.That(Red(frame, 120, 40), Is.EqualTo(128).Within(1));
    }

    [TestCase(false, 0.0331f)]
    [TestCase(true, 0.4845f)]
    public void ColorSpaceBlit_PreservesAdditiveRgbAtZeroAlpha(bool encode, float expected)
    {
        var sourceFormat = encode ? RenderTargetPixelFormat.LinearPremultipliedRgba16Float
            : RenderTargetPixelFormat.SrgbPremultipliedRgba16Float;
        var destinationFormat = encode ? RenderTargetPixelFormat.SrgbPremultipliedRgba16Float
            : RenderTargetPixelFormat.LinearPremultipliedRgba16Float;
        using RenderTarget source = RenderTarget.Create(1, 1, sourceFormat)!;
        using SKRuntimeEffect effect = SKRuntimeEffect.CreateShader(
            "half4 main(float2 p) { return half4(0.2, 0, 0, 0); }", out _)!;
        using SKShader shader = new SKRuntimeShaderBuilder(effect).Build();
        using (var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src })
            source.Value.Canvas.DrawPaint(paint);
        using RenderTarget destination = RenderTarget.Create(1, 1, destinationFormat)!;
        using (var canvas = new ImmediateCanvas(destination, RenderIntent.Delivery))
            canvas.DrawRenderTarget(source, default);
        using Bitmap output = destination.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That((float)output.GetPixelSpan<Half>()[0], Is.EqualTo(expected).Within(0.001f));
            Assert.That((float)output.GetPixelSpan<Half>()[3], Is.Zero);
        });
    }

    [Test]
    public void Pool_DoesNotReuseCompositionTargetsForLinearEffects()
    {
        using var pool = new RenderTargetPool(null);
        using RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Delivery);
        RenderTargetLease gamma = session.Acquire(new PixelSize(8, 8));
        RenderTarget gammaTarget = gamma.Target;
        gamma.Dispose();
        RenderTargetLease linear = session.Acquire(new PixelSize(8, 8), RenderTargetPixelFormat.LinearPremultipliedRgba16Float);
        Assert.Multiple(() =>
        {
            Assert.That(linear.Target, Is.Not.SameAs(gammaTarget));
            Assert.That(linear.Target.ColorSpace, Is.EqualTo(BitmapColorSpace.LinearSrgb));
        });
        linear.Dispose();
        using RenderTargetLease gammaAgain = session.Acquire(new PixelSize(8, 8));
        Assert.That(gammaAgain.Target, Is.SameAs(gammaTarget));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SnapshotPaths_PreserveTheTargetsColorSpace(bool linear)
    {
        var format = linear ? RenderTargetPixelFormat.LinearPremultipliedRgba16Float
            : RenderTargetPixelFormat.SrgbPremultipliedRgba16Float;
        using RenderTarget target = RenderTarget.Create(8, 8, format)!;
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery))
            canvas.Clear(Colors.Gray);
        using Bitmap allocated = target.Snapshot();
        using Bitmap reused = target.CreateSnapshotBitmap();
        target.SnapshotInto(reused);
        using Bitmap asynchronous = await target.SnapshotAsync();
        Assert.Multiple(() =>
        {
            Assert.That(allocated.ColorSpace, Is.EqualTo(format.GetColorSpace()));
            Assert.That(reused.GetPixelSpan().SequenceEqual(allocated.GetPixelSpan()), Is.True);
            Assert.That(asynchronous.GetPixelSpan().SequenceEqual(allocated.GetPixelSpan()), Is.True);
        });
    }

    [TestCase(BitmapColorTransfer.Pq)]
    [TestCase(BitmapColorTransfer.Hlg)]
    public void ExtendedSrgbF16_PreservesHdrConversion(BitmapColorTransfer transfer)
    {
        using RenderTarget linear = RenderTarget.Create(8, 8, RenderTargetPixelFormat.LinearPremultipliedRgba16Float)!;
        using (var paint = new SKPaint())
        {
            paint.SetColor(new SKColorF(2, 2, 2, 1), BitmapColorSpace.LinearSrgb.SKColorSpace);
            linear.Value.Canvas.DrawPaint(paint);
        }

        using Bitmap input = linear.Snapshot();
        using RenderTarget composition = RenderTarget.Create(8, 8)!;
        using (var canvas = new ImmediateCanvas(composition, RenderIntent.Delivery))
            canvas.DrawRenderTarget(linear, default);
        using Bitmap gamma = composition.Snapshot();
        BitmapColorSpace hdr = BitmapColorSpaceMapping.BuildHdrColorSpace(transfer, BitmapColorPrimaries.Rec2020);
        using Bitmap direct = input.Convert(BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, hdr);
        using Bitmap viaGamma = gamma.Convert(BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, hdr);
        var expected = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(direct.GetPixelSpan());
        var actual = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(viaGamma.GetPixelSpan());
        for (int channel = 0; channel < 4; channel++)
            Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(20));
    }

    [Test]
    public void Preview_ToneMapsExtendedSrgbLikeLinearInput()
    {
        using var linear = new SKBitmap(new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Premul,
            BitmapColorSpace.LinearSrgb.SKColorSpace));
        using (var canvas = new SKCanvas(linear))
        using (var paint = new SKPaint())
        {
            paint.SetColor(new SKColorF(2, 2, 2, 1), BitmapColorSpace.LinearSrgb.SKColorSpace);
            canvas.DrawPaint(paint);
        }

        using var source = new Bitmap(linear.Copy());
        using Bitmap gamma = source.Convert(BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        using SKImage linearImage = SKImage.FromBitmap(linear);
        using SKImage gammaImage = SKImage.FromBitmap(gamma.SKBitmap);
        byte expected = ToneMappedRed(linearImage);
        byte actual = ToneMappedRed(gammaImage);
        Assert.Multiple(() =>
        {
            Assert.That(expected, Is.EqualTo(213).Within(1));
            Assert.That(actual, Is.EqualTo(expected).Within(1));
        });
    }

    [Test]
    public void ToneMappedPreview_SamplesWithHighQualityInterpolation()
    {
        using var linear = new SKBitmap(new SKImageInfo(4, 4, SKColorType.RgbaF16, SKAlphaType.Premul,
            BitmapColorSpace.LinearSrgb.SKColorSpace));
        using (var canvas = new SKCanvas(linear))
        using (var paint = new SKPaint())
        {
            paint.SetColor(new SKColorF(2, 2, 2, 1), BitmapColorSpace.LinearSrgb.SKColorSpace);
            canvas.DrawPaint(paint);
        }

        using SKImage image = SKImage.FromBitmap(linear);
        // BitmapView's default HighQuality mode samples with the Mitchell cubic resampler.
        var highQuality = new SKSamplingOptions(SKCubicResampler.Mitchell);

        Assert.That(ToneMappedRed(image, highQuality), Is.EqualTo(ToneMappedRed(image)).Within(1));
    }

    [Test]
    public void ToneMappedPreview_LeavesPqSourcesToColorManagement()
    {
        SKColorSpace pq = BitmapColorSpaceMapping.BuildHdrColorSpace(
            BitmapColorTransfer.Pq, BitmapColorPrimaries.Rec2020).SKColorSpace;
        using var source = new SKBitmap(new SKImageInfo(4, 4, SKColorType.RgbaF16, SKAlphaType.Premul, pq));
        source.Erase(SKColors.Gray);
        using SKImage image = SKImage.FromBitmap(source);

        using SKShader? shader = BitmapView.CreateToneMappingShader(image, SKSamplingOptions.Default,
            SKMatrix.Identity, 0, UIToneMappingOperator.Reinhard);

        Assert.That(shader, Is.Null);
    }

    private static byte ToneMappedRed(SKImage image)
        => ToneMappedRed(image, SKSamplingOptions.Default);

    private static byte ToneMappedRed(SKImage image, SKSamplingOptions sampling)
    {
        using SKShader? shader = BitmapView.CreateToneMappingShader(image, sampling,
            SKMatrix.Identity, 0, UIToneMappingOperator.Reinhard);
        Assert.That(shader, Is.Not.Null);
        using var output = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul,
            BitmapColorSpace.Srgb.SKColorSpace));
        using var canvas = new SKCanvas(output);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawPaint(paint);
        return output.GetPixel(0, 0).Red;
    }

    [Test]
    public void Blur_FlushDecodesItsSrgbInputWithoutAnExtraLinearCopy()
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(4, 4);
        using FilterEffect.Resource resource = blur.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        var shapes = new ContainerRenderNode();
        shapes.AddChild(new RectangleRenderNode(new Rect(40, 20, 60, 40), Brushes.Resource.White, null));
        shapes.AddChild(new RectangleRenderNode(new Rect(120, 20, 60, 40), Brushes.Resource.White, null));
        node.AddChild(shapes);
        var factory = new CountingTargetFactory();
        using Bitmap frame = Render(node, factory: factory);
        Assert.Multiple(() =>
        {
            // Per shape: its sRGB raster and the linear flush buffer; the flush draw decodes the former.
            Assert.That(factory.Created, Is.EqualTo(4));
            Assert.That(Red(frame, 70, 40), Is.EqualTo(255).Within(1));
        });
    }

    [Test]
    public void DrawableTexture_HandsLinearRgbTo3D()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var rect = new RectShape();
            rect.Width.CurrentValue = 16;
            rect.Height.CurrentValue = 16;
            rect.Fill.CurrentValue = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
            var definition = new DrawableTextureSource();
            definition.Drawable.CurrentValue = rect;
            definition.TextureWidth.CurrentValue = 16;
            definition.TextureHeight.CurrentValue = 16;
            using var source = (DrawableTextureSource.Resource)definition.ToResource(CompositionContext.Default);

            ITexture2D texture = source.GetTexture(context, 1f)
                ?? throw new AssertionException("The drawable texture was not created.");
            Assert.That(ReadTextureRed(texture, 8, 8), Is.EqualTo(MidGrayLinear).Within(0.002f));
        });
    }

    [Test]
    public void NestedTarget_HandsLinearRgbTo3D()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var domain = new Rect(0, 0, 16, 16);
            using var child = new SolidNestedChildNode(domain, Color.FromArgb(255, 128, 128, 128));
            using var parent = new NestedTextureProbeNode(child, domain);
            using var renderer = new RenderNodeRenderer(parent, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = domain,
                OutputScale = 1,
                MaxWorkingScale = 1,
                CacheOptions = RenderCacheOptions.Disabled,
            });
            using RenderNodeRasterization rasterization = renderer.Rasterize();

            Assert.That(parent.TextureRed, Is.EqualTo(MidGrayLinear).Within(0.002f));
        });
    }

    // sRGB 128/255 decoded to linear light.
    private const float MidGrayLinear = 0.2158f;

    private static float ReadTextureRed(ITexture2D texture, int x, int y)
    {
        texture.PrepareForSampling();
        byte[] pixels = texture.DownloadPixels();
        return (float)BitConverter.ToHalf(pixels, (y * texture.Width + x) * 8);
    }

    private static Bitmap Render(RenderNode node, FusionMode fusion = FusionMode.Enabled,
        IRenderTargetFactory? factory = null)
    {
        using RenderTarget target = RenderTarget.Create(240, 80)!;
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery);
        canvas.Clear(Colors.Black);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            TargetDomain = Frame,
            OutputScale = 1,
            FusionMode = fusion,
            CacheOptions = RenderCacheOptions.Disabled,
        }, factory ?? new CpuTargetFactory());
        renderer.Render(canvas);
        using Bitmap snapshot = target.Snapshot();
        return snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
    }

    private static byte Red(Bitmap bitmap, int x, int y) => bitmap.GetPixelSpan()[y * bitmap.RowBytes + x * 4 + 2];

    private class CpuTargetFactory : IRenderTargetFactory
    {
        public virtual RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
            => RenderTarget.Create(allocation.DeviceSize.Width, allocation.DeviceSize.Height, allocation.PixelFormat);
    }

    private sealed class CountingTargetFactory : CpuTargetFactory
    {
        public int Created { get; private set; }

        public override RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            Created++;
            return base.Create(allocation);
        }
    }

    private sealed class SolidNestedChildNode(Rect bounds, Color color) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    output.Canvas.Use(canvas => canvas.Clear(color));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class FrameReadbackNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.TargetCommand(
                [],
                TargetCommandDescription.CreateRequestLocal(
                    static session => session.UseSnapshot(static _ => { }),
                    TargetRegion.Region(Frame),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.Readback)));
        }
    }

    // Reads the nested texture the way a 3D surface binds it, from inside the consuming callback.
    private sealed class NestedTextureProbeNode(RenderNode child, Rect domain) : RenderNode
    {
        private static readonly RenderResourceSlot<NestedRenderTargetBinding> s_nested = new();

        public float TextureRed { get; private set; } = float.NaN;

        public override void Process(RenderNodeContext context)
        {
            RecordedNestedRenderTarget nested = context.RecordNestedTargetAtScale(child, domain, 1f);
            OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
                session => session.UseResource(
                    nested.Binding,
                    binding =>
                    {
                        ITexture2D texture = binding.GetTexture(domain, 1f)
                            ?? throw new AssertionException("The nested target has no texture.");
                        TextureRed = ReadTextureRed(texture, 8, 8);
                        using OpaqueRenderOutput output = session.CreateOutput(domain);
                        session.Publish(output);
                    }),
                OpaqueRenderBoundsContract.Source(domain),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [s_nested.Bind(nested.Binding)]);
            context.Publish(context.OpaqueSource(description));
        }
    }
}
