using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics3D;

/// <summary>The multisample anti-aliasing requested for a 3D scene.</summary>
public enum AntiAliasingMode
{
    [Display(Name = nameof(GraphicsStrings.AntiAliasingMode_Off), ResourceType = typeof(GraphicsStrings))]
    Off,

    [Display(Name = nameof(GraphicsStrings.AntiAliasingMode_Msaa2x), ResourceType = typeof(GraphicsStrings))]
    Msaa2x,

    [Display(Name = nameof(GraphicsStrings.AntiAliasingMode_Msaa4x), ResourceType = typeof(GraphicsStrings))]
    Msaa4x,

    [Display(Name = nameof(GraphicsStrings.AntiAliasingMode_Msaa8x), ResourceType = typeof(GraphicsStrings))]
    Msaa8x,
}
