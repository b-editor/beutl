using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class DirectBlurFiniteOutputTests
{
    private static readonly Rect s_frame = new(0, 0, 256, 144);
    private static readonly Rect s_sourceBounds = new(190, 120, 120, 90);

    [Test]
    [Category("GpuPassFusionGpu")]
    public void DirectDestinationReplay_BlurSamplesOnlyTheDeclaredSourceBounds()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffectRenderNode node = CreateBlurNode(workingScale: null);
            using RenderNodeRenderer renderer = CreateRenderer(node);
            using RenderNodeRasterization result = renderer.Rasterize();

            AssertFiniteVisibleResult(result, "direct destination replay");
            Assert.That(
                renderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.Zero,
                "The fixture must exercise Blur's direct destination replay path.");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void DirectMaterialization_BlurSamplesOnlyTheDeclaredSourceBounds()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffectRenderNode node = CreateBlurNode(workingScale: 2f);
            using RenderNodeRenderer renderer = CreateRenderer(node);
            using RenderNodeRasterization result = renderer.Rasterize();

            AssertFiniteVisibleResult(result, "direct materialization");
            Assert.That(
                renderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.GreaterThan(0),
                "A working scale different from the destination must exercise Blur's materialization path.");
        });
    }

    [Test]
    public void NonMaterializedFilterLayer_UsesTheRegionReplayedForEachGroupChild()
    {
        Matrix deviceTransform = Matrix.CreateScale(2f, 2f);
        Thickness apron = new(0.5f);
        Rect hairlineBounds = new(20, 70, 216, 1);
        Rect replayedHairlineBounds = new(96, 70, 48, 1);
        Rect offFrameBounds = new(-120, 32, 180, 96);
        Rect replayedOffFrameBounds = new(-18, 48, 78, 64);

        Assert.Multiple(() =>
        {
            Assert.That(
                OpenedLayerBounds(hairlineBounds, replayedHairlineBounds, deviceTransform),
                Is.EqualTo(replayedHairlineBounds.Inflate(apron)),
                "The hairline layer must be the replayed region plus the raster apron, never widened to "
                + "the semantic area that replay did not write.");
            Assert.That(
                OpenedLayerBounds(offFrameBounds, replayedOffFrameBounds, deviceTransform),
                Is.EqualTo(replayedOffFrameBounds.Inflate(apron)),
                "The off-frame layer must be the replayed region plus the raster apron, never widened to "
                + "the semantic area that replay did not write.");
        });
    }

    /// <summary>Mirrors what <c>ImmediateCanvas.PushFilterLayer</c> opens for a replayed region.</summary>
    private static Rect OpenedLayerBounds(Rect semanticBounds, Rect replayedBounds, Matrix deviceTransform)
        => ImmediateCanvas.InflateByOneDevicePixel(
            RenderRequestExecutor.GetDirectFilterLayerBounds(semanticBounds, replayedBounds),
            deviceTransform);

    private static FilterEffectRenderNode CreateBlurNode(float? workingScale)
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(40, 40);
        FilterEffect.Resource resource = blur.ToResource(CompositionContext.Default);
        FilterEffectRenderNode node = workingScale is { } scale
            ? new FixedWorkingScaleFilterRenderNode(resource, scale)
            : new FilterEffectRenderNode(resource);
        node.AddChild(new PoisonedOutsideBoundsSourceNode());
        return node;
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Delivery,
                    Purpose = RenderRequestPurpose.Frame,
                    TargetDomain = s_frame,
                    RequestedRegion = s_frame,
                    OutputScale = 1f,
                    MaxWorkingScale = 2f,
                    CacheOptions = RenderCacheOptions.Disabled,
                },
            });

    private static void AssertFiniteVisibleResult(RenderNodeRasterization result, string label)
    {
        Assert.That(result.Bitmap, Is.Not.Null);
        Bitmap bitmap = result.Bitmap!;
        Assert.Multiple(() =>
        {
            Assert.That(
                ImageMetrics.FirstNonFinite((label, bitmap)),
                Is.Null,
                "Blur must not sample pixels outside the source's declared content bounds.");
            Assert.That(
                HasVisibleCoverage(bitmap),
                Is.True,
                "The offscreen source must still blur into the frame.");
        });
    }

    private static bool HasVisibleCoverage(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                float alpha = (float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]);
                if (float.IsFinite(alpha) && alpha > 0.01f)
                    return true;
            }
        }

        return false;
    }

    private sealed class PoisonedOutsideBoundsSourceNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.PaintedSource(
                s_sourceBounds,
                static (canvas, _, _, bounds) =>
                {
                    using SKSLShader poison = SKSLShader.Create(
                        "uniform float zero; half4 main(float2 p) { float n = zero / zero; return half4(n); }");
                    using SKSLShaderBuilder builder = poison.CreateBuilder();
                    builder.Uniforms["zero"] = 0f;
                    using SKShader poisonShader = builder.Build();
                    using var poisonPaint = new SKPaint
                    {
                        Shader = poisonShader,
                        BlendMode = SKBlendMode.Src,
                    };
                    canvas.Canvas.DrawPaint(poisonPaint);

                    using var paint = new SKPaint
                    {
                        ColorF = new SKColorF(1f, 0f, 0f, 1f),
                        BlendMode = SKBlendMode.Src,
                        IsAntialias = false,
                    };
                    canvas.Canvas.DrawRect(bounds.ToSKRect(), paint);
                },
                fill: null,
                pen: null,
                outputBounds: s_sourceBounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector));
        }
    }

    private sealed class FixedWorkingScaleFilterRenderNode(
        FilterEffect.Resource effect,
        float workingScale) : FilterEffectRenderNode(effect)
    {
        private readonly RenderScaleContract _scale = RenderScaleContract.Custom(_ => workingScale);

        protected override RenderScaleContract? GetWorkingScaleContract() => _scale;
    }
}
