using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Dilate), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Dilate : FilterEffect
{
    public Dilate()
    {
        ScanProperties<Dilate>();
    }

    [Display(Name = nameof(GraphicsStrings.Dilate_RadiusX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> RadiusX { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Dilate_RadiusY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> RadiusY { get; } = Property.CreateAnimatable<float>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Dilate(r.RadiusX, r.RadiusY);
    }
}
