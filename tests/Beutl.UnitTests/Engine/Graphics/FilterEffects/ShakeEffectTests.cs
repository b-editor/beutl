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
