using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.HueRotate), ResourceType = typeof(GraphicsStrings))]
public sealed partial class HueRotate : FilterEffect
{
    public HueRotate()
    {
        ScanProperties<HueRotate>();
    }

    [Display(Name = nameof(GraphicsStrings.Angle), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Angle { get; } = Property.CreateAnimatable<float>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.HueRotate(r.Angle);
    }
}
