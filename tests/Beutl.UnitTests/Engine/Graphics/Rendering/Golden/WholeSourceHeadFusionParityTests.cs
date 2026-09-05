using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class WholeSourceHeadFusionParityTests
{
    private static readonly Rect s_bounds = new(0, 0, 13, 9);

    [Test]
    public void ScriptOutputSizeUniforms_MatchAcrossDirectAndMaterializedExecution()
    {
        var expectedColorByMode = new Dictionary<FusionMode, SKColor>();

        GpuPassFusionParityResult parity = GpuPassFusionSameProcessParityHarness.AssertParity(mode =>
        {
            Bitmap bitmap = RenderOutputSizeScript(mode, out RenderExecutionStatistics statistics);
            SKColor color = bitmap.SKBitmap.GetPixel(8, 6);
            expectedColorByMode.Add(mode, color);
            if (mode == FusionMode.Enabled)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(statistics.ShaderRunExecutions, Is.EqualTo(1));
                    Assert.That(statistics.ShaderStageExecutions, Is.EqualTo(2));
                    Assert.That(statistics.FusedShaderRunExecutions, Is.EqualTo(1));
                });
            }
            return bitmap;
        });

        TestContext.WriteLine(
            $"Output-size uniform parity: SSIM={parity.FullImage.Ssim:R}, "
            + $"windowed={parity.FullImage.WindowedSsim:R}, "
            + $"RGB MAE={parity.FullImage.LinearRgbMae:R}, alpha MAE={parity.FullImage.AlphaMae:R}");
        Assert.Multiple(() =>
        {
            foreach ((FusionMode mode, SKColor color) in expectedColorByMode)
            {
                Assert.That(color.Green, Is.GreaterThanOrEqualTo(250),
                    $"{mode} must expose the 16x12 semantic output size");
                Assert.That(color.Red, Is.LessThanOrEqualTo(5),
                    $"{mode} must not expose the physical execution backing");
                Assert.That(color.Blue, Is.LessThanOrEqualTo(5),
                    $"{mode} must not expose the physical execution backing");
            }
        });
    }

    [Test]
    public void MosaicClampEdge_MatchesStandaloneWholeSourcePass()
    {
        RenderExecutionStatistics disabledStatistics = default;
        RenderExecutionStatistics enabledStatistics = default;
        ushort disabledEdgeAlpha = 0;
        ushort enabledEdgeAlpha = 0;

        GpuPassFusionParityResult parity = GpuPassFusionSameProcessParityHarness.AssertParity(mode =>
        {
            Bitmap bitmap = Render(mode, out RenderExecutionStatistics statistics);
            ushort alpha = bitmap.GetRow<ushort>(bitmap.Height / 2)[((bitmap.Width - 1) * 4) + 3];
            if (mode == FusionMode.Disabled)
            {
                disabledStatistics = statistics;
                disabledEdgeAlpha = alpha;
            }
            else
            {
                enabledStatistics = statistics;
                enabledEdgeAlpha = alpha;
            }
            return bitmap;
        });

        TestContext.WriteLine(
            $"Mosaic Clamp parity: SSIM={parity.FullImage.Ssim:R}, "
            + $"windowed={parity.FullImage.WindowedSsim:R}, "
            + $"RGB MAE={parity.FullImage.LinearRgbMae:R}, alpha MAE={parity.FullImage.AlphaMae:R}");
        Assert.Multiple(() =>
        {
            Assert.That(disabledEdgeAlpha, Is.Not.Zero,
                "the standalone Clamp path must extend the semantic edge into the partial Mosaic tile");
            Assert.That(enabledEdgeAlpha, Is.EqualTo(disabledEdgeAlpha),
                "the WholeSource-headed run must bind the same Clamp semantic source");
            Assert.That(enabledStatistics.ShaderRunExecutions, Is.EqualTo(1));
            Assert.That(enabledStatistics.ShaderStageExecutions, Is.EqualTo(2));
            Assert.That(enabledStatistics.FusedShaderRunExecutions, Is.EqualTo(1));
            Assert.That(disabledStatistics.ShaderRunExecutions, Is.EqualTo(1));
            Assert.That(disabledStatistics.ShaderStageExecutions, Is.EqualTo(1));
            Assert.That(disabledStatistics.FusedShaderRunExecutions, Is.Zero);
        });
    }

    private static Bitmap Render(
        FusionMode fusionMode,
        out RenderExecutionStatistics statistics)
    {
        var mosaic = new MosaicEffect();
        mosaic.TileSize.CurrentValue = new Size(10, 10);
        mosaic.Origin.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Absolute);
        var effects = new FilterEffectGroup
        {
            Children =
            {
                mosaic,
                new Gamma { Amount = { CurrentValue = 180f } },
            },
        };

        using FilterEffect.Resource resource = effects.ToResource(CompositionContext.Default);
        using var root = new FilterEffectRenderNode(resource);
        root.AddChild(new RectangleRenderNode(s_bounds, Brushes.Resource.White, null));
        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_bounds,
            CacheOptions = RenderCacheOptions.Disabled,
            FusionMode = fusionMode,
        }, new CpuTargetFactory());

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        statistics = renderer.LastExecutionStatistics;
        return rasterization.Bitmap?.Clone()
               ?? throw new InvalidOperationException("The Mosaic parity render produced no bitmap.");
    }

    private static Bitmap RenderOutputSizeScript(
        FusionMode fusionMode,
        out RenderExecutionStatistics statistics)
    {
        var script = new SKSLScriptEffect
        {
            Script =
            {
                CurrentValue =
                    """
                    uniform shader src;
                    uniform float width;
                    uniform float height;
                    uniform float2 iResolution;

                    half4 main(float2 coord) {
                        bool correct = width == 16.0 && height == 12.0
                            && iResolution.x == 16.0 && iResolution.y == 12.0;
                        return correct
                            ? half4(0.0, 1.0, 0.0, 1.0)
                            : half4(1.0, 0.0, 1.0, 1.0);
                    }
                    """,
            },
        };
        var effects = new FilterEffectGroup
        {
            Children =
            {
                script,
                new Gamma { Amount = { CurrentValue = 100f } },
            },
        };
        var contentBounds = new Rect(0, 0, 16, 12);
        var canvasBounds = new Rect(0, 0, 100, 100);

        using FilterEffect.Resource resource = effects.ToResource(CompositionContext.Default);
        using var root = new FilterEffectRenderNode(resource);
        root.AddChild(new RectangleRenderNode(contentBounds, Brushes.Resource.White, null));
        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = canvasBounds,
            CacheOptions = RenderCacheOptions.Disabled,
            FusionMode = fusionMode,
        }, new CpuTargetFactory());
        var canvasSize = new PixelSize(100, 100);
        SKSurface surface = SKSurface.Create(new SKImageInfo(
                canvasSize.Width,
                canvasSize.Height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear()))
            ?? throw new InvalidOperationException("Could not create the CPU output-size parity surface.");
        using RenderTarget destination = new CpuRenderTarget(surface, canvasSize);
        using (var canvas = new ImmediateCanvas(destination, RenderIntent.Preview, logicalSize: canvasBounds.Size))
        {
            canvas.Clear();
            renderer.Render(canvas);
        }
        statistics = renderer.LastExecutionStatistics;
        return destination.Snapshot();
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize size = allocation.DeviceSize;
            SKSurface surface = SKSurface.Create(new SKImageInfo(
                    size.Width,
                    size.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()))
                ?? throw new InvalidOperationException("Could not create the CPU WholeSource fusion test surface.");
            return new CpuRenderTarget(surface, size);
        }
    }

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);
}
