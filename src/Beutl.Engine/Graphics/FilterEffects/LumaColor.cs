using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.LumaColor), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LumaColor : FilterEffect
{
    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.LumaColor();
        }
    }
}
