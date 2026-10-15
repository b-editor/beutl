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
    /// All graphics stages (vertex and fragment).
    /// </summary>
    AllGraphics = Vertex | Fragment,

    /// <summary>
    /// All stages.
    /// </summary>
    All = AllGraphics,
}
