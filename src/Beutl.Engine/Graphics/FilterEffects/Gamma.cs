using System.ComponentModel.DataAnnotations;

namespace Beutl.Graphics.Effects;

public sealed class Gamma : FilterEffect
{
    public static readonly CoreProperty<float> AmountProperty;
    public static readonly CoreProperty<float> StrengthProperty;
    private float _amount = 100;
    private float _strength = 100;

    static Gamma()
    {
        AmountProperty = ConfigureProperty<float, Gamma>(nameof(Amount))
            .Accessor(o => o.Amount, (o, v) => o.Amount = v)
            .DefaultValue(100)
            .Register();

        StrengthProperty = ConfigureProperty<float, Gamma>(nameof(Strength))
            .Accessor(o => o.Strength, (o, v) => o.Strength = v)
            .DefaultValue(100)
            .Register();

        AffectsRender<Gamma>(AmountProperty, StrengthProperty);
    }

    [Range(1, 300)]
    public float Amount
    {
        get => _amount;
        set => SetAndRaise(AmountProperty, ref _amount, value);
    }

    [Range(0, 100)]
    public float Strength
    {
        get => _strength;
        set => SetAndRaise(StrengthProperty, ref _strength, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        float amount = _amount / 100f;

        context.LookupTable(
            amount,
            LookupTable.Gamma,
            _strength / 100);
    }
}
