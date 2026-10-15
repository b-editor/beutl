using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics3D.Camera;

/// <summary>
/// An orthographic projection camera for 3D scenes.
/// </summary>
[Display(Name = nameof(GraphicsStrings.OrthographicCamera), ResourceType = typeof(GraphicsStrings))]
public partial class OrthographicCamera : Camera3D
{
    public OrthographicCamera()
    {
        ScanProperties<OrthographicCamera>();
    }

    /// <summary>
    /// Gets the width of the orthographic view volume.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Width), ResourceType = typeof(GraphicsStrings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable(1920f);

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
