using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

public enum WipeTransitionShape
{
    [Display(Name = nameof(GraphicsStrings.WipeTransitionShape_Linear), ResourceType = typeof(GraphicsStrings))]
    Linear,

    [Display(Name = nameof(GraphicsStrings.WipeTransitionShape_Box), ResourceType = typeof(GraphicsStrings))]
    Box,

    [Display(Name = nameof(GraphicsStrings.WipeTransitionShape_Diamond), ResourceType = typeof(GraphicsStrings))]
    Diamond,

    [Display(Name = nameof(GraphicsStrings.WipeTransitionShape_Clock), ResourceType = typeof(GraphicsStrings))]
    Clock,
}
