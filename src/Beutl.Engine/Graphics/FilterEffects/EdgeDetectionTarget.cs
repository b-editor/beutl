using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum EdgeDetectionTarget
{
    /// <summary>Lines inside the picture, where it gets lighter or darker. Its transparent parts count for nothing.</summary>
    [Display(Name = nameof(GraphicsStrings.EdgeDetectionTarget_Luminance), ResourceType = typeof(GraphicsStrings))]
    Luminance,

    /// <summary>Lines along the picture's outline, where it fades to transparent.</summary>
    [Display(Name = nameof(GraphicsStrings.EdgeDetectionTarget_Alpha), ResourceType = typeof(GraphicsStrings))]
    Alpha,

    /// <summary>
    /// Lines inside the picture, where its color changes in lightness, hue or saturation. Its transparent parts count for
    /// nothing.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.EdgeDetectionTarget_Color), ResourceType = typeof(GraphicsStrings))]
    Color,
}
