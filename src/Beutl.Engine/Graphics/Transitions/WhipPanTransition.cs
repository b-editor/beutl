using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

/// <summary>Sweeps the frame with directional motion blur and mirrored edges.</summary>
[Display(Name = nameof(GraphicsStrings.WhipPanTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class WhipPanTransition : ClipTransition
{
    public WhipPanTransition()
    {
        ScanProperties<WhipPanTransition>();
    }

    [Range(0, 100)]
    [Display(Name = nameof(GraphicsStrings.ClipTransition_Strength), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Strength { get; } = Property.CreateExpression(75f);

    [Display(Name = nameof(GraphicsStrings.Direction), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<ClipTransitionDirection> Direction { get; } = Property.CreateExpression(ClipTransitionDirection.LeftToRight);

    public new partial class Resource
    {
        public override void Draw(ClipTransitionContext context)
        {
            StylizedTransitionRenderer.Draw(context, TransitionStyle.WhipPan, Strength, Direction);
        }
    }
}
