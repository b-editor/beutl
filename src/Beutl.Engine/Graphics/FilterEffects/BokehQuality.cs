using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum BokehQuality
{
    [Display(Name = nameof(GraphicsStrings.BokehQuality_Low), ResourceType = typeof(GraphicsStrings))]
    Low,

    [Display(Name = nameof(GraphicsStrings.BokehQuality_Medium), ResourceType = typeof(GraphicsStrings))]
    Medium,

    [Display(Name = nameof(GraphicsStrings.BokehQuality_High), ResourceType = typeof(GraphicsStrings))]
    High,
}
