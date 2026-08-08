using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class FilterEffectRenderNodeTest
{
    private static FilterEffectRenderNode CreateNode(FilterEffect.Resource resource)
    {
        var node = new FilterEffectRenderNode(resource);
        node.AddChild(new EllipseRenderNode(
            new Rect(0, 0, 100, 100),
            Brushes.Resource.White,
            null));
        return node;
    }

    [Test]
    public void Measure_ShouldReportRecordedFilterOutput()
    {
        var effect = new Blur();
        var resource = effect.ToResource(CompositionContext.Default);
        using var node = CreateNode(resource);
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
        });
    }

    [Test]
    public void Measure_ShouldApplyFilterEffectBounds()
    {
        var effect = new Blur() { Sigma = { CurrentValue = new(10, 10) } };
        var resource = effect.ToResource(CompositionContext.Default);
        using var node = CreateNode(resource);
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.HasFragments, Is.True);
        Assert.That(measurement.OutputBounds.X, Is.LessThan(0));
        Assert.That(measurement.OutputBounds.Y, Is.LessThan(0));
        Assert.That(measurement.OutputBounds.Width, Is.GreaterThan(100));
        Assert.That(measurement.OutputBounds.Height, Is.GreaterThan(100));
    }

    [Test]
    public void CurrentPixelEffects_ExecuteAsOneFusedShaderRun()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using Bitmap disabled = RenderCurrentPixelEffects(
            FusionMode.Disabled,
            diagnostics: null,
            out _);
        using Bitmap enabled = RenderCurrentPixelEffects(
            FusionMode.Enabled,
            diagnostics,
            out RenderExecutionStatistics statistics);

        Assert.Multiple(() =>
        {
            Assert.That(enabled.GetPixelSpan().SequenceEqual(disabled.GetPixelSpan()), Is.True);
            Assert.That(enabled.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
            Assert.That(statistics.ShaderRunExecutions, Is.EqualTo(1));
            Assert.That(statistics.ShaderStageExecutions, Is.EqualTo(2));
            Assert.That(statistics.FusedShaderRunExecutions, Is.EqualTo(1));
            Assert.That(diagnostics.Latest.HasOpaqueExternalWork, Is.False);
        });
    }

    [Test]
    public void Update_ShouldReturnFalseForSameFilterEffect()
    {
        var effect = new Blur();
        var resource = effect.ToResource(CompositionContext.Default);
        var node = new FilterEffectRenderNode(resource);

        var result = node.Update(resource);

        Assert.That(result, Is.False);
    }

    // Updating an effect property changes its captured resource version.
    [Test]
    public void Update_ShouldReturnTrueForDifferentFilterEffectProperty()
    {
        var effect = new Blur();
        var resource = effect.ToResource(CompositionContext.Default);
        var node = new FilterEffectRenderNode(resource);
        effect.Sigma.CurrentValue = new(10, 10);
        var updateOnly = false;
        resource.Update(effect, CompositionContext.Default, ref updateOnly);

        var result = node.Update(resource);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Update_ShouldReturnTrueForDifferentFilterEffect()
    {
        var effect1 = new Blur();
        var effect2 = new DropShadow();
        var effectResource1 = effect1.ToResource(CompositionContext.Default);
        var effectResource2 = effect2.ToResource(CompositionContext.Default);
        var node = new FilterEffectRenderNode(effectResource1);

        var result = node.Update(effectResource2);

        Assert.That(result, Is.True);
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(node, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });

    private static Bitmap RenderCurrentPixelEffects(
        FusionMode fusionMode,
        RenderPipelineDiagnosticsState? diagnostics,
        out RenderExecutionStatistics statistics)
    {
        var group = new FilterEffectGroup
        {
            Children =
            {
                new TestCurrentPixelEffect(
                    "half4 apply(half4 color) { return half4(color.rgb * 0.75, color.a); }"),
                new TestCurrentPixelEffect(
                    "half4 apply(half4 color) { return half4(color.bgr, color.a); }"),
            },
        };
        using var node = CreateNode(group.ToResource(CompositionContext.Default));
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    FusionMode = fusionMode,
                    Purpose = RenderRequestPurpose.Frame,
                    Diagnostics = diagnostics,
                },
                TargetFactory = new CpuTargetFactory(),
            });
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        statistics = renderer.LastExecutionStatistics;
        return rasterization.Bitmap?.Clone()
               ?? throw new InvalidOperationException("The filter-effect render produced no bitmap.");
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
                ?? throw new InvalidOperationException("Could not create the CPU filter-effect test surface.");
            return new CpuRenderTarget(surface, deviceSize);
        }
    }

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);

    [SuppressResourceClassGeneration]
    private sealed partial class TestCurrentPixelEffect(string source) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.Shader(ShaderDescription.CurrentPixel(source));
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource;
    }
}
