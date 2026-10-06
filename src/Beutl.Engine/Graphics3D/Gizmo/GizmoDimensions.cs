namespace Beutl.Graphics3D.Gizmo;

/// <summary>
/// The gizmo lengths that <see cref="GizmoMesh"/> draws and <see cref="GizmoHitTester"/> tests against, in the
/// gizmo's unit-sized space.
/// </summary>
internal static class GizmoDimensions
{
    public const float ArrowLength = 1.0f;

    public const float RotateRingRadius = 0.8f;

    public const float ScaleLineLength = 0.8f;

    // The translate-mode plane indicators: distance from the center along both axes, and side length.
    public const float PlaneOffset = 0.0f;

    public const float PlaneSize = 0.2f;
}
