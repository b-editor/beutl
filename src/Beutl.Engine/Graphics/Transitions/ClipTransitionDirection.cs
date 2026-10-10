using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

public enum ClipTransitionDirection
{
    [Display(Name = nameof(GraphicsStrings.ClipTransitionDirection_LeftToRight), ResourceType = typeof(GraphicsStrings))]
    LeftToRight,

    [Display(Name = nameof(GraphicsStrings.ClipTransitionDirection_RightToLeft), ResourceType = typeof(GraphicsStrings))]
    RightToLeft,

    [Display(Name = nameof(GraphicsStrings.ClipTransitionDirection_TopToBottom), ResourceType = typeof(GraphicsStrings))]
    TopToBottom,

    [Display(Name = nameof(GraphicsStrings.ClipTransitionDirection_BottomToTop), ResourceType = typeof(GraphicsStrings))]
    BottomToTop,
}
