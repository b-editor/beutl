using System.Numerics;
using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Graphics3D;

/// <summary>
/// Represents an OpenGL camera.
/// </summary>
public abstract class Camera : Animatable, IAffectsRender
{
    public static readonly CoreProperty<Vector3> PositionProperty;
    public static readonly CoreProperty<Vector3> TargetProperty;
    public static readonly CoreProperty<float> FovProperty;
    public static readonly CoreProperty<float> NearProperty;
    public static readonly CoreProperty<float> FarProperty;

    private Vector3 _position;
    private Vector3 _target;
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
            .DefaultValue(Vector3.Zero)
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
        return Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);
    }

    /// <summary>
    /// Gets ProjectionMatrix.
    /// </summary>
    /// <returns>Returns the projection matrix.</returns>
    public abstract Matrix4x4 GetProjectionMatrix();

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
