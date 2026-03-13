using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Erode), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Erode : FilterEffect
{
    public Erode()
    {
        ScanProperties<Erode>();
    }

    [Display(Name = nameof(GraphicsStrings.Erode_RadiusX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> RadiusX { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Erode_RadiusY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> RadiusY { get; } = Property.CreateAnimatable<float>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Erode(r.RadiusX, r.RadiusY);
    }
}
