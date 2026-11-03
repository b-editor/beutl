using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// The color space a gradient mixes its colors in.
/// </summary>
public enum GradientInterpolationSpace
{
    /// <summary>Mixes light: the middle of black and white is 188 in 8-bit sRGB.</summary>
    [Display(Name = nameof(GraphicsStrings.GradientInterpolationSpace_LinearSrgb), ResourceType = typeof(GraphicsStrings))]
    LinearSrgb,

    /// <summary>Mixes the stored values, as CSS does by default: the middle of black and white is 128.</summary>
    [Display(Name = nameof(GraphicsStrings.GradientInterpolationSpace_Srgb), ResourceType = typeof(GraphicsStrings))]
    Srgb,

    /// <summary>Mixes evenly to the eye: lightness changes steadily and colors in between stay clean.</summary>
    [Display(Name = nameof(GraphicsStrings.GradientInterpolationSpace_Oklab), ResourceType = typeof(GraphicsStrings))]
    Oklab,
}
