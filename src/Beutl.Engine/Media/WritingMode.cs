using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Media;

public enum WritingMode
{
    [Display(Name = nameof(GraphicsStrings.WritingMode_HorizontalTb), ResourceType = typeof(GraphicsStrings))]
    HorizontalTb,

    [Display(Name = nameof(GraphicsStrings.WritingMode_VerticalRl), ResourceType = typeof(GraphicsStrings))]
    VerticalRl,
}
