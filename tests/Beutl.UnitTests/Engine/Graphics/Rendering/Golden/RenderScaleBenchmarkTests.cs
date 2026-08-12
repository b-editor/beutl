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
    private const int ScheduleSeed = 20040719;
    private const double RasterizationBoundTarget = 0.25;
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

    private static BenchmarkMeasurement MeasureRenderMedians(int seed)
    {
        using var fullSession = new BenchmarkRenderSession(MakeWork(), Frame, 1f);
        using var halfSession = new BenchmarkRenderSession(MakeWork(), Frame, 0.5f);
        for (int i = 0; i < 3; i++)
        {
            using Bitmap fullWarmup = fullSession.Render();
            using Bitmap halfWarmup = halfSession.Render();
        }

        RenderPairOrder[] schedule = CreateSchedule(seed);
        var fullSamples = new double[schedule.Length];
        var halfSamples = new double[schedule.Length];
        for (int i = 0; i < schedule.Length; i++)
        {
            if (schedule[i] == RenderPairOrder.HalfThenFull)
            {
                halfSamples[i] = MeasureRenderMs(halfSession);
                fullSamples[i] = MeasureRenderMs(fullSession);
            }
            else
            {
                fullSamples[i] = MeasureRenderMs(fullSession);
                halfSamples[i] = MeasureRenderMs(halfSession);
            }
        }

        PairOutcome outcome = SummarizePairOutcomes(fullSamples, halfSamples);
        Array.Sort(fullSamples);
        Array.Sort(halfSamples);
        return new BenchmarkMeasurement(
            FullMedian: fullSamples[schedule.Length / 2],
            HalfMedian: halfSamples[schedule.Length / 2],
            outcome.HalfWins,
            outcome.Ties,
            schedule);
    }

    internal static RenderPairOrder[] CreateSchedule(int seed)
    {
        var random = new ScheduleRandom(unchecked((uint)seed));
        var schedule = new RenderPairOrder[11];
        Array.Fill(schedule, RenderPairOrder.HalfThenFull, 0, 5);
        Array.Fill(schedule, RenderPairOrder.FullThenHalf, 5, 5);
        schedule[^1] = random.Next(2) == 0
            ? RenderPairOrder.HalfThenFull
            : RenderPairOrder.FullThenHalf;

        for (int index = schedule.Length - 1; index > 0; index--)
        {
            int replacement = random.Next(index + 1);
            (schedule[index], schedule[replacement]) = (schedule[replacement], schedule[index]);
        }

        return schedule;
    }

    internal static PairOutcome SummarizePairOutcomes(
        IReadOnlyList<double> fullSamples,
        IReadOnlyList<double> halfSamples)
    {
        if (fullSamples.Count != halfSamples.Count)
            throw new ArgumentException("Paired benchmark sample counts must match.");

        int halfWins = 0;
        int ties = 0;
        for (int index = 0; index < fullSamples.Count; index++)
        {
            if (halfSamples[index] < fullSamples[index])
                halfWins++;
            else if (halfSamples[index] == fullSamples[index])
                ties++;
        }

        return new PairOutcome(halfWins, ties);
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
            BenchmarkMeasurement measurement = MeasureRenderMedians(ScheduleSeed);
            double ratio = measurement.HalfMedian / measurement.FullMedian;
            string realizedOrder = string.Join(
                ", ",
                measurement.Schedule.Select(static item => item == RenderPairOrder.HalfThenFull
                    ? "0.5/1.0"
                    : "1.0/0.5"));
            TestContext.WriteLine(
                $"render median: 1.0={measurement.FullMedian:F2}ms 0.5={measurement.HalfMedian:F2}ms "
                + $"ratio={ratio:F3} faster-pairs={measurement.HalfWins}/11 ties={measurement.Ties} "
                + $"seed={ScheduleSeed} order=[{realizedOrder}] "
                + $"rasterization-bound-target≈{RasterizationBoundTarget:F2}");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    measurement.HalfWins,
                    Is.GreaterThanOrEqualTo(9),
                    $"0.5x was faster in only {measurement.HalfWins}/11 pairs with {measurement.Ties} ties; "
                    + "a one-sided exact sign test requires at least 9/11 for p < 0.05");
                // Half scale shades one quarter as many pixels, but this short benchmark also includes
                // fixed planner and readback cost. Requiring a 15% median reduction rejects a nominal
                // 0.99x result while remaining stable when fixed work dominates the measured interval.
                Assert.That(
                    ratio,
                    Is.LessThan(0.85),
                    $"the half-scale median was {ratio:F3}x full scale, which is not a material reduction");
            }
        });
    }

    [Test]
    public void Schedule_HasFiveOrdersEachAndOneSeedSelectedOrder()
    {
        RenderPairOrder[] schedule = CreateSchedule(ScheduleSeed);

        int halfFirst = schedule.Count(static item => item == RenderPairOrder.HalfThenFull);
        int fullFirst = schedule.Count(static item => item == RenderPairOrder.FullThenHalf);
        Assert.Multiple(() =>
        {
            Assert.That(schedule, Has.Length.EqualTo(11));
            Assert.That(Math.Min(halfFirst, fullFirst), Is.EqualTo(5));
            Assert.That(Math.Max(halfFirst, fullFirst), Is.EqualTo(6));
        });
    }

    [Test]
    public void Schedule_IsReproducibleForThePinnedSeed()
    {
        Assert.That(CreateSchedule(ScheduleSeed), Is.EqualTo(CreateSchedule(ScheduleSeed)));
    }

    [Test]
    public void Schedule_UsesTheSeededPermutation()
    {
        Assert.That(
            CreateSchedule(ScheduleSeed),
            Is.EqualTo(new[]
            {
                RenderPairOrder.HalfThenFull,
                RenderPairOrder.HalfThenFull,
                RenderPairOrder.FullThenHalf,
                RenderPairOrder.FullThenHalf,
                RenderPairOrder.FullThenHalf,
                RenderPairOrder.FullThenHalf,
                RenderPairOrder.HalfThenFull,
                RenderPairOrder.HalfThenFull,
                RenderPairOrder.FullThenHalf,
                RenderPairOrder.FullThenHalf,
                RenderPairOrder.HalfThenFull,
            }));
    }

    [Test]
    public void PairOutcome_ReportsTiesAsNonWins()
    {
        PairOutcome outcome = SummarizePairOutcomes(
            fullSamples: new[] { 3d, 2d, 1d, 4d },
            halfSamples: new[] { 2d, 2d, 3d, 4d });

        Assert.Multiple(() =>
        {
            Assert.That(outcome.HalfWins, Is.EqualTo(1));
            Assert.That(outcome.Ties, Is.EqualTo(2));
        });
    }

    internal enum RenderPairOrder : byte
    {
        HalfThenFull,
        FullThenHalf,
    }

    internal readonly record struct PairOutcome(int HalfWins, int Ties);

    private readonly record struct BenchmarkMeasurement(
        double FullMedian,
        double HalfMedian,
        int HalfWins,
        int Ties,
        IReadOnlyList<RenderPairOrder> Schedule);

    private struct ScheduleRandom
    {
        private uint _state;

        public ScheduleRandom(uint seed)
        {
            _state = seed == 0 ? 0x9E3779B9u : seed;
        }

        public int Next(int exclusiveMaximum)
        {
            uint value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return (int)(value % (uint)exclusiveMaximum);
        }
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
                    DefaultRequest = new RenderNodeRenderRequest
                    {
                        Intent = RenderIntent.Delivery,
                        TargetDomain = new Rect(default, _logicalSize),
                        OutputScale = scale,
                        CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    },
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
