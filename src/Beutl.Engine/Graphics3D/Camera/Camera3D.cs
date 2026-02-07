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

    /// <summary>
    /// Gets the position of the camera in world space.
    /// </summary>
    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
    public IProperty<Vector3> Position { get; } = Property.CreateAnimatable(new Vector3(0, 0, 5));

    /// <summary>
    /// Gets the target point the camera is looking at.
    /// </summary>
    [Display(Name = nameof(Strings.Target), ResourceType = typeof(Strings))]
    public IProperty<Vector3> Target { get; } = Property.CreateAnimatable(Vector3.Zero);

    /// <summary>
    /// Gets the up direction of the camera.
    /// </summary>
    [Display(Name = nameof(Strings.Up), ResourceType = typeof(Strings))]
    public IProperty<Vector3> Up { get; } = Property.CreateAnimatable(Vector3.UnitY);

    /// <summary>
    /// Gets the near clipping plane distance.
    /// </summary>
    [Display(Name = nameof(Strings.NearPlane), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> NearPlane { get; } = Property.CreateAnimatable(0.1f);

    /// <summary>
    /// Gets the far clipping plane distance.
    /// </summary>
    [Display(Name = nameof(Strings.FarPlane), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> FarPlane { get; } = Property.CreateAnimatable(1000f);

    /// <summary>
    /// Gets the view matrix for this camera.
    /// </summary>
    public Matrix4x4 GetViewMatrix(Resource resource)
    {
        return Matrix4x4.CreateLookAt(resource.Position, resource.Target, resource.Up);
    }

    /// <summary>
    /// Gets the projection matrix for this camera.
    /// </summary>
    /// <param name="resource">The resource containing evaluated property values.</param>
    /// <param name="aspectRatio">The aspect ratio of the render target.</param>
    /// <returns>The projection matrix.</returns>
    public abstract Matrix4x4 GetProjectionMatrix(Resource resource, float aspectRatio);

    /// <summary>
    /// Gets the combined view-projection matrix.
    /// </summary>
    public Matrix4x4 GetViewProjectionMatrix(Resource resource, float aspectRatio)
    {
        return GetViewMatrix(resource) * GetProjectionMatrix(resource, aspectRatio);
    }

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
