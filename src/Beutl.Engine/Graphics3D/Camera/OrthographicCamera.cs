using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics3D.Camera;

/// <summary>
/// An orthographic projection camera for 3D scenes.
/// </summary>
[Display(Name = nameof(Strings.OrthographicCamera), ResourceType = typeof(Strings))]
public partial class OrthographicCamera : Camera3D
{
    public OrthographicCamera()
    {
        ScanProperties<OrthographicCamera>();
    }

    /// <summary>
    /// Gets the width of the orthographic view volume.
    /// </summary>
    [Display(Name = nameof(Strings.Width), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable(10f);

    /// <inheritdoc />
    public override Matrix4x4 GetProjectionMatrix(Camera3D.Resource resource, float aspectRatio)
    {
        var orthoResource = (Resource)resource;
        float width = orthoResource.Width;
        float height = width / aspectRatio;
        return Matrix4x4.CreateOrthographic(
            width,
            height,
            resource.NearPlane,
            resource.FarPlane);
    }

    public new partial class Resource : Camera3D.Resource
    {
        /// <inheritdoc />
        public override Matrix4x4 GetProjectionMatrix(float aspectRatio)
        {
            float height = Width / aspectRatio;
            return Matrix4x4.CreateOrthographic(
                Width,
                height,
                NearPlane,
                FarPlane);
        }
    }
}
