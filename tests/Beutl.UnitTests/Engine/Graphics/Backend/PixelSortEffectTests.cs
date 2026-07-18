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
    public void Describe_HorizontalLuminance_ProducesSortedPixelOrder()
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
                using Bitmap bmp = Rasterize(outputs[0]);
                byte[] values = Enumerable.Range(0, bmp.Width)
                    .Select(x => bmp.SKBitmap.GetPixel(x, bmp.Height / 2).Red)
                    .ToArray();

                Assert.Multiple(() =>
                {
                    Assert.That(values, Is.Ordered.Ascending,
                        "PixelSort must reorder the deliberately shuffled luminance row into ascending order");
                    Assert.That(values.Distinct().Count(), Is.GreaterThan(8),
                        "the semantic gate needs enough distinct samples to detect an identity output");
                });
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

    private static RenderNodeOperation[] Execute(PixelSortEffect effect, Rect bounds)
    {
        var resource = (FilterEffect.Resource)(object)effect.ToResource(new CompositionContext(TimeSpan.Zero));
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            bounds,
            canvas => DrawShuffledLuminanceColumns(canvas, bounds),
            hitTest: bounds.Contains);

        var builder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        effect.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);
        return PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
    }

    private static void DrawShuffledLuminanceColumns(ImmediateCanvas canvas, Rect bounds)
    {
        // Deliberately non-monotonic: the old bounds-only gate passed even when PixelSort returned identity.
        ReadOnlySpan<byte> luminance =
            [224, 32, 192, 64, 160, 96, 128, 16, 240, 48, 208, 80, 176, 112, 144, 8];
        int width = (int)bounds.Width;
        for (int x = 0; x < width; x++)
        {
            byte v = luminance[x % luminance.Length];
            var brush = new SolidColorBrush(new Color(255, v, v, v));
            var brushResource = (SolidColorBrush.Resource)(object)brush.ToResource(new CompositionContext(TimeSpan.Zero));
            canvas.DrawRectangle(new Rect(x, 0, 1, bounds.Height), brushResource, pen: null);
        }
    }
}
