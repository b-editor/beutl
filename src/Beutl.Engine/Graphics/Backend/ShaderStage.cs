using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Specifies the shader stage(s).
/// </summary>
[Flags]
public enum ShaderStage
{
    /// <summary>
    /// No shader stage.
    /// </summary>
    None = 0,

    /// <summary>
    /// Vertex shader stage.
    /// </summary>
    Vertex = 1 << 0,

    /// <summary>
    /// Fragment (pixel) shader stage.
    /// </summary>
    Fragment = 1 << 1,

    /// <summary>
    /// Compute shader stage.
    /// </summary>
    Compute = 1 << 2,

    /// <summary>
    /// Geometry shader stage.
    /// </summary>
    Geometry = 1 << 3,

    /// <summary>
    /// Tessellation control shader stage.
    /// </summary>
    TessellationControl = 1 << 4,

    /// <summary>
    /// Tessellation evaluation shader stage.
    /// </summary>
    TessellationEvaluation = 1 << 5,

    /// <summary>
    /// All graphics stages (vertex, fragment, geometry, tessellation).
    /// </summary>
    AllGraphics = Vertex | Fragment | Geometry | TessellationControl | TessellationEvaluation,

    /// <summary>
    /// All stages.
    /// </summary>
    All = AllGraphics | Compute,
}
