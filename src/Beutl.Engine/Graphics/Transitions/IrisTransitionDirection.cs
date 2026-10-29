using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

public enum IrisTransitionDirection
{
    [Display(Name = nameof(GraphicsStrings.IrisTransitionDirection_In), ResourceType = typeof(GraphicsStrings))]
    In,

    [Display(Name = nameof(GraphicsStrings.IrisTransitionDirection_Out), ResourceType = typeof(GraphicsStrings))]
    Out,
}
