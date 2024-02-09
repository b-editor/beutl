using System.ComponentModel.DataAnnotations;

using Beutl.Language;

namespace Beutl.Media;

public enum StrokeJoin
{
    [Display(Name = nameof(Strings.StrokeJoin_Miter), ResourceType = typeof(Strings))]
    Miter = 0,

    [Display(Name = nameof(Strings.StrokeJoin_Round), ResourceType = typeof(Strings))]
    Round = 1,

    [Display(Name = nameof(Strings.StrokeJoin_Bevel), ResourceType = typeof(Strings))]
    Bevel = 2
}
