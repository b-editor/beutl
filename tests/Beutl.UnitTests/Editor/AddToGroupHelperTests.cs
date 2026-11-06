using Beutl.Editor.Components.Helpers;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;

namespace Beutl.UnitTests.Editor;

public class AddToGroupHelperTests
{
    [Test]
    public void Add_ToATransformGroup_KeepsTheTransformsInTheirPlaces()
    {
        var shape = new RectShape();
        var group = (TransformGroup)shape.Transform.Value!;
        var scale = new ScaleTransform(150, 150);
        var translate = new TranslateTransform(30, 40);

        AddToGroupHelper.Add(shape.Transform, scale);
        AddToGroupHelper.Add(shape.Transform, translate);

        Assert.Multiple(() =>
        {
            Assert.That(shape.Transform.Value, Is.SameAs(group));
            Assert.That(group.Children, Is.EqualTo(new Transform[] { translate, scale }));
        });
    }

    [Test]
    public void Add_ToASingleTransform_PutsItInAGroupThatOwnsIt()
    {
        var existing = new RotationTransform(45);
        var shape = new RectShape { Transform = { Value = existing } };
        var translate = new TranslateTransform(30, 40);

        AddToGroupHelper.Add(shape.Transform, translate);

        var group = (TransformGroup)shape.Transform.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(group.Children, Is.EqualTo(new Transform[] { translate, existing }));
            Assert.That(existing.HierarchicalParent, Is.SameAs(group));
        });
    }

    [Test]
    public void Add_ToAnUnsetTransform_CreatesAGroup()
    {
        var shape = new RectShape { Transform = { Value = null } };
        var translate = new TranslateTransform(30, 40);

        AddToGroupHelper.Add(shape.Transform, translate);

        Assert.That(((TransformGroup)shape.Transform.Value!).Children.Single(), Is.SameAs(translate));
    }

    [Test]
    public void Add_ToASingleFilterEffect_PutsItInAGroupThatOwnsIt()
    {
        var existing = new Brightness();
        var shape = new RectShape { FilterEffect = { Value = existing } };
        var blur = new Blur();

        AddToGroupHelper.Add(shape.FilterEffect, blur);

        var group = (FilterEffectGroup)shape.FilterEffect.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(group.Children, Is.EqualTo(new FilterEffect[] { existing, blur }));
            Assert.That(existing.HierarchicalParent, Is.SameAs(group));
        });
    }

    [Test]
    public void Add_ToAnUnsetFilterEffect_CreatesAGroup()
    {
        var shape = new RectShape { FilterEffect = { Value = null } };
        var blur = new Blur();

        AddToGroupHelper.Add(shape.FilterEffect, blur);

        Assert.That(((FilterEffectGroup)shape.FilterEffect.Value!).Children.Single(), Is.SameAs(blur));
    }
}
