using System.ComponentModel.DataAnnotations;

using Beutl.Language;

namespace Beutl.Media;

public enum StrokeCap
{
    [Display(Name = nameof(Strings.StrokeCap_Flat), ResourceType = typeof(Strings))]
    Flat = 0,

    [Display(Name = nameof(Strings.StrokeCap_Round), ResourceType = typeof(Strings))]
    Round = 1,

    [Display(Name = nameof(Strings.StrokeCap_Square), ResourceType = typeof(Strings))]
    Square = 2
}
