using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum TextAnimationUnit
{
    [Display(Name = nameof(GraphicsStrings.TextAnimationUnit_Characters), ResourceType = typeof(GraphicsStrings))]
    Characters = 0,

    [Display(Name = nameof(GraphicsStrings.TextAnimationUnit_Words), ResourceType = typeof(GraphicsStrings))]
    Words,

    [Display(Name = nameof(GraphicsStrings.TextAnimationUnit_Lines), ResourceType = typeof(GraphicsStrings))]
    Lines,
}
