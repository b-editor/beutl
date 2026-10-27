using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
[NonParallelizable]
public sealed class RadialBlurBackendTests
{
    [TestCase(RadialBlurMode.Zoom)]
    [TestCase(RadialBlurMode.Spin)]
    public void ShaderCache_RecreatesForReplacementContextAndReusesWithinContext(RadialBlurMode mode)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new RadialBlur { Mode = { Value = mode }, Amount = { Value = 50 } };
            using var brush = (Brush.Resource)new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
            using var root = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
            root.AddChild(new RectangleRenderNode(new Rect(4, 4, 16, 8), brush, null));
            using var renderer = new RenderNodeRenderer(root,
                new RenderNodeRenderRequest { Intent = RenderIntent.Delivery, CacheOptions = RenderCacheOptions.Disabled });
            ushort[] expected = Render();
            Assert.That(expected, Has.Some.Not.Zero);
            long misses = renderer.ProgramCacheStatistics.Misses;
            Assert.That(Render(), Is.EqualTo(expected));
            Assert.That(renderer.ProgramCacheStatistics.Misses, Is.EqualTo(misses));
            Assert.That(renderer.ProgramCacheStatistics.Hits, Is.GreaterThan(0));

            using IGraphicsContext replacement = GraphicsContextFactory.CreateContext();
            using (InstalledGraphicsScope.Install(replacement))
            {
                Assert.That(Render(), Is.EqualTo(expected));
                Assert.That(renderer.ProgramCacheStatistics.Misses, Is.GreaterThan(misses));
                misses = renderer.ProgramCacheStatistics.Misses;
                Assert.That(Render(), Is.EqualTo(expected));
                Assert.That(renderer.ProgramCacheStatistics.Misses, Is.EqualTo(misses));
            }
            Assert.That(Render(), Is.EqualTo(expected));
            Assert.That(renderer.ProgramCacheStatistics.Misses, Is.GreaterThan(misses));

