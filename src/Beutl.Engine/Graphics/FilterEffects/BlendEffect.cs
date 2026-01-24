using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.BlendEffect), ResourceType = typeof(Strings))]
public sealed partial class BlendEffect : FilterEffect
{
    public BlendEffect()
    {
        ScanProperties<BlendEffect>();
        Brush.CurrentValue = new SolidColorBrush(Colors.White);
    }

    [Display(Name = nameof(Strings.Brush), ResourceType = typeof(Strings))]
    public IProperty<Brush?> Brush { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(Strings.BlendMode), ResourceType = typeof(Strings))]
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
