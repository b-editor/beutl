using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed class Threshold : FilterEffect
{
    public static readonly CoreProperty<float> ValueProperty;
    public static readonly CoreProperty<float> SmoothnessProperty;
    public static readonly CoreProperty<float> StrengthProperty;
    private float _value = 50;
    private float _smoothness = 0;
    private float _strength = 100;

    static Threshold()
    {
        ValueProperty = ConfigureProperty<float, Threshold>(nameof(Value))
            .Accessor(o => o.Value, (o, v) => o.Value = v)
            .DefaultValue(50)
            .Register();

        SmoothnessProperty = ConfigureProperty<float, Threshold>(nameof(Smoothness))
            .Accessor(o => o.Smoothness, (o, v) => o.Smoothness = v)
            .DefaultValue(0)
            .Register();

        StrengthProperty = ConfigureProperty<float, Threshold>(nameof(Strength))
            .Accessor(o => o.Strength, (o, v) => o.Strength = v)
            .DefaultValue(100)
            .Register();

        AffectsRender<Threshold>(ValueProperty, SmoothnessProperty, StrengthProperty);
    }

    [Display(Name = nameof(Strings.Amount), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public float Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }

    [Display(Name = nameof(Strings.Smoothing), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public float Smoothness
    {
        get => _smoothness;
        set => SetAndRaise(SmoothnessProperty, ref _smoothness, value);
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
        context.HighContrast(true, HighContrastInvertStyle.NoInvert, 0);

        context.LookupTable(
            (_value / 100, _smoothness / 100),
            _strength / 100,
            ((float threshold, float smoothness) d, byte[] array) =>
            {
                float lower = d.threshold * 255.0f - (d.smoothness * 255.0f) * 0.5f;
                float upper = d.threshold * 255.0f + (d.smoothness * 255.0f) * 0.5f;

                for (int i = 0; i < 256; i++)
                {
                    float value = i;

                    // smoothstep的な補間
                    float t = (value - lower) / (upper - lower);
                    t = Math.Clamp(t, 0f, 1f); // [0,1]に制限
                    t = t * t * (3f - 2f * t); // smoothstep補間

                    array[i] = (byte)(t * 255f);
                }
            });
    }
}
