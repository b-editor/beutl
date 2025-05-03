using System.ComponentModel.DataAnnotations;

using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed class Threshold : FilterEffect
{
    public static readonly CoreProperty<float> ValueProperty;
    public static readonly CoreProperty<float> StrengthProperty;
    private float _value = 50;
    private float _strength = 100;

    static Threshold()
    {
        ValueProperty = ConfigureProperty<float, Threshold>(nameof(Value))
            .Accessor(o => o.Value, (o, v) => o.Value = v)
            .DefaultValue(50)
            .Register();

        StrengthProperty = ConfigureProperty<float, Threshold>(nameof(Strength))
            .Accessor(o => o.Strength, (o, v) => o.Strength = v)
            .DefaultValue(100)
            .Register();

        AffectsRender<Threshold>(ValueProperty, StrengthProperty);
    }

    [Display(Name = nameof(Strings.Amount), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public float Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }

    [Display(Name = nameof(Strings.Strength), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public float Strength
    {
        get => _strength;
        set => SetAndRaise(StrengthProperty, ref _strength, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        int threshold = Math.Clamp((int)(_value / 100f * 255), 0, 255);

        context.HighContrast(true, HighContrastInvertStyle.NoInvert, 0);

        context.LookupTable(
            threshold,
            _strength / 100,
            (int data, byte[] array) =>
            {
                for (int i = data; i < array.Length; i++)
                {
                    array[i] = 255;
                }
            });
    }
}
