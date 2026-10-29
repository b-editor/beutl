using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Saturate), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Saturate : FilterEffect
{
    public Saturate()
    {
        ScanProperties<Saturate>();
    }

    [Display(Name = nameof(GraphicsStrings.Amount), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.Saturate(Amount / 100f);
        }
    }
}
