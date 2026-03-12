using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.BlendEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class BlendEffect : FilterEffect
{
    public BlendEffect()
    {
        ScanProperties<BlendEffect>();
        Brush.CurrentValue = new SolidColorBrush(Colors.White);
    }

    [Display(Name = nameof(GraphicsStrings.Brush), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Brush?> Brush { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(GraphicsStrings.BlendEffect_BlendMode), ResourceType = typeof(GraphicsStrings))]
    public IProperty<BlendMode> BlendMode { get; } = Property.CreateAnimatable(Graphics.BlendMode.SrcIn);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.BlendMode(r.Brush, r.BlendMode);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.Contains("Color"))
        {
            Color color = context.GetValue<Color>("Color");
            Brush.CurrentValue = new SolidColorBrush(color);
        }
    }
}
