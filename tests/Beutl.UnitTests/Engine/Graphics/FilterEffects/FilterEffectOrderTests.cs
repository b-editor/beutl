using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class FilterEffectOrderTests
{
    // The curves tab reads the colors an effect receives, so the effects after it are those acting on what it gives:
    // the later ones in each group around it, then the effects of the drawables around its drawable.
    [Test]
    public void The_effects_after_one_are_the_later_ones_around_it_and_those_of_the_groups_it_is_in()
    {
        var before = new Blur();
        var target = new Curves();
        var nestedAfter = new HueRotate();
        var after = new Invert();
        var inner = new FilterEffectGroup();
        inner.Children.Add(target);
        inner.Children.Add(nestedAfter);
        var shape = new RectShape();
        var chain = (FilterEffectGroup)shape.FilterEffect.Value!;
        chain.Children.Add(before);
        chain.Children.Add(inner);
        chain.Children.Add(after);
        var group = new DrawableGroup();
        group.Children.Add(shape);
        FilterEffect groupEffects = group.FilterEffect.Value!;

        Assert.That(FilterEffectOrder.After(target), Is.EquivalentTo(new EngineObject[] { nestedAfter, after, groupEffects }));
    }

    [Test]
    public void The_last_effect_of_a_drawable_on_its_own_has_none_after_it()
    {
        var target = new Curves();
        var shape = new RectShape();
        ((FilterEffectGroup)shape.FilterEffect.Value!).Children.Add(target);

        Assert.That(FilterEffectOrder.After(target), Is.Empty);
    }
}
