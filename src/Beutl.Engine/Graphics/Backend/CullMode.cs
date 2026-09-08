namespace Beutl.Graphics.Backend;

/// <summary>
/// Specifies which faces should be culled.
/// </summary>
public enum CullMode
{
    /// <summary>
    /// No culling.
    /// </summary>
    None = 0,

    /// <summary>
    /// Cull front-facing triangles.
    /// </summary>
    Front = 1,

    /// <summary>
    /// Cull back-facing triangles.
    /// </summary>
    Back = 2
}
