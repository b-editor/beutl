using System.Numerics;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D;

/// <summary>
/// Contains rendering context information for 3D materials.
/// </summary>
public readonly struct RenderContext3D
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenderContext3D"/> struct.
    /// </summary>
    public RenderContext3D(
        IGraphicsContext graphicsContext,
        IRenderPass3D renderPass,
        IShaderCompiler shaderCompiler,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix,
        Vector3 cameraPosition,
        Vector3 lightDirection,
        Vector3 lightColor,
        Vector3 ambientColor)
    {
        GraphicsContext = graphicsContext;
        RenderPass = renderPass;
        ShaderCompiler = shaderCompiler;
        ViewMatrix = viewMatrix;
        ProjectionMatrix = projectionMatrix;
        CameraPosition = cameraPosition;
        LightDirection = lightDirection;
        LightColor = lightColor;
        AmbientColor = ambientColor;
    }

    /// <summary>
    /// Gets the graphics context for creating GPU resources.
    /// </summary>
    public IGraphicsContext GraphicsContext { get; }

    /// <summary>
    /// Gets the current render pass.
    /// </summary>
    public IRenderPass3D RenderPass { get; }

    /// <summary>
    /// Gets the shader compiler.
    /// </summary>
    public IShaderCompiler ShaderCompiler { get; }

    /// <summary>
    /// Gets the view transformation matrix.
    /// </summary>
    public Matrix4x4 ViewMatrix { get; }

    /// <summary>
    /// Gets the projection transformation matrix.
    /// </summary>
    public Matrix4x4 ProjectionMatrix { get; }

    /// <summary>
    /// Gets the camera position in world space.
    /// </summary>
    public Vector3 CameraPosition { get; }

    /// <summary>
    /// Gets the direction of the main light source.
    /// </summary>
    public Vector3 LightDirection { get; }

    /// <summary>
    /// Gets the color of the main light source.
    /// </summary>
    public Vector3 LightColor { get; }

    /// <summary>
    /// Gets the ambient light color.
    /// </summary>
    public Vector3 AmbientColor { get; }
}
