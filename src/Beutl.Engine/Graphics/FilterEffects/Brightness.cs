using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Brightness), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Brightness : FilterEffect
{
    public Brightness()
    {
        ScanProperties<Brightness>();
    }

    [Display(Name = nameof(GraphicsStrings.Amount), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            float amount = Amount / 100f;

            context.Brightness(amount);
        }
    }
}
