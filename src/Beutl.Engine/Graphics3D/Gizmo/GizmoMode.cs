namespace Beutl.Graphics3D.Gizmo;

/// <summary>
/// Defines the mode for 3D gizmo visualization.
/// </summary>
public enum GizmoMode
{
    /// <summary>
    /// No gizmo displayed.
    /// </summary>
    None = 0,

    /// <summary>
    /// Translation gizmo with arrows along X, Y, Z axes.
    /// </summary>
    Translate = 1,

    /// <summary>
    /// Rotation gizmo with circular arcs for each axis.
    /// </summary>
    Rotate = 2,

    /// <summary>
    /// Scale gizmo with cubes at the end of each axis.
    /// </summary>
    Scale = 3
}
