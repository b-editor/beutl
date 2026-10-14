using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>Describes how text is aligned along a line's writing direction.</summary>
public enum TextAlignment
{
    /// <summary>Aligns text to the leading edge of the line.</summary>
    [Display(Name = nameof(GraphicsStrings.TextAlignment_Left), ResourceType = typeof(GraphicsStrings))]
    Start,

    /// <summary>Centers text within the line.</summary>
    [Display(Name = nameof(GraphicsStrings.TextAlignment_Center), ResourceType = typeof(GraphicsStrings))]
    Center,

    /// <summary>Aligns text to the trailing edge of the line.</summary>
    [Display(Name = nameof(GraphicsStrings.TextAlignment_Right), ResourceType = typeof(GraphicsStrings))]
    End,
}
