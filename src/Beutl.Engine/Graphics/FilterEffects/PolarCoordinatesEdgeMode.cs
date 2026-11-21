using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum PolarCoordinatesEdgeMode
{
    [Display(Name = nameof(GraphicsStrings.PolarCoordinatesEdgeMode_Transparent), ResourceType = typeof(GraphicsStrings))]
    Transparent,

    [Display(Name = nameof(GraphicsStrings.PolarCoordinatesEdgeMode_Clamp), ResourceType = typeof(GraphicsStrings))]
    Clamp,

    [Display(Name = nameof(GraphicsStrings.PolarCoordinatesEdgeMode_Repeat), ResourceType = typeof(GraphicsStrings))]
    Repeat,

    [Display(Name = nameof(GraphicsStrings.PolarCoordinatesEdgeMode_Mirror), ResourceType = typeof(GraphicsStrings))]
    Mirror,
}
