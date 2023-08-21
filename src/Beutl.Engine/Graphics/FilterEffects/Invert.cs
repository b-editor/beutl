using System.ComponentModel.DataAnnotations;
using System.Reactive;

using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed class Invert : FilterEffect
{
    public static readonly CoreProperty<float> AmountProperty;
    public static readonly CoreProperty<bool> ExcludeAlphaChannelProperty;
    private float _amount = 100;
    private bool _excludeAlphaChannel = true;

    static Invert()
    {
        AmountProperty = ConfigureProperty<float, Invert>(nameof(Amount))
            .Accessor(o => o.Amount, (o, v) => o.Amount = v)
            .DefaultValue(100)
            .Register();

        ExcludeAlphaChannelProperty = ConfigureProperty<bool, Invert>(nameof(ExcludeAlphaChannel))
            .Accessor(o => o.ExcludeAlphaChannel, (o, v) => o.ExcludeAlphaChannel = v)
            .DefaultValue(true)
            .Register();

        AffectsRender<Invert>(AmountProperty, ExcludeAlphaChannelProperty);
    }

    [Range(0, 100)]
    [Display(Name = nameof(Strings.Amount), ResourceType = typeof(Strings))]
    public float Amount
    {
        get => _amount;
        set => SetAndRaise(AmountProperty, ref _amount, value);
    }
    
    [Display(Name = nameof(Strings.ExcludeAlphaChannel), ResourceType = typeof(Strings))]
    public bool ExcludeAlphaChannel
    {
        get => _excludeAlphaChannel;
        set => SetAndRaise(ExcludeAlphaChannelProperty, ref _excludeAlphaChannel, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        if (ExcludeAlphaChannel)
        {
            context.LookupTable(
                Unit.Default,
                _amount / 100,
                (Unit _, (byte[] A, byte[] R, byte[] G, byte[] B) array) =>
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
                _amount / 100,
                (Unit _, byte[] array) => LookupTable.Invert(array));
        }
    }
}
