using System;
using System.Collections.Generic;
using System.Numerics;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Lighting;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics3D;

/// <summary>
/// Interface for 3D rendering abstraction.
/// Provides backend-agnostic 3D rendering capabilities.
/// </summary>
public interface IRenderer3D : IDisposable
{
    /// <summary>
    /// Gets the current render width.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the current render height.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Initializes the renderer with the specified dimensions.
    /// </summary>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    void Initialize(int width, int height);

    /// <summary>
    /// Resizes the renderer to the specified dimensions.
    /// </summary>
    /// <param name="width">The new render width.</param>
    /// <param name="height">The new render height.</param>
    void Resize(int width, int height);

    /// <summary>
    /// Renders the 3D scene.
    /// </summary>
    /// <param name="renderContext">The render context.</param>
    /// <param name="camera">The camera resource.</param>
    /// <param name="objects">The object resources to render.</param>
    /// <param name="lights">The lights in the scene.</param>
    /// <param name="backgroundColor">The background clear color.</param>
    /// <param name="ambientColor">The ambient light color.</param>
    /// <param name="ambientIntensity">The ambient light intensity.</param>
    /// <param name="gizmoTarget">The object to display gizmo for, or null.</param>
    /// <param name="gizmoMode">The gizmo visualization mode.</param>
    void Render(
        RenderContext renderContext,
        Camera3D.Resource camera,
        IReadOnlyList<Object3D.Resource> objects,
        IReadOnlyList<Light3D.Resource> lights,
        Color backgroundColor,
        Color ambientColor,
        float ambientIntensity,
        Object3D.Resource? gizmoTarget = null,
        GizmoMode gizmoMode = GizmoMode.None);

    /// <summary>
    /// Creates a SkiaSharp surface from the rendered output.
    /// </summary>
    /// <returns>The surface containing the rendered scene, or null if not available.</returns>
    SKSurface? CreateSkiaSurface();

    /// <summary>
    /// Downloads the rendered pixels from GPU to CPU memory.
    /// </summary>
    /// <returns>The pixel data as a byte array.</returns>
    byte[] DownloadPixels();

    /// <summary>
    /// Performs a hit test at the specified screen point.
    /// </summary>
    /// <param name="screenPoint">The point in screen coordinates.</param>
    /// <returns>The 3D object at that point, or null if none.</returns>
    Object3D.Resource? HitTest(Point screenPoint);

    /// <summary>
    /// Performs a hit test and returns the path from root to the hit object.
    /// </summary>
    /// <param name="screenPoint">The point in screen coordinates.</param>
    /// <returns>A list representing the path from root to the hit object, or empty if none.</returns>
    IReadOnlyList<Object3D.Resource> HitTestWithPath(Point screenPoint);

    /// <summary>
    /// Performs a gizmo hit test at the specified screen point.
    /// </summary>
    /// <param name="screenPoint">The point in screen coordinates.</param>
    /// <param name="gizmoTarget">The object that the gizmo is displayed for.</param>
    /// <param name="gizmoMode">The current gizmo mode.</param>
    /// <returns>The gizmo axis that was hit, or None if no axis was hit.</returns>
    GizmoAxis GizmoHitTest(Point screenPoint, Object3D.Resource? gizmoTarget, GizmoMode gizmoMode);
}
