using System.ComponentModel.DataAnnotations;

using SkiaSharp;

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

    [Range(0, 100)]
    public float Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }

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
        context.AppendSKColorFilter((threshold, strength: (_strength / 100)), (data, _) =>
        {
            var lut = new LookupTable();
            Span<float> span = lut.AsSpan();

            for (int i = data.threshold; i < 256; i++)
            {
                span[i] = 1;
            }

            byte[] array = lut.ToByteArray(data.strength, 0);

            return SKColorFilter.CreateTable(array);
        });
    }
}
