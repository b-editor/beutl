namespace Beutl.Graphics.Backend;

/// <summary>
/// Specifies the winding order for front-facing triangles.
/// </summary>
public enum FrontFace
{
    /// <summary>
    /// Triangles with counter-clockwise winding are front-facing.
    /// </summary>
    CounterClockwise = 0,

    /// <summary>
    /// Triangles with clockwise winding are front-facing.
    /// </summary>
    Clockwise = 1
}
