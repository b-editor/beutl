using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class FilterEffectOrderTests
{
    // The curves tab reads the colors an effect receives, so the effects after it are those acting on what it gives:
    // the later ones in each group around it, then the effects of the drawables around its drawable. A group among them
    // comes with what it holds, as each of its children draws as an effect of its own.
    [Test]
    public void The_effects_after_one_are_the_later_ones_around_it_and_those_of_the_groups_it_is_in()
    {
        var before = new Blur();
        var target = new Curves();
        var nestedAfter = new HueRotate();
        var after = new Invert();
        var laterChild = new Gamma();
        var laterGroup = new FilterEffectGroup();
        laterGroup.Children.Add(laterChild);
        var inner = new FilterEffectGroup();
        inner.Children.Add(target);
        inner.Children.Add(nestedAfter);
        var shape = new RectShape();
        var chain = (FilterEffectGroup)shape.FilterEffect.Value!;
        chain.Children.Add(before);
        chain.Children.Add(inner);
        chain.Children.Add(after);
        chain.Children.Add(laterGroup);
        var group = new DrawableGroup();
        group.Children.Add(shape);
        var groupEffects = (FilterEffectGroup)group.FilterEffect.Value!;
        var groupChild = new HueRotate();
        groupEffects.Children.Add(groupChild);

        Assert.That(
            FilterEffectOrder.After(target),
            Is.EquivalentTo(new EngineObject[] { nestedAfter, after, laterGroup, laterChild, groupEffects, groupChild }));
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
