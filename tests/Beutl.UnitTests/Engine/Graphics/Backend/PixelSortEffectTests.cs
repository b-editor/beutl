using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// <see cref="PixelSortEffect"/> は内部的に GLSL シェーダー 3 パスを使う Vulkan 専用パス。
/// FilterEffectActivator 経由で実行できることを確認する。
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

            var bounds = new Rect(0, 0, 16, 16);
            using var targets = new EffectTargets { new EffectTarget(sourceRenderTarget!, bounds) };

            using var feCtx = new FilterEffectContext(bounds);
            var effect = new PixelSortEffect();
            effect.Direction.CurrentValue = PixelSortDirection.Horizontal;
            effect.SortKey.CurrentValue = PixelSortKey.Luminance;
            effect.ThresholdMin.CurrentValue = 0f;
            effect.ThresholdMax.CurrentValue = 100f;
            effect.Ascending.CurrentValue = true;

            var resource = (FilterEffect.Resource)
                (object)effect.ToResource(new CompositionContext(TimeSpan.Zero));
            effect.ApplyTo(feCtx, resource);

            using var builder = new SKImageFilterBuilder();
            using var activator = new FilterEffectActivator(targets, builder);
            activator.Apply(feCtx);
            activator.Flush(false);

            // Apply 後はソート結果のターゲットに置換されている
            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].RenderTarget, Is.Not.Null);
            Assert.That(targets[0].RenderTarget!.Width, Is.EqualTo(16));
            Assert.That(targets[0].RenderTarget!.Height, Is.EqualTo(16));
        });
    }

    [Test]
    public void ApplyTo_VerticalSaturation_DoesNotThrow()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var sourceRenderTarget = CreateGradientTarget(8, 8);
            Assume.That(sourceRenderTarget, Is.Not.Null);

            var bounds = new Rect(0, 0, 8, 8);
            using var targets = new EffectTargets { new EffectTarget(sourceRenderTarget!, bounds) };

            using var feCtx = new FilterEffectContext(bounds);
            var effect = new PixelSortEffect();
            effect.Direction.CurrentValue = PixelSortDirection.Vertical;
            effect.SortKey.CurrentValue = PixelSortKey.Saturation;
            effect.Ascending.CurrentValue = false;

            var resource = (FilterEffect.Resource)
                (object)effect.ToResource(new CompositionContext(TimeSpan.Zero));
            effect.ApplyTo(feCtx, resource);

            using var builder = new SKImageFilterBuilder();
            using var activator = new FilterEffectActivator(targets, builder);
            Assert.DoesNotThrow(() => activator.Apply(feCtx));
            Assert.DoesNotThrow(() => activator.Flush(false));
        });
    }

    private static RenderTarget? CreateGradientTarget(int width, int height)
    {
        var target = RenderTarget.Create(width, height);
        if (target == null)
            return null;

        using (var canvas = new ImmediateCanvas(target))
        {
            canvas.Clear(Colors.Black);
            // 水平方向に明度勾配
            for (int x = 0; x < width; x++)
            {
                byte v = (byte)(255 * x / Math.Max(1, width - 1));
                var brush = new SolidColorBrush(new Color(255, v, v, v));
                var brushResource = (SolidColorBrush.Resource)
                    (object)brush.ToResource(new CompositionContext(TimeSpan.Zero));
                canvas.DrawRectangle(new Rect(x, 0, 1, height), brushResource, pen: null);
            }
        }

        return target;
    }
}
