using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed partial class Saturate : FilterEffect
{
    public Saturate()
    {
        ScanProperties<Saturate>();
    }

    [Display(Name = nameof(Strings.Amount), ResourceType = typeof(Strings))]
    public IProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Saturate(Amount.CurrentValue / 100f);
    }
}
