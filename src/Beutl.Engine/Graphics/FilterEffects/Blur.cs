using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.Blur), ResourceType = typeof(Strings))]
public sealed partial class Blur : FilterEffect
{
    public Blur()
    {
        ScanProperties<Blur>();
    }

    [Display(Name = nameof(Strings.Sigma), ResourceType = typeof(Strings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public IProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Blur(r.Sigma);
    }
}
