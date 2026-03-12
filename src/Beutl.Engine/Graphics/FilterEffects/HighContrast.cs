using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.HighContrast), ResourceType = typeof(GraphicsStrings))]
public sealed partial class HighContrast : FilterEffect
{
    public HighContrast()
    {
        ScanProperties<HighContrast>();
    }

    [Display(Name = nameof(GraphicsStrings.HighContrast_Grayscale), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> Grayscale { get; } = Property.CreateAnimatable(false);

    [Display(Name = nameof(GraphicsStrings.HighContrast_InvertStyle), ResourceType = typeof(GraphicsStrings))]
    public IProperty<HighContrastInvertStyle> InvertStyle { get; } = Property.CreateAnimatable(HighContrastInvertStyle.NoInvert);

    [Display(Name = nameof(GraphicsStrings.HighContrast_Contrast), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Contrast { get; } = Property.CreateAnimatable<float>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.HighContrast(r.Grayscale, r.InvertStyle, r.Contrast / 100f);
    }
}
