using System.ComponentModel.DataAnnotations;

using Beutl.Language;

namespace Beutl.Media;

public enum StrokeJoin
{
    [Display(Name = nameof(GraphicsStrings.StrokeJoin_Miter), ResourceType = typeof(GraphicsStrings))]
    Miter = 0,

    [Display(Name = nameof(GraphicsStrings.StrokeJoin_Round), ResourceType = typeof(GraphicsStrings))]
    Round = 1,

    [Display(Name = nameof(GraphicsStrings.StrokeJoin_Bevel), ResourceType = typeof(GraphicsStrings))]
    Bevel = 2
}
