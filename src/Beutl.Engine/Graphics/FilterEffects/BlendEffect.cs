using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.BlendEffect), ResourceType = typeof(GraphicsStrings))]
[DroppedMembers("Color")]
public sealed partial class BlendEffect : FilterEffect
{
    public BlendEffect()
    {
        ScanProperties<BlendEffect>();
        Brush.Value = new SolidColorBrush(Colors.White);
    }

    [Display(Name = nameof(GraphicsStrings.Brush), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Brush?> Brush { get; } = Property.CreateExpression<Brush?>();

    [Display(Name = nameof(GraphicsStrings.BlendEffect_BlendMode), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<BlendMode> BlendMode { get; } = Property.CreateAnimatable(Graphics.BlendMode.SrcIn);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.BlendMode(Brush, BlendMode);
        }
    }
}
