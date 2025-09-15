using System;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Operators.Source;

namespace Beutl.UnitTests.Operators;

public class SourceOperatorsDefaultsTests
{
    [Test]
    public void RectOperator_Defaults()
    {
        var op = new RectOperator();
        Assert.That(op.Value, Is.Not.Null);
        var props = op.Properties;
        Assert.That(props.Count, Is.GreaterThanOrEqualTo(11));
        Assert.That(props.First(p => p.GetCoreProperty() == Shape.WidthProperty).GetValue(), Is.EqualTo(100f));
        Assert.That(props.First(p => p.GetCoreProperty() == Shape.HeightProperty).GetValue(), Is.EqualTo(100f));
        Assert.That(props.First(p => p.GetCoreProperty() == Drawable.TransformProperty).GetValue(), Is.InstanceOf<TransformGroup>());
        Assert.That(props.First(p => p.GetCoreProperty() == Drawable.FillProperty).GetValue(), Is.InstanceOf<SolidColorBrush>());
        Assert.That(props.First(p => p.GetCoreProperty() == Drawable.FilterEffectProperty).GetValue(), Is.InstanceOf<FilterEffectGroup>());
    }

    [Test]
    public void TextBlockOperator_Defaults()
    {
        var op = new TextBlockOperator();
        Assert.That(op.Value, Is.Not.Null);
        var props = op.Properties;
        Assert.That(props.Count, Is.GreaterThanOrEqualTo(16));
        Assert.That(props.First(p => p.GetCoreProperty() == TextBlock.SizeProperty).GetValue(), Is.EqualTo(24f));
        Assert.That(props.First(p => p.GetCoreProperty() == TextBlock.TextProperty).GetValue(), Is.EqualTo(string.Empty));
        Assert.That(props.First(p => p.GetCoreProperty() == Drawable.TransformProperty).GetValue(), Is.InstanceOf<TransformGroup>());
        Assert.That(props.First(p => p.GetCoreProperty() == Drawable.FillProperty).GetValue(), Is.InstanceOf<SolidColorBrush>());
    }

    [Test]
    public void GeometryOperator_Defaults()
    {
        var op = new GeometryOperator();
        Assert.That(op.Value, Is.Not.Null);
        var props = op.Properties;
        Assert.That(props.Count, Is.GreaterThanOrEqualTo(10));
        Assert.That(props.First(p => p.GetCoreProperty() == GeometryShape.DataProperty).GetValue(), Is.InstanceOf<PathGeometry>());
        Assert.That(props.First(p => p.GetCoreProperty() == Drawable.TransformProperty).GetValue(), Is.InstanceOf<TransformGroup>());
        Assert.That(props.First(p => p.GetCoreProperty() == Drawable.FillProperty).GetValue(), Is.InstanceOf<SolidColorBrush>());
        Assert.That(props.First(p => p.GetCoreProperty() == Drawable.FilterEffectProperty).GetValue(), Is.InstanceOf<FilterEffectGroup>());
    }
}
