using System.ComponentModel.DataAnnotations;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Operation;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.Image), ResourceType = typeof(Strings))]
public sealed class SourceImageOperator : PublishOperator<SourceImage>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Source);
        AddProperty(Value.Transform, new TransformGroup());
        AddProperty(Value.AlignmentX);
        AddProperty(Value.AlignmentY);
        AddProperty(Value.TransformOrigin);
        AddProperty(Value.FilterEffect, new FilterEffectGroup());
        AddProperty(Value.BlendMode);
        AddProperty(Value.Opacity);
    }
}
