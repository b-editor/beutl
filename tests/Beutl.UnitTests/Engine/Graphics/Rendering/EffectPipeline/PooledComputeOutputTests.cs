using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// A pooled compute output must survive recorded Skia work carried by its previous lease. The compute transition
/// flushes that work before raw Vulkan writes the same image; otherwise a downstream Skia flush could replay it over
/// the compute result. Pool-hit frames are exactly the steady state SC-003 pins, and frame 1 is always an all-miss
/// frame, so the regression frame is frame 2 — a single-frame render cannot catch this.
/// </summary>
[NonParallelizable]
[TestFixture]
public class PooledComputeOutputTests
{
    [Test]
    public void Execute_SecondFramePoolHit_KeepsComputeOutputVisible()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, 16, 16);
            var effect = new PixelSortEffect();
            effect.Direction.CurrentValue = PixelSortDirection.Horizontal;
            effect.SortKey.CurrentValue = PixelSortKey.Luminance;
            effect.ThresholdMin.CurrentValue = 0f;
            effect.ThresholdMax.CurrentValue = 100f;
            effect.Ascending.CurrentValue = true;

            using var pool = new RenderTargetPool();
            for (int frame = 1; frame <= 2; frame++)
            {
                RenderNodeOperation[] outputs = Execute(effect, bounds, pool);
                try
                {
                    Assert.That(outputs, Has.Length.EqualTo(1), $"frame {frame}");
                    using Bitmap bmp = Rasterize(outputs[0]);
                    Assert.That(HasVisibleContent(bmp), Is.True,
                        $"frame {frame}: deferred Skia work overwrote the pooled compute output");
                }
                finally
                {
                    RenderNodeOperation.DisposeAll(outputs);
                }
            }
        });
    }

    private static bool HasVisibleContent(Bitmap bmp)
    {
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                if (bmp.SKBitmap.GetPixel(x, y).Alpha != 0)
                    return true;
            }
        }

        return false;
    }

    private static Bitmap Rasterize(RenderNodeOperation op)
    {
        var size = PixelRect.FromRect(op.Bounds);
        using RenderTarget target = RenderTarget.Create(Math.Max(1, size.Width), Math.Max(1, size.Height))
            ?? throw new InvalidOperationException("RenderTarget.Create returned null (raster surface unavailable).");
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: op.Bounds.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)))
            {
                op.Render(canvas);
            }
        }

        return target.Snapshot();
    }

    private static RenderNodeOperation[] Execute(PixelSortEffect effect, Rect bounds, RenderTargetPool pool)
    {
        var resource = (FilterEffect.Resource)(object)effect.ToResource(new CompositionContext(TimeSpan.Zero));
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            bounds,
            canvas => DrawGradient(canvas, bounds),
            hitTest: bounds.Contains);

        var builder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        effect.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);
        return PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery);
    }

    private static void DrawGradient(ImmediateCanvas canvas, Rect bounds)
    {
        int width = (int)bounds.Width;
        for (int x = 0; x < width; x++)
        {
            byte v = (byte)(255 * x / Math.Max(1, width - 1));
            var brush = new SolidColorBrush(new Color(255, v, v, v));
            var brushResource = (SolidColorBrush.Resource)(object)brush.ToResource(new CompositionContext(TimeSpan.Zero));
            canvas.DrawRectangle(new Rect(x, 0, 1, bounds.Height), brushResource, pen: null);
        }
    }
}
