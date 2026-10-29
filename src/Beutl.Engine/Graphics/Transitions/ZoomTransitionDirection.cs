using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

public enum ZoomTransitionDirection
{
    [Display(Name = nameof(GraphicsStrings.ZoomTransitionDirection_In), ResourceType = typeof(GraphicsStrings))]
    In,

    [Display(Name = nameof(GraphicsStrings.ZoomTransitionDirection_Out), ResourceType = typeof(GraphicsStrings))]
    Out,
}
