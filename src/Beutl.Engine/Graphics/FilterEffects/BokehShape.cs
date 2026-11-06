using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum BokehShape
{
    [Display(Name = nameof(GraphicsStrings.BokehShape_Circle), ResourceType = typeof(GraphicsStrings))]
    Circle,

    [Display(Name = nameof(GraphicsStrings.BokehShape_Polygon), ResourceType = typeof(GraphicsStrings))]
    Polygon,
}
