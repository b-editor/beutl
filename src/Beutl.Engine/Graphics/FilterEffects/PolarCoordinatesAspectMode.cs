using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum PolarCoordinatesAspectMode
{
    [Display(Name = nameof(GraphicsStrings.PolarCoordinatesAspectMode_Stretch), ResourceType = typeof(GraphicsStrings))]
    Stretch,

    [Display(Name = nameof(GraphicsStrings.PolarCoordinatesAspectMode_Contain), ResourceType = typeof(GraphicsStrings))]
    Contain,

    [Display(Name = nameof(GraphicsStrings.PolarCoordinatesAspectMode_Cover), ResourceType = typeof(GraphicsStrings))]
    Cover,
}
