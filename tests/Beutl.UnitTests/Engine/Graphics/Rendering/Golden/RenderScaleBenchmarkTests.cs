using System.Diagnostics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// SC-003: a reduced-scale render is materially faster than full scale. Timing-based, so it is
// [Explicit] (run on demand) to avoid CI flakiness, but it runs for real on MoltenVK.
[NonParallelizable]
[Category("Benchmark")]
public class RenderScaleBenchmarkTests
{
    private static readonly PixelSize Frame = new(1280, 720);

    private static Drawable.Resource MakeWork()
    {
        var shape = new EllipseShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 900;
        shape.Height.CurrentValue = 600;
        shape.Fill.CurrentValue = Brushes.White;
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(20, 20);
        shape.FilterEffect.CurrentValue = blur;
        return shape.ToResource(CompositionContext.Default);
    }

    private static double MedianRenderMs(float scale, int iterations)
    {
        var samples = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            using Bitmap b = GoldenImageHarness.RenderAtScale(MakeWork(), Frame, scale);
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        return samples[iterations / 2];
    }

    [Test]
    [Explicit("timing-sensitive")]
    public void HalfScale_IsMateriallyFaster()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // Warm up the pipeline / shader cache.
            MedianRenderMs(1f, 3);

            double full = MedianRenderMs(1f, 11);
            double half = MedianRenderMs(0.5f, 11);
            double ratio = half / full;
            TestContext.WriteLine($"render median: 1.0={full:F2}ms 0.5={half:F2}ms ratio={ratio:F3}");
            Assert.That(ratio, Is.LessThan(0.6), $"0.5x/1.0x render-time ratio {ratio:F3} not < 0.6");
        });
    }
}
