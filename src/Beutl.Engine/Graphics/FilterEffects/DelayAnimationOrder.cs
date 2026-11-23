using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum DelayAnimationOrder
{
    [Display(Name = nameof(GraphicsStrings.DelayAnimationOrder_Forward), ResourceType = typeof(GraphicsStrings))]
    Forward = 0,

    [Display(Name = nameof(GraphicsStrings.DelayAnimationOrder_Reverse), ResourceType = typeof(GraphicsStrings))]
    Reverse,

    [Display(Name = nameof(GraphicsStrings.DelayAnimationOrder_FromCenter), ResourceType = typeof(GraphicsStrings))]
    FromCenter,

    [Display(Name = nameof(GraphicsStrings.DelayAnimationOrder_Random), ResourceType = typeof(GraphicsStrings))]
    Random,

    [Display(Name = nameof(GraphicsStrings.DelayAnimationOrder_LeftToRight), ResourceType = typeof(GraphicsStrings))]
    LeftToRight,

    [Display(Name = nameof(GraphicsStrings.DelayAnimationOrder_RightToLeft), ResourceType = typeof(GraphicsStrings))]
    RightToLeft,

    [Display(Name = nameof(GraphicsStrings.DelayAnimationOrder_TopToBottom), ResourceType = typeof(GraphicsStrings))]
    TopToBottom,

    [Display(Name = nameof(GraphicsStrings.DelayAnimationOrder_BottomToTop), ResourceType = typeof(GraphicsStrings))]
    BottomToTop,
}
