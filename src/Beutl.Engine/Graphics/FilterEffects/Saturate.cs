namespace Beutl.Graphics.Effects;

public sealed class Saturate : FilterEffect
{
    public static readonly CoreProperty<float> AmountProperty;
    private float _amount = 100F;

    static Saturate()
    {
        AmountProperty = ConfigureProperty<float, Saturate>(nameof(Amount))
            .Accessor(o => o.Amount, (o, v) => o.Amount = v)
            .DefaultValue(100F)
            .Register();

        AffectsRender<Saturate>(AmountProperty);
    }

    public float Amount
    {
        get => _amount;
        set => SetAndRaise(AmountProperty, ref _amount, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Saturate(Amount / 100F);
    }
}
