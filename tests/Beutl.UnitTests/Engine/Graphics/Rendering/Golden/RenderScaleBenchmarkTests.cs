using System.Diagnostics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// SC-003: a reduced-scale render is materially faster than full scale. Timing-based, so [Explicit]
// to avoid CI flakiness, but it runs for real on MoltenVK.
[NonParallelizable]
[Category("Benchmark")]
[TestFixture]
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

    private static (double Full, double Half, int FasterPairs) MeasureRenderMedians(int iterations)
    {
        using var fullSession = new BenchmarkRenderSession(MakeWork(), Frame, 1f);
        using var halfSession = new BenchmarkRenderSession(MakeWork(), Frame, 0.5f);
        for (int i = 0; i < 3; i++)
        {
            using Bitmap fullWarmup = fullSession.Render();
            using Bitmap halfWarmup = halfSession.Render();
        }

        var fullSamples = new double[iterations];
        var halfSamples = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            if ((i & 1) == 0)
            {
                fullSamples[i] = MeasureRenderMs(fullSession);
                halfSamples[i] = MeasureRenderMs(halfSession);
            }
            else
            {
                halfSamples[i] = MeasureRenderMs(halfSession);
                fullSamples[i] = MeasureRenderMs(fullSession);
            }
        }

        int fasterPairs = fullSamples.Zip(halfSamples)
            .Count(static pair => pair.Second < pair.First);
        Array.Sort(fullSamples);
        Array.Sort(halfSamples);
        return (fullSamples[iterations / 2], halfSamples[iterations / 2], fasterPairs);
    }

    private static double MeasureRenderMs(BenchmarkRenderSession session)
    {
        var stopwatch = Stopwatch.StartNew();
        using Bitmap bitmap = session.Render();
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    [Test]
    [Explicit("timing-sensitive")]
    public void HalfScale_IsSignificantlyFaster()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            (double full, double half, int fasterPairs) = MeasureRenderMedians(11);
            double ratio = half / full;
            TestContext.WriteLine(
                $"render median: 1.0={full:F2}ms 0.5={half:F2}ms ratio={ratio:F3} faster-pairs={fasterPairs}/11");
            Assert.That(
                fasterPairs,
                Is.GreaterThanOrEqualTo(9),
                $"0.5x was faster in only {fasterPairs}/11 pairs; a one-sided exact sign test requires at least 9/11 for p < 0.05");
        });
    }

    private sealed class BenchmarkRenderSession : IDisposable
    {
        private readonly DrawableRenderNode _node;
        private readonly RenderNodeRenderer _renderer;
        private readonly Drawable.Resource _resource;
        private readonly PixelSize _deviceSize;
        private readonly Size _logicalSize;
        private readonly float _scale;

        public BenchmarkRenderSession(Drawable.Resource resource, PixelSize logicalSize, float scale)
        {
            _resource = resource;
            _logicalSize = logicalSize.ToSize(1);
            _deviceSize = new PixelSize(
                (int)MathF.Ceiling(logicalSize.Width * scale),
                (int)MathF.Ceiling(logicalSize.Height * scale));
            _scale = scale;
            _node = new DrawableRenderNode(resource);
            using (var context = new GraphicsContext2D(_node, _logicalSize, scale))
            {
                resource.GetOriginal().Render(context, resource);
            }

            _renderer = new RenderNodeRenderer(
                _node,
                new RenderNodeRendererOptions
                {
                    Intent = RenderIntent.Delivery,
                    TargetDomain = new Rect(default, _logicalSize),
                    OutputScale = scale,
                    UseRenderCache = false,
                });
        }

        public Bitmap Render()
        {
            using RenderTarget target = RenderTarget.Create(_deviceSize.Width, _deviceSize.Height)
                                        ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
            using var canvas = new ImmediateCanvas(target, _scale, logicalSize: _logicalSize);
            canvas.Clear(Colors.Black);
            _renderer.Render(canvas);
            return target.Snapshot();
        }

        public void Dispose()
        {
            _renderer.Dispose();
            _node.Dispose();
            _resource.Dispose();
        }
    }
}
