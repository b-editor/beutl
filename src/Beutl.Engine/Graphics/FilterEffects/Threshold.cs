using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed partial class Threshold : FilterEffect
{
    public Threshold()
    {
        ScanProperties<Threshold>();
    }

    [Display(Name = nameof(Strings.Amount), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public IProperty<float> Value { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(Strings.Smoothing), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public IProperty<float> Smoothness { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(Strings.Strength), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public IProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    public override void ApplyTo(FilterEffectContext context)
    {
        context.HighContrast(true, HighContrastInvertStyle.NoInvert, 0);

        context.LookupTable(
            (Value.CurrentValue / 100, Smoothness.CurrentValue / 100),
            Strength.CurrentValue / 100,
            static ((float threshold, float smoothness) data, byte[] array) =>
            {
                float lower = data.threshold * 255.0f - (data.smoothness * 255.0f) * 0.5f;
                float upper = data.threshold * 255.0f + (data.smoothness * 255.0f) * 0.5f;

                for (int i = 0; i < 256; i++)
                {
                    float value = i;

                    float t = (value - lower) / (upper - lower);
                    t = Math.Clamp(t, 0f, 1f);
                    t = t * t * (3f - 2f * t);

                    array[i] = (byte)(t * 255f);
                }
            });
    }
}
