using System.ComponentModel.DataAnnotations;

using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed class Brightness : FilterEffect
{
    public static readonly CoreProperty<float> AmountProperty;
    private float _amount = 100;

    static Brightness()
    {
        AmountProperty = ConfigureProperty<float, Brightness>(nameof(Amount))
            .Accessor(o => o.Amount, (o, v) => o.Amount = v)
            .DefaultValue(100)
            .Register();

        AffectsRender<Brightness>(AmountProperty);
    }

    [Display(Name = nameof(Strings.Amount), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public float Amount
    {
        get => _amount;
        set => SetAndRaise(AmountProperty, ref _amount, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        float amount = _amount / 100f;

        context.Brightness(amount);
    }
}