            ushort[] Render()
            {
                using RenderNodeRasterization output = renderer.Rasterize();
                return output.Bitmap!.GetPixelSpan<ushort>().ToArray();
            }
        });
    }

    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void CpuRenderer_RejectsRadialBlurWithoutFallback(RenderIntent intent)
    {
        var bounds = new Rect(0, 0, 16, 16);
        using var brush = (Brush.Resource)new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        using var root = new FilterEffectRenderNode(new RadialBlur().ToResource(CompositionContext.Default));
        root.AddChild(new RectangleRenderNode(bounds, brush, null));
        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest { Intent = intent },
            new CpuRenderTargetFactory());

        Assert.Throws<NotSupportedException>(() => renderer.Rasterize().Dispose());
    }

    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void StandaloneCpuExecutor_RejectsRadialBlurWithoutFallback(RenderIntent intent)
    {
        var bounds = new Rect(0, 0, 16, 16);
        using RenderTarget backing = CpuRenderTargets.Create(16, 16);
        backing.Value.Canvas.Clear(SKColors.Red);
        using var targets = new EffectTargets { new EffectTarget(backing, bounds, EffectiveScale.At(1)) };
        var effect = new RadialBlur();
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);
        effect.ApplyTo(context, resource);
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(targets, builder, intent,
            RenderRequestPurpose.Auxiliary, drawableBrushMaterializer: null);

        Assert.Throws<NotSupportedException>(() => executor.Apply(context));
    }

    [TestCase(RadialBlurMode.Zoom, RenderIntent.Preview)]
    [TestCase(RadialBlurMode.Spin, RenderIntent.Preview)]
    [TestCase(RadialBlurMode.Zoom, RenderIntent.Delivery)]
    [TestCase(RadialBlurMode.Spin, RenderIntent.Delivery)]
    public void DeclinedOutputAllocation_KeepsPreviewSourceButFailsDelivery(RadialBlurMode mode, RenderIntent intent)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, 16, 16);
            using RenderTarget backing = RenderTarget.Create(16, 16)!;
            backing.Value.Canvas.Clear(SKColors.Red);
            backing.Value.Canvas.Flush();
            using var targets = new EffectTargets { new EffectTarget(backing, bounds, EffectiveScale.At(1)) };
            EffectTarget source = targets[0];
            using Bitmap expected = source.RenderTarget!.Snapshot();
            var factory = new DecliningTargetFactory();
            using var pool = new RenderTargetPool(factory);
            using RenderTargetLeaseSession session = pool.BeginSession(intent);
            using var builder = new SKImageFilterBuilder();
            using var executor = new FilterEffectExecutor(targets, builder, intent, RenderRequestPurpose.Auxiliary,
                1, 1, 1, default, renderTargetLeaseSession: session);
            var effect = new RadialBlur { Mode = { Value = mode } };
            using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
            using var context = new FilterEffectContext(bounds);
            context.ApplyTransactional(effect, resource);

            if (intent == RenderIntent.Preview)
                Assert.DoesNotThrow(() => executor.Apply(context));
            else
                Assert.That(() => executor.Apply(context), Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("could not allocate"));

            Assert.Multiple(() =>
            {
                Assert.That(factory.Declined, Is.GreaterThan(0));
                Assert.That(session.ContentDropObserved, Is.EqualTo(intent == RenderIntent.Preview));
                Assert.That(targets.Single().Bounds, Is.EqualTo(source.Bounds));
            });
            using Bitmap actual = targets.Single().RenderTarget!.Snapshot();
            Assert.That(actual.GetPixelSpan<ushort>().ToArray(), Is.EqualTo(expected.GetPixelSpan<ushort>().ToArray()));
        });
    }

    [TestCase(RadialBlurMode.Zoom)]
    [TestCase(RadialBlurMode.Spin)]
    public void RadialBlur_BetweenSkslStages_PreservesOrderAndUsesShaderGraph(RadialBlurMode mode)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var before = new TestShaderNode(ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return color * 0.5; }"));
            using var radial = new FilterEffectRenderNode(new RadialBlur { Mode = { Value = mode } }
                .ToResource(CompositionContext.Default));
            using var after = new TestShaderNode(ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return half4(color.g, color.b, color.r, color.a); }"));
            using var brush = (Brush.Resource)new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
            before.AddChild(new RectangleRenderNode(new Rect(16, 16, 16, 16), brush, null));
            radial.AddChild(before);
            after.AddChild(radial);
            using var renderer = new RenderNodeRenderer(after,
                new RenderNodeRenderRequest { Intent = RenderIntent.Delivery, CacheOptions = RenderCacheOptions.Disabled });
            using RenderNodeRasterization output = renderer.Rasterize();
            Half[] pixels = output.Bitmap!.GetPixelSpan<Half>().ToArray();
            Assert.That(pixels.Max(value => (float)value), Is.EqualTo(0.5f).Within(0.001f));
            for (int i = 0; i < pixels.Length; i += 4)
            {
                Assert.That((float)pixels[i], Is.Zero);
                Assert.That((float)pixels[i + 1], Is.Zero);
                Assert.That((float)pixels[i + 2], Is.EqualTo((float)pixels[i + 3]).Within(0.001f));
            }
        });
    }

    [TestCase(RadialBlurMode.Zoom, 0.5f, false, 32)]
    [TestCase(RadialBlurMode.Zoom, 1f, false, 32)]
    [TestCase(RadialBlurMode.Zoom, 2f, true, 32)]
    [TestCase(RadialBlurMode.Spin, 0.5f, true, 32)]
    [TestCase(RadialBlurMode.Spin, 1f, false, 32)]
    [TestCase(RadialBlurMode.Spin, 2f, false, 32)]
    [TestCase(RadialBlurMode.Zoom, 1f, true, 128)]
    [TestCase(RadialBlurMode.Spin, 1f, true, 128)]
    public void DefaultRenderer_UsesGlslWithFractionalBoundsAndTransparency(RadialBlurMode mode, float scale, bool crop, int samples)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new RadialBlur
            {
                Mode = { Value = mode },
                Amount = { Value = 47 },
                Samples = { Value = samples },
                Center = { Value = new RelativePoint(0.3f, 0.7f, RelativeUnit.Relative) },
            };
            var gradient = new LinearGradientBrush();
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(210, 255, 32, 64), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(64, 16, 255, 128), 1));
            using var brush = (Brush.Resource)gradient.ToResource(CompositionContext.Default);
            using var root = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
            var bounds = new Rect(17.25f, 21.75f, 40, 26);
            root.AddChild(new RectangleRenderNode(bounds, brush, null));
            var request = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = new Rect(0, 0, 100, 100),
                RequestedRegion = crop ? new Rect(23, 25, 30, 20) : null,
                OutputScale = scale,
                MaxWorkingScale = scale,
                CacheOptions = RenderCacheOptions.Disabled,
            };
            using var renderer = new RenderNodeRenderer(root, request);
            using RenderNodeRasterization output = renderer.Rasterize();
            Half[] pixels = output.Bitmap!.GetPixelSpan<Half>().ToArray();
            Assert.That(pixels.Max(value => (float)value), Is.GreaterThan(0.05f));
            for (int i = 0; i < pixels.Length; i += 4)
            {
                float alpha = (float)pixels[i + 3];
                Assert.That(alpha, Is.InRange(0, 210f / 255 + 0.001f));
                for (int channel = 0; channel < 3; channel++)
                    Assert.That((float)pixels[i + channel], Is.InRange(0, alpha + 0.001f));
            }

            if (crop)
            {
                using var fullRenderer = new RenderNodeRenderer(root, request with { RequestedRegion = null });
                using RenderNodeRasterization full = fullRenderer.Rasterize();
                PixelPoint offset = PixelRect.FromRect(output.Bounds, scale).Position
                                    - PixelRect.FromRect(full.Bounds, scale).Position;
                // Interior pixels only (the crop edge antialiases); compositing can round them one half-float step apart.
                for (int y = 1; y < output.Bitmap.Height - 1; y++)
                {
                    ReadOnlySpan<ushort> actual = output.Bitmap.GetRow<ushort>(y).Slice(4, (output.Bitmap.Width - 2) * 4);
                    ReadOnlySpan<ushort> expected = full.Bitmap!.GetRow<ushort>(y + offset.Y)
                        .Slice((offset.X + 1) * 4, actual.Length);
                    Assert.That(actual.ToArray(), Is.EqualTo(expected.ToArray()).Within(1),
                        "Cropping must preserve the blur center and sampling positions.");
                }
            }
        });
    }

    private sealed class TestShaderNode(ShaderDescription description) : ContainerRenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            foreach (RenderFragmentHandle input in context.Inputs)
                context.Publish(context.Shader(input, description));
        }
    }

    private sealed class DecliningTargetFactory : IRenderTargetFactory
    {
        public int Declined { get; private set; }

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            Declined++;
            return null;
        }
    }
}
