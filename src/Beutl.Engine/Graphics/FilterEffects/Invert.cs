using System.ComponentModel.DataAnnotations;
using System.Reactive;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed class Invert : FilterEffect
{
    public Invert()
    {
        ScanProperties<Invert>();
    }

    [Range(0, 100)]
    [Display(Name = nameof(Strings.Amount), ResourceType = typeof(Strings))]
    public IProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(Strings.ExcludeAlphaChannel), ResourceType = typeof(Strings))]
    public IProperty<bool> ExcludeAlphaChannel { get; } = Property.CreateAnimatable(true);

    public override void ApplyTo(FilterEffectContext context)
    {
        if (ExcludeAlphaChannel.CurrentValue)
        {
            context.LookupTable(
                Unit.Default,
                Amount.CurrentValue / 100,
                static (Unit _, (byte[] A, byte[] R, byte[] G, byte[] B) array) =>
                {
                    LookupTable.Linear(array.A);
                    LookupTable.Invert(array.R);
                    LookupTable.Invert(array.G);
                    LookupTable.Invert(array.B);
                });
        }
        else
        {
            context.LookupTable(
                Unit.Default,
                Amount.CurrentValue / 100,
                static (Unit _, byte[] array) => LookupTable.Invert(array));
        }
    }
}
