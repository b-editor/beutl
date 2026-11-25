using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum RangeAnimatorShape
{
    [Display(Name = nameof(GraphicsStrings.RangeAnimatorShape_Square), ResourceType = typeof(GraphicsStrings))]
    Square = 0,

    [Display(Name = nameof(GraphicsStrings.RangeAnimatorShape_RampUp), ResourceType = typeof(GraphicsStrings))]
    RampUp,

    [Display(Name = nameof(GraphicsStrings.RangeAnimatorShape_RampDown), ResourceType = typeof(GraphicsStrings))]
    RampDown,

    [Display(Name = nameof(GraphicsStrings.RangeAnimatorShape_Triangle), ResourceType = typeof(GraphicsStrings))]
    Triangle,

    [Display(Name = nameof(GraphicsStrings.RangeAnimatorShape_Round), ResourceType = typeof(GraphicsStrings))]
    Round,

    [Display(Name = nameof(GraphicsStrings.RangeAnimatorShape_Smooth), ResourceType = typeof(GraphicsStrings))]
    Smooth,
}
