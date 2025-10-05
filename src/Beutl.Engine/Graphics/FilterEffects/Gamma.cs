using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed partial class Gamma : FilterEffect
{
    public Gamma()
    {
        ScanProperties<Gamma>();
    }

    [Display(Name = nameof(Strings.Amount), ResourceType = typeof(Strings))]
    [Range(1, 300)]
    public IProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(Strings.Strength), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public IProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    public override void ApplyTo(FilterEffectContext context)
    {
        float amount = Amount.CurrentValue / 100f;

        context.LookupTable(
            amount,
            Strength.CurrentValue / 100,
            static (float data, byte[] array) => LookupTable.Gamma(array, data));
    }
}
