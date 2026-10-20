using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Media;

public enum TextAlignment
{
    [Display(Name = nameof(GraphicsStrings.TextAlignment_Left), ResourceType = typeof(GraphicsStrings))]
    Start,

    [Display(Name = nameof(GraphicsStrings.TextAlignment_Center), ResourceType = typeof(GraphicsStrings))]
    Center,

    [Display(Name = nameof(GraphicsStrings.TextAlignment_Right), ResourceType = typeof(GraphicsStrings))]
    End,
}
