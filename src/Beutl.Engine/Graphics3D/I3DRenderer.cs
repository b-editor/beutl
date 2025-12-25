using System;
using System.Collections.Generic;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics3D;

/// <summary>
/// Interface for 3D rendering abstraction.
/// Provides backend-agnostic 3D rendering capabilities.
/// </summary>
public interface I3DRenderer : IDisposable
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
    /// <param name="camera">The camera resource.</param>
    /// <param name="objects">The object resources to render.</param>
    /// <param name="lights">The lights in the scene.</param>
    /// <param name="backgroundColor">The background clear color.</param>
    /// <param name="ambientColor">The ambient light color.</param>
    /// <param name="ambientIntensity">The ambient light intensity.</param>
    void Render(
        Camera3D.Resource camera,
        IReadOnlyList<Object3D.Resource> objects,
        IReadOnlyList<Light3D.Resource> lights,
        Color backgroundColor,
        Color ambientColor,
        float ambientIntensity);

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
}
