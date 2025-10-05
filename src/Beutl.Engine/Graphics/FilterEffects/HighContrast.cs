using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed partial class HighContrast : FilterEffect
{
    public HighContrast()
    {
        ScanProperties<HighContrast>();
    }

    [Display(Name = nameof(Strings.Grayscale), ResourceType = typeof(Strings))]
    public IProperty<bool> Grayscale { get; } = Property.CreateAnimatable(false);

    [Display(Name = nameof(Strings.InvertStyle), ResourceType = typeof(Strings))]
    public IProperty<HighContrastInvertStyle> InvertStyle { get; } = Property.CreateAnimatable(HighContrastInvertStyle.None);

    [Display(Name = nameof(Strings.Contrast), ResourceType = typeof(Strings))]
    public IProperty<float> Contrast { get; } = Property.CreateAnimatable<float>();

    public override void ApplyTo(FilterEffectContext context)
    {
        context.HighContrast(Grayscale.CurrentValue, InvertStyle.CurrentValue, Contrast.CurrentValue / 100f);
    }
}
