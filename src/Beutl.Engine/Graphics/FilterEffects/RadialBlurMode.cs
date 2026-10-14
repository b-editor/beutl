using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum RadialBlurMode
{
    [Display(Name = nameof(GraphicsStrings.RadialBlurMode_Zoom), ResourceType = typeof(GraphicsStrings))]
    Zoom,

    [Display(Name = nameof(GraphicsStrings.RadialBlurMode_Spin), ResourceType = typeof(GraphicsStrings))]
    Spin,
}
