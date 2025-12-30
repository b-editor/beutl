using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed partial class Brightness : FilterEffect
{
    public Brightness()
    {
        ScanProperties<Brightness>();
    }

    [Display(Name = nameof(Strings.Amount), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        float amount = r.Amount / 100f;

        context.Brightness(amount);
    }
}
