using System.Numerics;
using Beutl.Animation;
using Beutl.Graphics3D.Transformation;
using Beutl.Media;

namespace Beutl.Graphics3D;

/// <summary>
/// Represents an OpenGL camera.
/// </summary>
public abstract class Camera : Animatable, IAffectsRender
{
    public static readonly CoreProperty<Vector3> PositionProperty;
    public static readonly CoreProperty<Vector3> TargetProperty;
    public static readonly CoreProperty<ITransform3D?> TransformProperty;
    public static readonly CoreProperty<float> FovProperty;
    public static readonly CoreProperty<float> NearProperty;
    public static readonly CoreProperty<float> FarProperty;

    private Vector3 _position;
    private Vector3 _target = Vector3.UnitZ;
    private ITransform3D? _transform;
    private float _fov = 90;
    private float _near = 0.1f;
    private float _far = 20000;

    static Camera()
    {
        PositionProperty = ConfigureProperty<Vector3, Camera>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue(Vector3.Zero)
            .Register();

        TargetProperty = ConfigureProperty<Vector3, Camera>(nameof(Target))
            .Accessor(o => o.Target, (o, v) => o.Target = v)
            .DefaultValue(Vector3.UnitZ)
            .Register();

        TransformProperty = ConfigureProperty<ITransform3D?, Camera>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .Register();

        FovProperty = ConfigureProperty<float, Camera>(nameof(Fov))
            .Accessor(o => o.Fov, (o, v) => o.Fov = v)
            .DefaultValue(90)
            .Register();

        NearProperty = ConfigureProperty<float, Camera>(nameof(Near))
            .Accessor(o => o.Near, (o, v) => o.Near = v)
            .DefaultValue(0.1f)
            .Register();

        FarProperty = ConfigureProperty<float, Camera>(nameof(Far))
            .Accessor(o => o.Far, (o, v) => o.Far = v)
            .DefaultValue(20000)
            .Register();

        AffectsRender<Camera>(
            PositionProperty,
            TargetProperty,
            TransformProperty,
            FovProperty,
            NearProperty,
            FarProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Camera"/> class.
    /// </summary>
    protected Camera()
    {
        AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Camera"/> class.
    /// </summary>
    /// <param name="position">The position of the camera.</param>
    protected Camera(Vector3 position) : this()
    {
        Position = position;
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    /// <summary>
    /// Gets or sets the position of this <see cref="Camera"/>.
    /// </summary>
    public Vector3 Position
    {
        get => _position;
        set => SetAndRaise(PositionProperty, ref _position, value);
    }

    /// <summary>
    /// Gets or sets the target position of this <see cref="Camera"/>.
    /// </summary>
    public Vector3 Target
    {
        get => _target;
        set => SetAndRaise(TargetProperty, ref _target, value);
    }

    /// <summary>
    /// Gets or sets the <see cref="ITransform3D"/> of this <see cref="Camera"/>.
    /// </summary>
    public ITransform3D? Transform
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    /// <summary>
    /// Gets or sets the Degrees representing the Fov of this <see cref="Camera"/>.
    /// </summary>
    public float Fov
    {
        get => _fov;
        set => SetAndRaise(FovProperty, ref _fov, value);
    }

    /// <summary>
    /// Gets or sets the range to be drawn by this <see cref="Camera"/>.
    /// </summary>
    public float Near
    {
        get => _near;
        set => SetAndRaise(NearProperty, ref _near, value);
    }

    /// <summary>
    /// Gets or sets the range to be drawn by this <see cref="Camera"/>.
    /// </summary>
    public float Far
    {
        get => _far;
        set => SetAndRaise(FarProperty, ref _far, value);
    }

    /// <summary>
    /// Gets the ViewMatrix.
    /// </summary>
    /// <returns>Returns the view matrix.</returns>
    public Matrix4x4 GetViewMatrix()
    {
        return Transform?.Value ?? Matrix4x4.Identity * Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);
    }

    /// <summary>
    /// Gets ProjectionMatrix.
    /// </summary>
    /// <returns>Returns the projection matrix.</returns>
    public abstract Matrix4x4 GetProjectionMatrix();
    
    /// <summary>
    /// Converts a screen point to a ray in world space.
    /// </summary>
    /// <param name="x">The x-coordinate of the screen point.</param>
    /// <param name="y">The y-coordinate of the screen point.</param>
    /// <param name="screenW">The width of the screen.</param>
    /// <param name="screenH">The height of the screen.</param>
    /// <returns>A <see cref="Ray"/> representing the ray in world space.</returns>
    public Ray ScreenPointToRay(float x, float y, float screenW, float screenH)
    {
        Vector2 viewportPoint = new Vector2(x / screenW, y / screenH);
        viewportPoint.X = (viewportPoint.X - 0.5f) * 2;
        viewportPoint.Y = -(viewportPoint.Y - 0.5f) * 2;
        return ViewportPointToRay(viewportPoint);
    }

    /// <summary>
    /// Converts a point in viewport coordinates to a ray in world space.
    /// </summary>
    /// <param name="viewportPoint">The point in viewport coordinates, where (0,0) is the bottom-left and (1,1) is the top-right.</param>
    /// <returns>A <see cref="Ray"/> that starts at the camera position and points in the direction corresponding to the viewport point.</returns>
    public abstract Ray ViewportPointToRay(Vector2 viewportPoint);

    private void OnAffectsRenderInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : Camera
    {
        foreach (CoreProperty? item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.RaiseInvalidated(new RenderInvalidatedEventArgs(s, e.Property.Name));

                    if (e.OldValue is IAffectsRender oldAffectsRender)
                        oldAffectsRender.Invalidated -= s.OnAffectsRenderInvalidated;

                    if (e.NewValue is IAffectsRender newAffectsRender)
                        newAffectsRender.Invalidated += s.OnAffectsRenderInvalidated;
                }
            });
        }
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }
}
