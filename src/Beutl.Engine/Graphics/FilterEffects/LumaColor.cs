using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.LumaColor), ResourceType = typeof(Strings))]
public sealed partial class LumaColor : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        context.LumaColor();
    }
}
