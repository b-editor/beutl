using System.ComponentModel.DataAnnotations;

using Beutl.Language;

namespace Beutl.Media;

public enum StrokeCap
{
    [Display(Name = nameof(GraphicsStrings.StrokeCap_Flat), ResourceType = typeof(GraphicsStrings))]
    Flat = 0,

    [Display(Name = nameof(GraphicsStrings.StrokeCap_Round), ResourceType = typeof(GraphicsStrings))]
    Round = 1,

    [Display(Name = nameof(GraphicsStrings.StrokeCap_Square), ResourceType = typeof(GraphicsStrings))]
    Square = 2
}
