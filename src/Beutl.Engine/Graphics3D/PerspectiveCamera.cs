using System.Numerics;
using Beutl.Utilities;

namespace Beutl.Graphics3D;

/// <summary>
/// Represents the perspective camera.
/// </summary>
public sealed class PerspectiveCamera : Camera
{
    public static readonly CoreProperty<float> AspectRatioProperty;
    private float _aspectRatio;

    static PerspectiveCamera()
    {
        AspectRatioProperty = ConfigureProperty<float, PerspectiveCamera>(nameof(AspectRatio))
            .Accessor(o => o.AspectRatio, (o, v) => o.AspectRatio = v)
            .DefaultValue(0)
            .Register();

        AffectsRender<PerspectiveCamera>(
            AspectRatioProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PerspectiveCamera"/> class.
    /// </summary>
    public PerspectiveCamera() : base()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PerspectiveCamera"/> class.
    /// </summary>
    /// <param name="position">The position of the camera.</param>
    /// <param name="aspectRatio">The aspect ratio of the camera's viewport.</param>
    public PerspectiveCamera(Vector3 position, float aspectRatio)
        : base(position)
    {
        AspectRatio = aspectRatio;
    }

    /// <summary>
    /// Gets or sets the aspect ratio of the camera's viewport.
    /// </summary>
    public float AspectRatio
    {
        get => _aspectRatio;
        set => SetAndRaise(AspectRatioProperty, ref _aspectRatio, value);
    }

    /// <inheritdoc/>
    public override Matrix4x4 GetProjectionMatrix()
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(MathUtilities.ToRadians(Fov), AspectRatio, Near, Far);
    }

    /// <inheritdoc/>
    public override Ray ViewportPointToRay(Vector2 viewportPoint)
    {
        float nearPlaneHalfH = Near * (float)Math.Tan(MathUtilities.ToRadians(Fov) / 2.0f);
        var nearPlanePoint = new Vector3
        {
            X = nearPlaneHalfH * AspectRatio * viewportPoint.X,
            Y = nearPlaneHalfH * viewportPoint.Y,
            Z = -Near
        };

        Matrix4x4 view = GetViewMatrix();
        nearPlanePoint = Vector3.Transform(nearPlanePoint, view);

        return new Ray()
        {
            Origin = nearPlanePoint,
            Direction = Vector3.Normalize(nearPlanePoint - view.Translation)
        };
    }
}
