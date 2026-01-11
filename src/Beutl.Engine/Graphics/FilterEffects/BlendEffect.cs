using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

public sealed partial class BlendEffect : FilterEffect
{
    public BlendEffect()
    {
        ScanProperties<BlendEffect>();
        Brush.CurrentValue = new SolidColorBrush(Colors.White);
    }

    public IProperty<Brush?> Brush { get; } = Property.Create<Brush?>();

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
