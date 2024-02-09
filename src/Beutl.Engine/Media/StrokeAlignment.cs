using System.ComponentModel.DataAnnotations;

using Beutl.Language;

namespace Beutl.Media;

public enum StrokeAlignment
{
    [Display(Name = nameof(Strings.StrokeAlignment_Center), ResourceType = typeof(Strings))]
    Center = 0,

    [Display(Name = nameof(Strings.StrokeAlignment_Inside), ResourceType = typeof(Strings))]
    Inside = 1,

    [Display(Name = nameof(Strings.StrokeAlignment_Outside), ResourceType = typeof(Strings))]
    Outside = 2
}
