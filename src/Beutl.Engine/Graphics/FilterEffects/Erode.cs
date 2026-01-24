using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.Erode), ResourceType = typeof(Strings))]
public sealed partial class Erode : FilterEffect
{
    public Erode()
    {
        ScanProperties<Erode>();
    }

    [Display(Name = nameof(Strings.RadiusX), ResourceType = typeof(Strings))]
    public IProperty<float> RadiusX { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.RadiusY), ResourceType = typeof(Strings))]
    public IProperty<float> RadiusY { get; } = Property.CreateAnimatable<float>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Erode(r.RadiusX, r.RadiusY);
    }
}
