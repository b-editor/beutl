using System.ComponentModel.DataAnnotations;

using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum MeshWarpInterpolation
{
    [Display(Name = nameof(Strings.Linear), ResourceType = typeof(Strings))]
    Linear,

    [Display(Name = nameof(GraphicsStrings.MeshWarp_Smooth), ResourceType = typeof(GraphicsStrings))]
    Smooth,
}
