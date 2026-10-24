using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
[NonParallelizable]
public sealed class PixelSortSpecializationTests
{
    private static readonly Rect s_bounds = new(0, 0, 4, 4);

    private static IEnumerable<TestCaseData> SpecializationCases
    {
        get
        {
            foreach (PixelSortKey sortKey in Enum.GetValues<PixelSortKey>())
            {
                foreach (PixelSortDirection direction in Enum.GetValues<PixelSortDirection>())
                {
                    yield return new TestCaseData(sortKey, direction, true)
                        .SetName($"PixelSort_SpecializedMatchesCpuOrder_{sortKey}_{direction}_Ascending");
                    yield return new TestCaseData(sortKey, direction, false)
                        .SetName($"PixelSort_SpecializedMatchesCpuOrder_{sortKey}_{direction}_Descending");
                }
            }
        }
    }

    [TestCaseSource(nameof(SpecializationCases))]
    public void SpecializedVariants_MatchCpuOrder(
        PixelSortKey sortKey,
        PixelSortDirection direction,
        bool ascending)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create((int)s_bounds.Width, (int)s_bounds.Height)
                ?? throw new InvalidOperationException("Could not create the pixel-sort specialization source.");
            DrawDistinctPixels(source);

            using RenderTarget specialized = ApplySpecializedPixelSort(source, sortKey, direction, ascending);
            byte[] specializedPixels = specialized.Texture!.DownloadPixels();
            byte[] expectedPixels = SortOnCpu(
                source,
                sortKey,
                direction,
                ascending);

            Assert.That(specializedPixels, Is.EqualTo(expectedPixels));
        });
    }

    [Test]
    public void DifferentSortKeySpecializations_ProduceDifferentRenders()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create((int)s_bounds.Width, (int)s_bounds.Height)
                ?? throw new InvalidOperationException("Could not create the pixel-sort specialization source.");
            DrawDistinctPixels(source);

            using RenderTarget red = ApplySpecializedPixelSort(
                source,
                PixelSortKey.Red,
                PixelSortDirection.Horizontal,
                ascending: true);
            using RenderTarget blue = ApplySpecializedPixelSort(
                source,
                PixelSortKey.Blue,
                PixelSortDirection.Horizontal,
                ascending: true);
            byte[] redPixels = red.Texture!.DownloadPixels();
            byte[] bluePixels = blue.Texture!.DownloadPixels();

            Assert.That(
                bluePixels,
                Is.Not.EqualTo(redPixels),
                "Distinct specialization values must change the executed PixelSort pipeline, not only its cache identity.");
        });
    }

    private static byte[] SortOnCpu(
        RenderTarget source,
        PixelSortKey sortKey,
        PixelSortDirection direction,
        bool ascending)
    {
        Half[] pixels = MemoryMarshal.Cast<byte, Half>(source.Texture!.DownloadPixels()).ToArray();
        Half[] sorted = (Half[])pixels.Clone();
        for (int line = 0; line < 4; line++)
        {
            int PixelIndex(int position) => direction == PixelSortDirection.Horizontal
                ? line * 4 + position
                : position * 4 + line;
            int[] order = Enumerable.Range(0, 4)
                .Select(PixelIndex)
                .OrderBy(index => SortValue(pixels.AsSpan(index * 4, 3), sortKey))
                .ToArray();
            if (!ascending) Array.Reverse(order);
            for (int position = 0; position < 4; position++)
                Array.Copy(pixels, order[position] * 4, sorted, PixelIndex(position) * 4, 3);
        }

        return MemoryMarshal.AsBytes(sorted.AsSpan()).ToArray();
    }

    private static float SortValue(ReadOnlySpan<Half> pixel, PixelSortKey key)
    {
        float r = (float)pixel[0], g = (float)pixel[1], b = (float)pixel[2];
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;
        float hue = delta == 0 ? 0 : max == r ? ((g - b) / delta + 6) % 6
            : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        float value = key switch
        {
            PixelSortKey.Luminance => 0.2126f * r + 0.7152f * g + 0.0722f * b,
            PixelSortKey.Hue => hue / 6,
            PixelSortKey.Saturation => max == 0 ? 0 : delta / max,
            PixelSortKey.Red => r,
            PixelSortKey.Green => g,
            PixelSortKey.Blue => b,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        // Rank compares the encoded key stored in an RGBA16Float texture.
        return (float)(Half)Math.Max(1f / 255, value * 0.998f + 0.001f);
    }

    private static RenderTarget ApplySpecializedPixelSort(
        RenderTarget source,
        PixelSortKey sortKey,
        PixelSortDirection direction,
        bool ascending)
    {
        var effect = new PixelSortEffect();
        effect.Direction.Value = direction;
        effect.SortKey.Value = sortKey;
        effect.ThresholdMin.Value = 0;
        effect.ThresholdMax.Value = 100;
        effect.Ascending.Value = ascending;

        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(effect, resource);
        using var targets = new EffectTargets
        {
            new EffectTarget(source, s_bounds, EffectiveScale.At(1)),
        };
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            deviceGridOffset: default,
            useExecutorManagedCanvas: true);

        executor.Apply(context);

        RenderTarget applied = executor.CurrentTargets.Single().RenderTarget
            ?? throw new InvalidOperationException("The specialized pixel-sort effect produced no target.");
        return applied.ShallowCopy();
    }

    private static void DrawDistinctPixels(RenderTarget target)
    {
        SKColor[] colors =
        [
            new(12, 201, 73), new(231, 42, 118), new(64, 91, 223), new(174, 219, 31),
            new(93, 17, 186), new(246, 133, 52), new(28, 168, 211), new(157, 76, 9),
            new(204, 187, 99), new(49, 234, 142), new(119, 58, 247), new(222, 105, 164),
            new(71, 149, 38), new(188, 26, 214), new(137, 196, 181), new(35, 113, 127),
        ];
        target.BeginDraw();
        SKCanvas canvas = target.Value.Canvas;
        using var paint = new SKPaint { IsAntialias = false };
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                paint.Color = colors[(y * 4) + x];
                canvas.DrawRect(SKRect.Create(x, y, 1, 1), paint);
            }
        }

        // Distinct saturation keys in this column collide when encoded into the half-float scratch texture.
        target.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);
        Half[] pixels = MemoryMarshal.Cast<byte, Half>(target.Texture!.DownloadPixels()).ToArray();
        pixels[5 * 4] = pixels[9 * 4] = (Half)1;
        pixels[5 * 4 + 1] = pixels[9 * 4 + 1] = (Half)0.5f;
        pixels[5 * 4 + 2] = (Half)0.03724f;
        pixels[9 * 4 + 2] = (Half)0.03732f;
        target.Texture.Upload(MemoryMarshal.AsBytes(pixels.AsSpan()));
    }
}
