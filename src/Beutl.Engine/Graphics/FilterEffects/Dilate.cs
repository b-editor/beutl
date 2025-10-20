using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed partial class Dilate : FilterEffect
{
    public Dilate()
    {
        ScanProperties<Dilate>();
    }

    [Display(Name = nameof(Strings.RadiusX), ResourceType = typeof(Strings))]
    public IProperty<float> RadiusX { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.RadiusY), ResourceType = typeof(Strings))]
    public IProperty<float> RadiusY { get; } = Property.CreateAnimatable<float>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Dilate(r.RadiusX, r.RadiusY);
    }
}
