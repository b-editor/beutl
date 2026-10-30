using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// <see cref="PixelSortEffect"/> は内部的に GLSL シェーダー 3 パスを使う Vulkan 専用パス。
/// FilterEffectExecutor 経由で実行できることを確認する。
/// </summary>
[NonParallelizable]
public class PixelSortEffectTests
{
    [Test]
    public void ApplyTo_HorizontalLuminance_ReplacesTargetWithSortedOutput()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var sourceRenderTarget = CreateGradientTarget(16, 16);
            Assume.That(sourceRenderTarget, Is.Not.Null);
            using Bitmap source = sourceRenderTarget!.Snapshot();

            var bounds = new Rect(0, 0, 16, 16);
            using var targets = new EffectTargets
            {
                new EffectTarget(sourceRenderTarget!, bounds),
            };

            using var feCtx = new FilterEffectContext(bounds);
            var effect = new PixelSortEffect();
            effect.Direction.Value = PixelSortDirection.Horizontal;
            effect.SortKey.Value = PixelSortKey.Luminance;
            effect.ThresholdMin.Value = 0f;
            effect.ThresholdMax.Value = 100f;
            // Descending, so that sorting the left-to-right gradient changes it.
            effect.Ascending.Value = false;

            var resource = (FilterEffect.Resource)(object)effect.ToResource(new CompositionContext(MediaTime.Zero));
            feCtx.ApplyTransactional(resource);

            using var builder = new SKImageFilterBuilder();
            using var executor = new FilterEffectExecutor(
                targets,
                builder,
                RenderIntent.Delivery,
                RenderRequestPurpose.Auxiliary,
                drawableBrushMaterializer: null);
            executor.Apply(feCtx);
            executor.Flush(false);

            // The sorted output replaces the target and differs from the source.
            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].RenderTarget, Is.Not.Null);
            Assert.That(targets[0].RenderTarget!.Width, Is.EqualTo(16));
            Assert.That(targets[0].RenderTarget!.Height, Is.EqualTo(16));
            using Bitmap sorted = targets[0].RenderTarget!.Snapshot();
            Assert.That(sorted.GetPixelSpan().SequenceEqual(source.GetPixelSpan()), Is.False);
        });
    }

    [Test]
    public void ApplyTo_RecordsThreeShaderPasses()
    {
        var effect = new PixelSortEffect();
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, 16, 16));
        context.ApplyTransactional(resource);
        var item = (FEItem_ShaderGraph)context.GetOrderedItems().Single();
        Assert.That(item.Description.PassNames, Is.EqualTo(new[] { "prepare", "rank", "gather" }));
    }

    [Test]
    public void FailDeliveryOrDrop_fails_delivery_and_degrades_preview()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(
                () => EffectTargetAllocation.FailDeliveryOrDrop(RenderIntent.Delivery, null, "No GPU texture."));
            Assert.DoesNotThrow(
                () => EffectTargetAllocation.FailDeliveryOrDrop(RenderIntent.Preview, null, "No GPU texture."));
        });
    }

    private static RenderTarget? CreateGradientTarget(int width, int height)
    {
        var target = RenderTarget.Create(width, height);
        if (target == null) return null;

        using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview))
        {
            canvas.Clear(Colors.Black);
            // 水平方向に明度勾配
            for (int x = 0; x < width; x++)
            {
                byte v = (byte)(255 * x / Math.Max(1, width - 1));
                var brush = new SolidColorBrush(new Color(255, v, v, v));
                var brushResource = (SolidColorBrush.Resource)(object)brush.ToResource(new CompositionContext(MediaTime.Zero));
                canvas.DrawRectangle(new Rect(x, 0, 1, height), brushResource, pen: null);
            }
        }

        return target;
    }
}
