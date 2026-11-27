using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

public sealed class TextAnimationOrderingTests
{
    [TestCase(TextAnimationUnit.Characters, new float[] { 3, 4, 0, 2, 1, 0, -1, 5, 6 })]
    [TestCase(TextAnimationUnit.Words, new float[] { 2, 3, 0, 1, 0, 0, -1, 4, 5 })]
    [TestCase(TextAnimationUnit.Lines, new float[] { 1, 2, 0, 0, 0, 0, -1, 3, 4 })]
    public void IndexOrdersRankLogicalUnitsAcrossMixedOccurrences(TextAnimationUnit unit, float[] forward)
    {
        var bounds = new Rect(0, 0, 6, 8);
        using var parts = new EffectTargets
        {
            Part(bounds, Tag(10, 3, 2, 1)),
            Part(bounds),
            Part(bounds, Tag(10, 0, 0, 0)),
            Part(bounds, Tag(10, 2, 1, 0)),
            Part(bounds, Tag(10, 1, 0, 0)),
            Part(bounds, Tag(10, 0, 0, 0)),
            Part(bounds, Tag(10, -1, -1, 0)),
            Part(bounds, Tag(20, 0, 0, 0)),
            Part(bounds),
        };
        int max = (int)forward.Max();
        var seededRanks = new float[max + 1];
        DelayAnimationOrdering.ComputeRanks(DelayAnimationOrder.Random, 93, [], seededRanks);

        Assert.That(Ranks(parts, unit, DelayAnimationOrder.Forward), Is.EqualTo(forward));
        Assert.That(Ranks(parts, unit, DelayAnimationOrder.Reverse),
            Is.EqualTo(forward.Select(rank => rank < 0 ? rank : max - rank)));
        Assert.That(Ranks(parts, unit, DelayAnimationOrder.Random, 93),
            Is.EqualTo(forward.Select(rank => rank < 0 ? rank : seededRanks[(int)rank])));
    }

    [TestCase(WritingMode.HorizontalTb, DelayAnimationOrder.TopToBottom, false)]
    [TestCase(WritingMode.HorizontalTb, DelayAnimationOrder.BottomToTop, true)]
    [TestCase(WritingMode.VerticalRl, DelayAnimationOrder.LeftToRight, false)]
    [TestCase(WritingMode.VerticalRl, DelayAnimationOrder.RightToLeft, true)]
    public void AcrossLineSweepsTieDifferentInkCentersInEveryUnit(
        WritingMode mode, DelayAnimationOrder order, bool reverse)
    {
        using var parts = new EffectTargets
        {
            Part(At(0, 8, 8, 12), Tag(10, 0, 0, 0, mode)),
            Part(At(10, 1, 8, 19), Tag(10, 1, 0, 0, mode)),
            Part(At(20, 15, 4, 8), Tag(10, 2, 1, 0, mode)),
            Part(At(0, 50, 8, 20), Tag(10, 3, 2, 1, mode)),
            Part(At(0, 90, 8, 20)),
        };

        foreach (TextAnimationUnit unit in Enum.GetValues<TextAnimationUnit>())
        {
            float[] ranks = Ranks(parts, unit, order);
            Assert.That(ranks[..3], Is.All.EqualTo(ranks[0]), unit.ToString());
            if (reverse)
            {
                Assert.That(ranks[0], Is.GreaterThan(ranks[3]));
                Assert.That(ranks[3], Is.GreaterThan(ranks[4]));
            }
            else
            {
                Assert.That(ranks[0], Is.LessThan(ranks[3]));
                Assert.That(ranks[3], Is.LessThan(ranks[4]));
            }
        }

        Rect At(int x, int y, int width, int height) => mode == WritingMode.HorizontalTb
            ? new Rect(x, y, width, height)
            : new Rect(y, x, height, width);
    }

    [TestCase(DelayAnimationOrder.LeftToRight)]
    [TestCase(DelayAnimationOrder.FromCenter)]
    public void PositionalRanksUseEachWordsCompleteUnion(DelayAnimationOrder order)
    {
        var first = new Rect(0, 0, 28, 10);
        var second = new Rect(35, 0, 5, 10);
        var third = new Rect(90, 0, 20, 10);
        using var parts = new EffectTargets
        {
            Part(new Rect(20, 0, 4, 10), Tag(10, 0, 0, 0), first),
            Part(new Rect(37, 0, 2, 10), Tag(10, 1, 0, 0), second),
            Part(new Rect(90, 0, 4, 10), Tag(10, 2, 1, 0), third),
        };
        var expected = new float[2];
        DelayAnimationOrdering.ComputeRanks(order, 0, [first.Union(second), third], expected);

        Assert.That(Ranks(parts, TextAnimationUnit.Words, order), Is.EqualTo(new[] { expected[0], expected[0], expected[1] }));
    }

    [Test]
    public void UnselectablePartsGetZeroAmountEvenWhenTheRangeCoversThem()
    {
        var bounds = new Rect(0, 0, 6, 8);
        using var parts = new EffectTargets { Part(bounds, Tag(10, -1, -1, 0)), Part(bounds, Tag(10, 0, 0, 0)) };
        float[] amounts = Ranks(parts, TextAnimationUnit.Lines, DelayAnimationOrder.Forward);

        RangeAnimatorSelector.ConvertRanksToAmounts(amounts, 0, 100, -50, RangeAnimatorShape.RampDown, 0);

        Assert.That(amounts, Is.EqualTo(new[] { 0f, 0f }));
        amounts = Ranks(parts, TextAnimationUnit.Lines, DelayAnimationOrder.Forward);
        RangeAnimatorSelector.ConvertRanksToAmounts(amounts, 0, 100, 0, RangeAnimatorShape.RampDown, 0);
        Assert.That(amounts, Is.EqualTo(new[] { 0f, 0.5f }));
    }

    private static TextPartTag Tag(long occurrence, int character, int word, int line, WritingMode mode = WritingMode.HorizontalTb)
        => new(occurrence, 1, new TextPartIndices(character, word, line), new TextPartCounts(4, 3, 2), mode);

    private static EffectTarget Part(Rect bounds, TextPartTag? text = null, Rect? complete = null)
    {
        using var raster = new CpuRenderTarget((int)bounds.Width, (int)bounds.Height);
        return new EffectTarget(raster, bounds) { TextPart = text, CompleteBounds = complete };
    }

    private static float[] Ranks(EffectTargets parts, TextAnimationUnit unit, DelayAnimationOrder order, int seed = 0)
    {
        var ranks = new float[parts.Count];
        DelayAnimationOrdering.ComputeTargetRanks(order, seed, parts, ranks, unit);
        return ranks;
    }
}
