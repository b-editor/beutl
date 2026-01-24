using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Operation;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.GeometryShape), ResourceType = typeof(Strings))]
public sealed class GeometryOperator : PublishOperator<GeometryShape>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Data, new PathGeometry());
        AddProperty(Value.Transform, new TransformGroup());
        AddProperty(Value.AlignmentX, AlignmentX.Left);
        AddProperty(Value.AlignmentY, AlignmentY.Top);
        AddProperty(Value.TransformOrigin);
        AddProperty(Value.Pen);
        AddProperty(Value.Fill, new SolidColorBrush(Colors.White));
        AddProperty(Value.FilterEffect, new FilterEffectGroup());
        AddProperty(Value.BlendMode);
        AddProperty(Value.Opacity);
    }
}
