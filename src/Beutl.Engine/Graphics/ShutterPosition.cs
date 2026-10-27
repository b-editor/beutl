using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics;

/// <summary>The current frame's position within the exposure interval.</summary>
public enum ShutterPosition
{
    [Display(Name = nameof(GraphicsStrings.MotionBlur_Center), ResourceType = typeof(GraphicsStrings))]
    Center,

    [Display(Name = nameof(GraphicsStrings.MotionBlur_Start), ResourceType = typeof(GraphicsStrings))]
    Start,

    [Display(Name = nameof(GraphicsStrings.MotionBlur_End), ResourceType = typeof(GraphicsStrings))]
    End,
}
