using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class DelayAnimationOrderTests
{
    [TestCase(DelayAnimationOrder.Forward, new float[] { 0, 1, 2, 3, 4 })]
    [TestCase(DelayAnimationOrder.Reverse, new float[] { 4, 3, 2, 1, 0 })]
    [TestCase(DelayAnimationOrder.Forward, new float[] { 0, 1, 2, 3 })]
    [TestCase(DelayAnimationOrder.Reverse, new float[] { 3, 2, 1, 0 })]
    public void IndexOrdersUseTheOriginalPartIndices(DelayAnimationOrder order, float[] expected)
    {
        var ranks = new float[expected.Length];
        DelayAnimationOrdering.ComputeRanks(order, 0, [], ranks);

        Assert.That(ranks, Is.EqualTo(expected));
    }

    [Test]
    public void EmptyAndSingleTargetLists([Values] DelayAnimationOrder order)
    {
        Assert.DoesNotThrow(() => DelayAnimationOrdering.ComputeRanks(order, 42, [], []));

        float[] ranks = [-1];
        DelayAnimationOrdering.ComputeRanks(order, 42, [new Rect(20, 30, 10, 5)], ranks);
        Assert.That(ranks, Is.All.Zero);
    }

    [Test]
    public void RandomOrderIsSeededAndPreservesRelativeOrderAfterAppend()
    {
        const int count = 10;
        var ranks = new float[count];
        var sameSeed = new float[count];
        var differentSeed = new float[count];
        var appended = new float[count + 2];
        DelayAnimationOrdering.ComputeRanks(DelayAnimationOrder.Random, 19, [], ranks);
        DelayAnimationOrdering.ComputeRanks(DelayAnimationOrder.Random, 19, [], sameSeed);
        DelayAnimationOrdering.ComputeRanks(DelayAnimationOrder.Random, -42, [], differentSeed);
        DelayAnimationOrdering.ComputeRanks(DelayAnimationOrder.Random, 19, [], appended);

        IEnumerable<int> indices = Enumerable.Range(0, count);
        Assert.Multiple(() =>
        {
            Assert.That(ranks, Is.EquivalentTo(indices.Select(i => (float)i)));
            Assert.That(sameSeed, Is.EqualTo(ranks));
            Assert.That(differentSeed, Is.Not.EqualTo(ranks));
            Assert.That(indices.OrderBy(i => appended[i]), Is.EqualTo(indices.OrderBy(i => ranks[i])));
        });
    }

    [Test]
    public void RandomOrderStartsWithEveryPartAboutEquallyOften()
    {
        const int seeds = 2000;
        Assert.Multiple(() =>
        {
            for (int count = 2; count <= 16; count++)
            {
                int[] starts = CountFirstParts(count, seeds);
                double expected = (double)seeds / count;
                double chiSquare = starts.Sum(observed => (observed - expected) * (observed - expected) / expected);
                Assert.That(chiSquare, Is.LessThan(ChiSquareUpperBound(count - 1)), $"{count} parts start first {string.Join(", ", starts)} times");
            }
        });
    }

    // Seeds that people type are small. Part 0's key used to be the seed's hash alone, which is small for small seeds.
    [Test]
    public void SmallSeedsDoNotFavourTheFirstPart()
    {
        const int seeds = 32;
        Assert.Multiple(() =>
        {
            for (int count = 3; count <= 16; count++)
            {
                double share = 1.0 / count;
                double bound = seeds * share + 2.5 * Math.Sqrt(seeds * share * (1 - share));
                Assert.That(CountFirstParts(count, seeds)[0], Is.LessThanOrEqualTo(bound), $"{count} parts");
            }
        });
    }

    [TestCase(3)]
    [TestCase(4)]
    public void TheDefaultSeedDoesNotKeepTheForwardOrder(int count)
    {
        var ranks = new float[count];
        DelayAnimationOrdering.ComputeRanks(DelayAnimationOrder.Random, 0, [], ranks);

        Assert.That(ranks, Is.Not.EqualTo(Enumerable.Range(0, count).Select(i => (float)i)));
    }

    // How many of the seeds 0 to seeds - 1 start the Random order with each part.
    private static int[] CountFirstParts(int count, int seeds)
    {
        var starts = new int[count];
        var ranks = new float[count];
        for (int seed = 0; seed < seeds; seed++)
        {
            DelayAnimationOrdering.ComputeRanks(DelayAnimationOrder.Random, seed, [], ranks);
            starts[Array.IndexOf(ranks, 0f)]++;
        }

        return starts;
    }

    // The Wilson-Hilferty approximation of the chi-square distribution's 99.9th percentile.
    private static double ChiSquareUpperBound(int degreesOfFreedom)
    {
        double variance = 2.0 / (9 * degreesOfFreedom);
        return degreesOfFreedom * Math.Pow(1 - variance + 3.09 * Math.Sqrt(variance), 3);
    }

    [TestCase(DelayAnimationOrder.TopToBottom, new float[] { 0, 0, 0, 0, 40f / 30f, 40f / 30f, 40f / 30f })]
    [TestCase(DelayAnimationOrder.BottomToTop, new float[] { 40f / 30f, 40f / 30f, 40f / 30f, 0, 0, 0, 0 })]
    [TestCase(DelayAnimationOrder.LeftToRight, new float[] { 0, 1, 2, 0, 0, 1, 2 })]
    [TestCase(DelayAnimationOrder.RightToLeft, new float[] { 2, 1, 0, 0, 2, 1, 0 })]
    public void PositionOrdersSweepTheGridAndSkipEmptyTargets(DelayAnimationOrder order, float[] expected)
    {
        Rect?[] bounds =
        [
            new Rect(100, 200, 20, 30),
            new Rect(120, 200, 20, 30),
            new Rect(140, 200, 20, 30),
            null,
            new Rect(100, 240, 20, 30),
            new Rect(120, 240, 20, 30),
            new Rect(140, 240, 20, 30),
        ];
        var ranks = new float[bounds.Length];

        DelayAnimationOrdering.ComputeRanks(order, 0, bounds, ranks);

        Assert.That(ranks, Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void PositionOrderUsesTheMedianOfPositiveSizes()
    {
        // Centers are thirty units apart. The four positive widths have a median of thirty.
        Rect?[] bounds = [new Rect(0, 0, 0, 10), new Rect(10, 0, 40, 10), new Rect(55, 0, 10, 10),
            new Rect(50, 0, 80, 10), new Rect(110, 0, 20, 10)];
        var ranks = new float[bounds.Length];

        DelayAnimationOrdering.ComputeRanks(DelayAnimationOrder.LeftToRight, 0, bounds, ranks);

        Assert.That(ranks, Is.EqualTo(new float[] { 0, 1, 2, 3, 4 }));
    }

    [Test]
    public void PositionOrderFallsBackToUnitSizeAndHandlesNonFiniteCenters()
    {
        Rect?[] bounds = [new Rect(5, 0, 0, 10), new Rect(float.NaN, 0, 0, 10), new Rect(7, 0, 0, 10)];
        var ranks = new float[bounds.Length];

        DelayAnimationOrdering.ComputeRanks(DelayAnimationOrder.LeftToRight, 0, bounds, ranks);

        Assert.That(ranks, Is.EqualTo(new float[] { 0, 0, 2 }));
    }

    [Test]
    public void FromCenterUsesTwoDimensionalDistanceAndSkipsEmptyTargets()
    {
        Rect?[] bounds =
        [
            new Rect(20, 30, 10, 10),
            new Rect(10, 20, 10, 10),
            new Rect(10, 30, 10, 10),
            null,
            new Rect(20, 40, 10, 10),
            new Rect(30, 30, 10, 10),
            new Rect(30, 40, 10, 10),
        ];
        var ranks = new float[bounds.Length];
        float diagonal = MathF.Sqrt(2);

        DelayAnimationOrdering.ComputeRanks(DelayAnimationOrder.FromCenter, 0, bounds, ranks);

        Assert.That(ranks, Is.EqualTo(new float[] { 0, diagonal, 1, 0, 1, 1, diagonal }).Within(0.0001f));
    }

    [Test]
    public void FromCenterUsesTheCombinedBoundsAndMedianSideLength()
    {
        Rect?[] bounds = [new Rect(60, 0, 20, 20), new Rect(0, 0, 10, 20), new Rect(20, 0, 40, 20)];
        var ranks = new float[bounds.Length];

        DelayAnimationOrdering.ComputeRanks(DelayAnimationOrder.FromCenter, 0, bounds, ranks);

        Assert.That(ranks, Is.EqualTo(new float[] { 1.5f, 1.75f, 0 }));
    }

    [TestCase(DelayAnimationOrder.Forward, 250f, new float[] { 0.5f, 0.25f, 0, 0 })]
    [TestCase(DelayAnimationOrder.Reverse, 250f, new float[] { 0, 0, 0.25f, 0.5f })]
    [TestCase(DelayAnimationOrder.Forward, float.NaN, new float[] { 0.5f, 0.5f, 0.5f, 0.5f })]
    [TestCase(DelayAnimationOrder.Reverse, float.PositiveInfinity, new float[] { 0.5f, 0.5f, 0.5f, 0.5f })]
    public void RunsAnimatedOpacityAtTheRankedTime(DelayAnimationOrder order, float delay, float[] expected)
    {
        var effect = new DelayAnimationEffect
        {
            Delay = { Value = delay },
            Order = { Value = order },
        };

        Assert.That(RenderAnimatedOpacity(effect, expected.Length), Is.EqualTo(expected).Within(0.001f));
    }

    [TestCase(DelayAnimationOrder.Forward, new float[] { 0.5f, 0 })]
    [TestCase(DelayAnimationOrder.Reverse, new float[] { 0, 1f / 6, 1f / 3, 0.5f })]
    [TestCase(DelayAnimationOrder.LeftToRight, new float[] { 0.5f, 0.375f, 0.25f, 0.125f, 0 })]
    [TestCase(DelayAnimationOrder.FromCenter, new float[] { 0, 0.5f, 0.5f, 0 })]
    public void TotalDelaySpansTheSameDurationAcrossOrdersAndCounts(DelayAnimationOrder order, float[] expected)
    {
        var effect = new DelayAnimationEffect
        {
            Delay = { Value = 500 },
            DelayMode = { Value = DelayAnimationMode.Total },
            Order = { Value = order },
        };

        Assert.That(RenderAnimatedOpacity(effect, expected.Length), Is.EqualTo(expected).Within(0.001f));
    }

    [TestCase(DelayAnimationOrder.Reverse, 1)]
    [TestCase(DelayAnimationOrder.TopToBottom, 3)]
    [TestCase(DelayAnimationOrder.FromCenter, 2)]
    public void TotalDelayDoesNotWaitWhenAllPartsStartTogether(DelayAnimationOrder order, int count)
    {
        var effect = new DelayAnimationEffect
        {
            Delay = { Value = 500 },
            DelayMode = { Value = DelayAnimationMode.Total },
            Order = { Value = order },
        };

        Assert.That(RenderAnimatedOpacity(effect, count), Is.All.EqualTo(0.5f).Within(0.001f));
    }

    private static float[] RenderAnimatedOpacity(DelayAnimationEffect effect, int count)
    {
        var opacity = new OpacityEffect();
        opacity.Opacity.SetKeyFrameAnimation(
            TestAnimations.Linear(0f, 100f, MediaTime.FromSeconds(1), useGlobalClock: true));
        effect.Effect.Value = opacity;

        using var targets = new EffectTargets();
        for (int i = 0; i < count; i++)
            targets.Add(FilterEffectTestHarness.CreateSolidTarget(new Rect(i * 2, 0, 2, 2)));

        using DelayAnimationEffect.Resource resource = effect.ToResource(new CompositionContext(MediaTime.FromSeconds(0.5)));
        using var context = new FilterEffectContext(targets.CalculateBounds());
        context.ApplyTransactional(resource);
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: null,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        executor.Apply(context);
        executor.Flush(false);

        return targets.Select(target =>
        {
            using Bitmap bitmap = target.RenderTarget!.Snapshot();
            return (float)BitConverter.UInt16BitsToHalf(bitmap.GetPixelSpan<ushort>()[3]);
        }).ToArray();
    }
}
