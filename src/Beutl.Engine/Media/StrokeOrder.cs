using System.ComponentModel.DataAnnotations;

using Beutl.Language;

namespace Beutl.Media;

/// <summary>Whether a stroke is drawn over or under the fill it outlines.</summary>
public enum StrokeOrder
{
    [Display(Name = nameof(GraphicsStrings.StrokeOrder_AboveFill), ResourceType = typeof(GraphicsStrings))]
    AboveFill = 0,

    [Display(Name = nameof(GraphicsStrings.StrokeOrder_BelowFill), ResourceType = typeof(GraphicsStrings))]
    BelowFill = 1
}
