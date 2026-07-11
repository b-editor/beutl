using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// <see cref="PixelSortEffect"/> は内部的に GLSL シェーダー 3 パスを使う Vulkan 専用パス。
/// 宣言的パイプライン (Describe → compile → execute) 経由で実行できることを確認する。
/// </summary>
[NonParallelizable]
public class PixelSortEffectTests
{
    [Test]
    public void Describe_HorizontalLuminance_ProducesSortedOutput()
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

            RenderNodeOperation[] outputs = Execute(effect, bounds);
            try
            {
                Assert.That(outputs, Has.Length.EqualTo(1));
                Assert.That(outputs[0].Bounds, Is.EqualTo(bounds));
            }
            finally
            {
                RenderNodeOperation.DisposeAll(outputs);
            }
        });
    }

    [Test]
    public void Describe_VerticalSaturation_DoesNotThrow()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, 8, 8);
            var effect = new PixelSortEffect();
            effect.Direction.CurrentValue = PixelSortDirection.Vertical;
            effect.SortKey.CurrentValue = PixelSortKey.Saturation;
            effect.Ascending.CurrentValue = false;

            RenderNodeOperation[] outputs = null!;
            Assert.DoesNotThrow(() => outputs = Execute(effect, bounds));
            RenderNodeOperation.DisposeAll(outputs);
        });
    }

    // When the GLSL shaders fail to compile on an otherwise-live Vulkan context, the compute pass must copy the
    // source through (identity) instead of returning early and leaving the cleared, transparent destination — that
    // blanked the layer. The shader-init failure is forced through a test seam; the output must keep visible content.
    [Test]
    public void Dispatch_ShaderInitFailure_CopiesSourceThroughInsteadOfBlanking()
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

            PixelSortEffect.ForceShaderInitFailureForTests();
            RenderNodeOperation[] outputs = null!;
            try
            {
                outputs = Execute(effect, bounds);
                Assert.That(outputs, Has.Length.EqualTo(1));
                using Bitmap bmp = Rasterize(outputs[0]);
                Assert.That(HasVisibleContent(bmp), Is.True,
                    "a shader-init failure must copy the source through, not blank the layer to transparent");
            }
            finally
            {
                RenderNodeOperation.DisposeAll(outputs);
                PixelSortEffect.ResetShaderInitForTests();
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
        using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: op.Bounds.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)))
            {
                op.Render(canvas);
            }
        }

        return target.Snapshot();
    }

    private static RenderNodeOperation[] Execute(PixelSortEffect effect, Rect bounds)
    {
        var resource = (FilterEffect.Resource)(object)effect.ToResource(new CompositionContext(TimeSpan.Zero));
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            bounds,
            canvas => DrawGradient(canvas, bounds),
            hitTest: bounds.Contains);

        var builder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: 1f);
        effect.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);
        return PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null);
    }

    private static void DrawGradient(ImmediateCanvas canvas, Rect bounds)
    {
        // 水平方向に明度勾配
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
