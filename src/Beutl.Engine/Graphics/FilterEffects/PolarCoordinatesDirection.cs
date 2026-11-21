using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum PolarCoordinatesDirection
{
    [Display(Name = nameof(GraphicsStrings.PolarCoordinatesDirection_RectangularToPolar), ResourceType = typeof(GraphicsStrings))]
    RectangularToPolar,

    [Display(Name = nameof(GraphicsStrings.PolarCoordinatesDirection_PolarToRectangular), ResourceType = typeof(GraphicsStrings))]
    PolarToRectangular,
}
