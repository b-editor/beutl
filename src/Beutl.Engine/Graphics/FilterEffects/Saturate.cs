using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Saturate), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Saturate : FilterEffect
{
    public Saturate()
    {
        ScanProperties<Saturate>();
    }

    [Display(Name = nameof(GraphicsStrings.Amount), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Saturate(r.Amount / 100f);
    }
}
