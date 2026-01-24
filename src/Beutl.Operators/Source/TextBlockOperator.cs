using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Operation;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.Text), ResourceType = typeof(Strings))]
public sealed class TextBlockOperator : PublishOperator<TextBlock>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Size, 24f);
        AddProperty(Value.FontFamily, Media.FontFamily.Default);
        AddProperty(Value.FontStyle, Media.FontStyle.Normal);
        AddProperty(Value.FontWeight, Media.FontWeight.Regular);
        AddProperty(Value.Spacing, 0f);
        AddProperty(Value.SplitByCharacters);
        AddProperty(Value.Text, string.Empty);
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
