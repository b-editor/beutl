using System;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics3D.Camera;

/// <summary>
/// A perspective projection camera for 3D scenes.
/// </summary>
[Display(Name = nameof(Strings.PerspectiveCamera), ResourceType = typeof(Strings))]
public partial class PerspectiveCamera : Camera3D
{
    public PerspectiveCamera()
    {
        ScanProperties<PerspectiveCamera>();
    }

    /// <summary>
    /// Gets the vertical field of view in degrees.
    /// </summary>
    [Display(Name = nameof(Strings.FieldOfView), ResourceType = typeof(Strings))]
    [Range(1f, 179f)]
    public IProperty<float> FieldOfView { get; } = Property.CreateAnimatable(60f);

    /// <inheritdoc />
    public override Matrix4x4 GetProjectionMatrix(Camera3D.Resource resource, float aspectRatio)
    {
        var perspectiveResource = (Resource)resource;
        float fovRadians = perspectiveResource.FieldOfView * MathF.PI / 180f;
        return Matrix4x4.CreatePerspectiveFieldOfView(
            fovRadians,
            aspectRatio,
            resource.NearPlane,
            resource.FarPlane);
    }

    public new partial class Resource : Camera3D.Resource
    {
        /// <inheritdoc />
        public override Matrix4x4 GetProjectionMatrix(float aspectRatio)
        {
            float fovRadians = FieldOfView * MathF.PI / 180f;
            return Matrix4x4.CreatePerspectiveFieldOfView(
                fovRadians,
                aspectRatio,
                NearPlane,
                FarPlane);
        }
    }
}
