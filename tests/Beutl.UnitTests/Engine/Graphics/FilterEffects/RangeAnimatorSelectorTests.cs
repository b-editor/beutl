using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class RangeAnimatorSelectorTests
{
    [TestCase(RangeAnimatorShape.Square, new float[] { 0, 1, 1, 1, 0 })]
    [TestCase(RangeAnimatorShape.RampUp, new float[] { 0, 1f / 6, 0.5f, 5f / 6, 1 })]
    [TestCase(RangeAnimatorShape.RampDown, new float[] { 1, 5f / 6, 0.5f, 1f / 6, 0 })]
    [TestCase(RangeAnimatorShape.Triangle, new float[] { 0, 1f / 3, 1, 1f / 3, 0 })]
    [TestCase(RangeAnimatorShape.Round, new float[] { 0, 0.745356f, 1, 0.745356f, 0 })]
    [TestCase(RangeAnimatorShape.Smooth, new float[] { 0, 0.25f, 1, 0.25f, 0 })]
    public void ShapesSampleEachSlotsCenter(RangeAnimatorShape shape, float[] expected)
    {
        float[] ranks = [0, 1, 2, 3, 4];

        RangeAnimatorSelector.ConvertRanksToAmounts(ranks, 20, 80, 0, shape, 100);

        Assert.That(ranks, Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void SwappedEndpointsSelectTheSameRangeBeforeOffset()
    {
        float[] ranks = [0, 1, 2, 3];

        RangeAnimatorSelector.ConvertRanksToAmounts(ranks, 70, 20, 10, RangeAnimatorShape.Square, 100);

        Assert.That(ranks, Is.EqualTo(new float[] { 0, 0.8f, 1, 0.2f }).Within(0.0001f));
    }

    [TestCase(100)]
    [TestCase(0)]
    public void OffsetSweepsASquareAcrossThreeParts(float smoothness)
    {
        const float slot = 100f / 3;
        float[] offsets = [-0.75f, 0.25f, 0.75f, 1.25f, 1.75f, 2.25f, 2.75f];
        float[][] expected = smoothness == 100
            ? [[0.25f, 0, 0], [0.75f, 0.25f, 0], [0.25f, 0.75f, 0], [0, 0.75f, 0.25f],
                [0, 0.25f, 0.75f], [0, 0, 0.75f], [0, 0, 0.25f]]
            : [[0, 0, 0], [1, 0, 0], [0, 1, 0], [0, 1, 0], [0, 0, 1], [0, 0, 1], [0, 0, 0]];
        for (int i = 0; i < offsets.Length; i++)
        {
            float[] ranks = [0, 1, 2];

            RangeAnimatorSelector.ConvertRanksToAmounts(ranks, 0, slot, offsets[i] * slot, RangeAnimatorShape.Square, smoothness);

            Assert.That(ranks, Is.EqualTo(expected[i]).Within(0.0001f), $"Offset of {offsets[i]} slots");
        }
    }

    [Test]
    public void HalfSmoothnessRampsOverHalfASlot()
    {
        float[] ranks = [0, 1, 2, 3];

        RangeAnimatorSelector.ConvertRanksToAmounts(ranks, 28.125f, 59.375f, 0, RangeAnimatorShape.Square, 50);

        Assert.That(ranks, Is.EqualTo(new float[] { 0, 1, 0.25f, 0 }));
    }

    [TestCase(DelayAnimationOrder.LeftToRight, new float[] { 1, 0, 0, 1 })]
    [TestCase(DelayAnimationOrder.RightToLeft, new float[] { 0, 1, 1, 0 })]
    [TestCase(DelayAnimationOrder.TopToBottom, new float[] { 1, 0, 1, 0 })]
    [TestCase(DelayAnimationOrder.BottomToTop, new float[] { 0, 1, 0, 1 })]
    public void PositionOrdersSelectLevelPartsTogether(DelayAnimationOrder order, float[] expected)
    {
        Rect?[] bounds = [new Rect(0, 0, 20, 20), new Rect(40, 40, 20, 20),
            new Rect(40, 0, 20, 20), new Rect(0, 40, 20, 20)];
        var ranks = new float[bounds.Length];
        DelayAnimationOrdering.ComputeRanks(order, 0, bounds, ranks);

        RangeAnimatorSelector.ConvertRanksToAmounts(ranks, 0, 40, 0, RangeAnimatorShape.Square, 100);

        Assert.That(ranks, Is.EqualTo(expected));
    }

    [Test]
    public void ASelectionCanCoverHalfOfASinglePart()
    {
        float[] ranks = [0];

        RangeAnimatorSelector.ConvertRanksToAmounts(ranks, 0, 50, 0, RangeAnimatorShape.Square, 100);

        Assert.That(ranks, Is.EqualTo(new float[] { 0.5f }));
    }
}
