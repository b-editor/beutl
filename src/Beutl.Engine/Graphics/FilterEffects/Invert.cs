using System.ComponentModel.DataAnnotations;
using System.Reactive;

namespace Beutl.Graphics.Effects;

public sealed class Invert : FilterEffect
{
    public static readonly CoreProperty<float> AmountProperty;
    private float _amount = 100;

    static Invert()
    {
        AmountProperty = ConfigureProperty<float, Invert>(nameof(Amount))
            .Accessor(o => o.Amount, (o, v) => o.Amount = v)
            .DefaultValue(100)
            .Register();

        AffectsRender<Invert>(AmountProperty);
    }

    [Range(0, 100)]
    public float Amount
    {
        get => _amount;
        set => SetAndRaise(AmountProperty, ref _amount, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.LookupTable(
            Unit.Default,
            _amount / 100,
            (Unit _, byte[] array) => LookupTable.Invert(array));
    }
}
