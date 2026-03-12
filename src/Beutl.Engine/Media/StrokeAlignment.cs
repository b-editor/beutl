using System.ComponentModel.DataAnnotations;

using Beutl.Language;

namespace Beutl.Media;

public enum StrokeAlignment
{
    [Display(Name = nameof(GraphicsStrings.StrokeAlignment_Center), ResourceType = typeof(GraphicsStrings))]
    Center = 0,

    [Display(Name = nameof(GraphicsStrings.StrokeAlignment_Inside), ResourceType = typeof(GraphicsStrings))]
    Inside = 1,

    [Display(Name = nameof(GraphicsStrings.StrokeAlignment_Outside), ResourceType = typeof(GraphicsStrings))]
    Outside = 2
}
