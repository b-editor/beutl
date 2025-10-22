using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Operation;

namespace Beutl.UnitTests.ProjectSystem.Operation;

public class PublishOperatorTests
{
    private class TestDrawable : Drawable
    {
        protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
        {
            return new Size(100, 100);
        }

        protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
        {
        }

        public override Resource ToResource(RenderContext context)
        {
            var res = new Resource();
            var updateOnly = true;
            res.Update(this, context, ref updateOnly);
            return res;
        }

        public new class Resource : Drawable.Resource
        {
        }
    }

    private class TestOperator : PublishOperator<TestDrawable>
    {
        protected override void FillProperties()
        {
            AddProperty(Value.Transform, new TransformGroup());
            AddProperty(Value.AlignmentX);
            AddProperty(Value.AlignmentY);
            AddProperty(Value.TransformOrigin);
            AddProperty(Value.Fill, new SolidColorBrush(Colors.White));
            AddProperty(Value.FilterEffect, new FilterEffectGroup());
            AddProperty(Value.BlendMode);
            AddProperty(Value.Opacity, 100f);
        }
    }

    [Test]
    public void ValueProperty_ShouldBeConfigured()
    {
        var obj = new TestOperator();

        Assert.That(obj.Value, Is.Not.Null);
    }

    [Test]
    public void Properties_ShouldBeConfigured()
    {
        var obj = new TestOperator();

        Assert.That(obj.Properties, Is.Not.Null);
        Assert.That(obj.Properties.Count, Is.EqualTo(8));
        Assert.That(obj.Properties[0].GetValue(), Is.InstanceOf<TransformGroup>());
        Assert.That(obj.Properties[1].GetValue(), Is.EqualTo(AlignmentX.Center));
        Assert.That(obj.Properties[2].GetValue(), Is.EqualTo(AlignmentY.Center));
        Assert.That(obj.Properties[3].GetValue(), Is.EqualTo(RelativePoint.Center));
        Assert.That(obj.Properties[4].GetValue(), Is.InstanceOf<SolidColorBrush>());
        Assert.That(obj.Properties[5].GetValue(), Is.InstanceOf<FilterEffectGroup>());
        Assert.That(obj.Properties[6].GetValue(), Is.EqualTo(BlendMode.SrcOver));
        Assert.That(obj.Properties[7].GetValue(), Is.EqualTo(100f));
    }

    [Test]
    public void Value_Invalidated_ShouldTriggerOperatorInvalidated()
    {
        var obj = new TestOperator();
        bool invalidatedTriggered = false;
        obj.Invalidated += (s, e) => invalidatedTriggered = true;

        obj.Value.Invalidate();

        Assert.That(invalidatedTriggered, Is.True);
    }
}
