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
    public IProperty<HighContrastInvertStyle> InvertStyle { get; } = Property.CreateAnimatable(HighContrastInvertStyle.NoInvert);

    [Display(Name = nameof(Strings.Contrast), ResourceType = typeof(Strings))]
    public IProperty<float> Contrast { get; } = Property.CreateAnimatable<float>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.HighContrast(r.Grayscale, r.InvertStyle, r.Contrast / 100f);
    }
}
