using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

/// <summary>Accelerates into the next clip with a radial blur that peaks around the cut.</summary>
[Display(Name = nameof(GraphicsStrings.ZoomBlurTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class ZoomBlurTransition : ClipTransition
{
    public ZoomBlurTransition()
    {
        ScanProperties<ZoomBlurTransition>();
    }

    [Range(0, 100)]
    [Display(Name = nameof(GraphicsStrings.ClipTransition_Strength), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Strength { get; } = Property.CreateExpression(65f);

    public new partial class Resource
    {
        public override void Draw(ClipTransitionContext context)
        {
            StylizedTransitionRenderer.Draw(context, TransitionStyle.ZoomBlur, Strength);
        }
    }
}
