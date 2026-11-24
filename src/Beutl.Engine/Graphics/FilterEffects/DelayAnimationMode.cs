using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum DelayAnimationMode
{
    [Display(Name = nameof(GraphicsStrings.DelayAnimationMode_PerStep), ResourceType = typeof(GraphicsStrings))]
    PerStep = 0,

    [Display(Name = nameof(GraphicsStrings.DelayAnimationMode_Total), ResourceType = typeof(GraphicsStrings))]
    Total,
}
