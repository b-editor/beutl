using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Dilate), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Dilate : FilterEffect
{
    public Dilate()
    {
        ScanProperties<Dilate>();
    }

    [Display(Name = nameof(GraphicsStrings.Dilate_RadiusX), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> RadiusX { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Dilate_RadiusY), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> RadiusY { get; } = Property.CreateAnimatable<float>();

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.Dilate(RadiusX, RadiusY);
        }
    }
}
