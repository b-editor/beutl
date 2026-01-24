using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.HueRotate), ResourceType = typeof(Strings))]
public sealed partial class HueRotate : FilterEffect
{
    public HueRotate()
    {
        ScanProperties<HueRotate>();
    }

    [Display(Name = nameof(Strings.Angle), ResourceType = typeof(Strings))]
    public IProperty<float> Angle { get; } = Property.CreateAnimatable<float>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.HueRotate(r.Angle);
    }
}
