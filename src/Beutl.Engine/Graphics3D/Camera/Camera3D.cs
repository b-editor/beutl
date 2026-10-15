using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics3D.Camera;

/// <summary>
/// Base class for 3D cameras.
/// </summary>
public abstract partial class Camera3D : EngineObject
{
    public Camera3D()
    {
        ScanProperties<Camera3D>();
    }

    // At the default 60 degree field of view this distance shows the z = 0 plane at one unit per pixel for a
    // 1080 px tall render, so 2D content placed there keeps its 2D size.
    internal const float DefaultDistance = 935.3074f;

    [Display(Name = nameof(GraphicsStrings.Position), ResourceType = typeof(GraphicsStrings)), NumberStep(1, 0.1)]
    public IProperty<Vector3> Position { get; } = Property.CreateAnimatable(new Vector3(0, 0, -DefaultDistance));

    [Display(Name = nameof(GraphicsStrings.Target), ResourceType = typeof(GraphicsStrings)), NumberStep(1, 0.1)]
    public IProperty<Vector3> Target { get; } = Property.CreateAnimatable(Vector3.Zero);

    [Display(Name = nameof(GraphicsStrings.Camera3D_Up), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Vector3> Up { get; } = Property.CreateAnimatable(-Vector3.UnitY);

    [Display(Name = nameof(GraphicsStrings.Camera3D_NearPlane), ResourceType = typeof(GraphicsStrings))]
    [Range(0.001f, float.MaxValue), NumberStep(1, 0.1)]
    public IProperty<float> NearPlane { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.Camera3D_FarPlane), ResourceType = typeof(GraphicsStrings))]
    [Range(0.001f, float.MaxValue), NumberStep(1, 0.1)]
    public IProperty<float> FarPlane { get; } = Property.CreateAnimatable(10000f);

    public abstract partial class Resource
    {
        /// <summary>
        /// Gets the view matrix for this camera.
        /// </summary>
        public Matrix4x4 GetViewMatrix()
        {
            return Matrix4x4.CreateLookAt(Position, Target, Up);
        }

        /// <summary>
        /// Gets the projection matrix for this camera.
        /// </summary>
        /// <param name="aspectRatio">The aspect ratio of the render target.</param>
        /// <returns>The projection matrix.</returns>
        public abstract Matrix4x4 GetProjectionMatrix(float aspectRatio);
    }
}
