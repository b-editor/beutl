using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class ShakeEffectTests
{
    private static readonly Rect s_bounds = new(10, 20, 100, 60);

    [TestCase(0f, 0f)]
    [TestCase(float.NaN, 0f)]
    [TestCase(0f, float.PositiveInfinity)]
    public void StrengthThatMovesNothing_RecordsNothing(float x, float y)
    {
        using var context = Apply(x, y);

        Assert.Multiple(() =>
        {
            Assert.That(context.GetOrderedItems(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(s_bounds));
        });
    }

    [TestCase(12f, 0f)]
    [TestCase(0f, -12f)]
    public void StrengthOnOneAxis_RecordsTheShake(float x, float y)
    {
        using var context = Apply(x, y);

        Assert.That(context.GetOrderedItems(), Has.Count.EqualTo(1));
    }

    [TestCase(12f, 0f)]
    [TestCase(5f, -30f)]
    public void TheShake_ReadsOnlyAsFarAsItsStrengthAroundWhatItDraws(float x, float y)
    {
        using var context = Apply(x, y);
        var shake = (IFEItem_Custom)context.GetOrderedItems().Single();
        var output = new Rect(0, 0, 100, 50);

        Assert.That(shake.TryTransformSamplingBounds(output, out Rect input), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(input.Contains(output.Inflate(new Thickness(MathF.Abs(x), MathF.Abs(y)))), Is.True,
                "an offset as large as the strength must still find its pixels in what is read");
            Assert.That(input.Width, Is.LessThanOrEqualTo(output.Width + 2 * MathF.Abs(x) + 2));
            Assert.That(input.Height, Is.LessThanOrEqualTo(output.Height + 2 * MathF.Abs(y) + 2));
        });
    }

    private static FilterEffectContext Apply(float x, float y)
    {
        var effect = new ShakeEffect();
        effect.StrengthX.Value = x;
        effect.StrengthY.Value = y;
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);
        return context;
    }
}
