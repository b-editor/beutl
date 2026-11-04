using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics3D.Materials;

/// <summary>
/// How a material treats its alpha: the three modes of glTF's <c>alphaMode</c>, and a choice left to the material.
/// </summary>
public enum AlphaMode
{
    /// <summary>The material decides from its color and opacity.</summary>
    [Display(Name = nameof(GraphicsStrings.AlphaMode_Auto), ResourceType = typeof(GraphicsStrings))]
    Auto,

    /// <summary>Alpha is ignored. Writes depth.</summary>
    [Display(Name = nameof(GraphicsStrings.AlphaMode_Opaque), ResourceType = typeof(GraphicsStrings))]
    Opaque,

    /// <summary>A pixel below the cutoff is dropped and the rest are opaque. Writes depth.</summary>
    [Display(Name = nameof(GraphicsStrings.AlphaMode_Mask), ResourceType = typeof(GraphicsStrings))]
    Mask,

    /// <summary>Blended with what is behind it. Writes no depth.</summary>
    [Display(Name = nameof(GraphicsStrings.AlphaMode_Blend), ResourceType = typeof(GraphicsStrings))]
    Blend,
}
