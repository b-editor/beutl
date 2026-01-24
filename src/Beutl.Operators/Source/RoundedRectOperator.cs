using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Operation;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.RoundedRect), ResourceType = typeof(Strings))]
public sealed class RoundedRectOperator : PublishOperator<RoundedRectShape>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Width, 100f);
        AddProperty(Value.Height, 100f);
        AddProperty(Value.CornerRadius, new CornerRadius(25));
        AddProperty(Value.Smoothing, 0f);
        AddProperty(Value.Transform, new TransformGroup());
        AddProperty(Value.AlignmentX);
        AddProperty(Value.AlignmentY);
        AddProperty(Value.TransformOrigin);
        AddProperty(Value.Pen);
        AddProperty(Value.Fill, new SolidColorBrush(Colors.White));
        AddProperty(Value.FilterEffect, new FilterEffectGroup());
        AddProperty(Value.BlendMode);
        AddProperty(Value.Opacity);
    }
}
