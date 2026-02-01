namespace Beutl.Graphics3D.Gizmo;

/// <summary>
/// Represents which axis of the gizmo is selected.
/// </summary>
public enum GizmoAxis
{
    /// <summary>
    /// No axis selected.
    /// </summary>
    None = 0,

    /// <summary>
    /// X axis (red).
    /// </summary>
    X = 1,

    /// <summary>
    /// Y axis (green).
    /// </summary>
    Y = 2,

    /// <summary>
    /// Z axis (blue).
    /// </summary>
    Z = 3,

    /// <summary>
    /// XY plane (red-green) - for translate mode.
    /// </summary>
    XY = 4,

    /// <summary>
    /// YZ plane (green-blue) - for translate mode.
    /// </summary>
    YZ = 5,

    /// <summary>
    /// ZX plane (blue-red) - for translate mode.
    /// </summary>
    ZX = 6,

    /// <summary>
    /// All axes - for uniform scale mode.
    /// </summary>
    All = 7
}
