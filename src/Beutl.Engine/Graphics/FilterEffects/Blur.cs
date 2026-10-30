using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Blur), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Blur : FilterEffect
{
    public Blur()
    {
        ScanProperties<Blur>();
    }

    [Display(Name = nameof(GraphicsStrings.Sigma), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public IExpressionProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.Blur(Sigma);
        }
    }
}
