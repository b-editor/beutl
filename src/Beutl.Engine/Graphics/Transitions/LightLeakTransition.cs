using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

/// <summary>Passes a warm light leak and a narrow flare across the cut.</summary>
[Display(Name = nameof(GraphicsStrings.LightLeakTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LightLeakTransition : ClipTransition
{
    public LightLeakTransition()
    {
        ScanProperties<LightLeakTransition>();
    }

    [Range(0, 100)]
    [Display(Name = nameof(GraphicsStrings.ClipTransition_Strength), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Strength { get; } = Property.CreateExpression(70f);

    public new partial class Resource
    {
        public override void Draw(ClipTransitionContext context)
        {
            StylizedTransitionRenderer.Draw(context, TransitionStyle.LightLeak, Strength);
        }
    }
}
