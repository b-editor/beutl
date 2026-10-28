using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

/// <summary>Breaks the cut into deterministic horizontal bands with separated RGB channels.</summary>
[Display(Name = nameof(GraphicsStrings.GlitchTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class GlitchTransition : ClipTransition
{
    public GlitchTransition()
    {
        ScanProperties<GlitchTransition>();
    }

    [Range(0, 100)]
    [Display(Name = nameof(GraphicsStrings.ClipTransition_Strength), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Strength { get; } = Property.CreateExpression(65f);

    public new partial class Resource
    {
        public override void Draw(ClipTransitionContext context)
        {
            StylizedTransitionRenderer.Draw(context, TransitionStyle.Glitch, Strength);
        }
    }
}
