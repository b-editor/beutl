using System.Numerics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Lighting;

namespace Beutl.Graphics3D;

/// <summary>
/// Contains rendering context information for 3D materials.
/// </summary>
public readonly struct RenderContext3D
{
    /// <summary>
    /// Maximum number of lights supported in a single render pass.
    /// </summary>
    public const int MaxLights = 8;

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
        Vector3 ambientColor,
        IReadOnlyList<LightData> lights,
        RenderContext renderContext)
    {
        GraphicsContext = graphicsContext;
        RenderPass = renderPass;
        ShaderCompiler = shaderCompiler;
        ViewMatrix = viewMatrix;
        ProjectionMatrix = projectionMatrix;
        CameraPosition = cameraPosition;
        AmbientColor = ambientColor;
        Lights = lights;
        RenderContext = renderContext;
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
    /// Gets the ambient light color.
    /// </summary>
    public Vector3 AmbientColor { get; }

    /// <summary>
    /// Gets the list of lights in the scene.
    /// </summary>
    public IReadOnlyList<LightData> Lights { get; }

    /// <summary>
    /// Gets the 2D render context for texture source rendering.
    /// May be null if not provided during 3D rendering.
    /// </summary>
    public RenderContext RenderContext { get; }

    /// <summary>
    /// Gets the primary directional light direction (for backwards compatibility).
    /// Returns (0, -1, -1) if no directional light is present.
    /// </summary>
    public Vector3 LightDirection
    {
        get
        {
            foreach (var light in Lights)
            {
                if (light.Type == (int)LightType.Directional)
                    return light.PositionOrDirection;
            }
            return new Vector3(0, -1, -1);
        }
    }

    /// <summary>
    /// Gets the primary directional light color (for backwards compatibility).
    /// Returns (1, 1, 1) if no directional light is present.
    /// </summary>
    public Vector3 LightColor
    {
        get
        {
            foreach (var light in Lights)
            {
                if (light.Type == (int)LightType.Directional)
                    return light.Color * light.Intensity;
            }
            return Vector3.One;
        }
    }
}
