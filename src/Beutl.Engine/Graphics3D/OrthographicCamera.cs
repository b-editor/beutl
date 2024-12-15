using System.Numerics;

namespace Beutl.Graphics3D;

/// <summary>
/// Represents an orthographic camera.
/// </summary>
public sealed class OrthographicCamera : Camera
{
    public static readonly CoreProperty<float> WidthProperty;
    public static readonly CoreProperty<float> HeightProperty;
    private float _width;
    private float _height;

    static OrthographicCamera()
    {
        WidthProperty = ConfigureProperty<float, OrthographicCamera>(nameof(Width))
            .Accessor(o => o.Width, (o, v) => o.Width = v)
            .DefaultValue(0)
            .Register();

        HeightProperty = ConfigureProperty<float, OrthographicCamera>(nameof(Height))
            .Accessor(o => o.Height, (o, v) => o.Height = v)
            .DefaultValue(0)
            .Register();

        AffectsRender<OrthographicCamera>(
            WidthProperty,
            HeightProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrthographicCamera"/> class.
    /// </summary>
    public OrthographicCamera()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrthographicCamera"/> class.
    /// </summary>
    /// <param name="position">The position of the camera.</param>
    /// <param name="width">The width of the view volume.</param>
    /// <param name="height">The height of the view volume.</param>
    public OrthographicCamera(Vector3 position, float width, float height)
        : base(position)
    {
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets or sets the width of the view volume.
    /// </summary>
    public float Width
    {
        get => _width;
        set => SetAndRaise(WidthProperty, ref _width, value);
    }

    /// <summary>
    /// Gets or sets the height of the view volume.
    /// </summary>
    public float Height
    {
        get => _height;
        set => SetAndRaise(HeightProperty, ref _height, value);
    }

    /// <inheritdoc/>
    public override Matrix4x4 GetProjectionMatrix()
    {
        return Matrix4x4.CreateOrthographic(Width, Height, Near, Far);
    }
}
