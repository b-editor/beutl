using System.Numerics;

namespace Beutl.Graphics3D.Transformation;

public sealed class ScaleTransform3D : Transform3D
{
    public static readonly CoreProperty<float> ScaleProperty;
    public static readonly CoreProperty<float> ScaleXProperty;
    public static readonly CoreProperty<float> ScaleYProperty;
    public static readonly CoreProperty<float> ScaleZProperty;
    private float _scale = 100;
    private float _scaleX = 100;
    private float _scaleY = 100;
    private float _scaleZ = 100;

    static ScaleTransform3D()
    {
        ScaleProperty = ConfigureProperty<float, ScaleTransform3D>(nameof(Scale))
            .Accessor(o => o.Scale, (o, v) => o.Scale = v)
            .DefaultValue(100)
            .Register();

        ScaleXProperty = ConfigureProperty<float, ScaleTransform3D>(nameof(ScaleX))
            .Accessor(o => o.ScaleX, (o, v) => o.ScaleX = v)
            .DefaultValue(100)
            .Register();

        ScaleYProperty = ConfigureProperty<float, ScaleTransform3D>(nameof(ScaleY))
            .Accessor(o => o.ScaleY, (o, v) => o.ScaleY = v)
            .DefaultValue(100)
            .Register();

        ScaleZProperty = ConfigureProperty<float, ScaleTransform3D>(nameof(ScaleZ))
            .Accessor(o => o.ScaleZ, (o, v) => o.ScaleZ = v)
            .DefaultValue(100)
            .Register();

        AffectsRender<ScaleTransform3D>(ScaleProperty, ScaleXProperty, ScaleYProperty, ScaleZProperty);
    }

    public ScaleTransform3D()
    {
    }

    public ScaleTransform3D(Vector3 vector, float scale = 100)
    {
        Scale = scale;
        ScaleX = vector.X;
        ScaleY = vector.Y;
    }

    public ScaleTransform3D(float x, float y, float z, float scale = 100)
    {
        Scale = scale;
        ScaleX = x;
        ScaleY = y;
    }

    public float Scale
    {
        get => _scale;
        set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    public float ScaleX
    {
        get => _scaleX;
        set => SetAndRaise(ScaleXProperty, ref _scaleX, value);
    }

    public float ScaleY
    {
        get => _scaleY;
        set => SetAndRaise(ScaleYProperty, ref _scaleY, value);
    }

    public float ScaleZ
    {
        get => _scaleZ;
        set => SetAndRaise(ScaleZProperty, ref _scaleZ, value);
    }

    public override Matrix4x4 Value
    {
        get
        {
            float scale = Scale / 100f;
            float scaleX = ScaleX / 100f;
            float scaleY = ScaleY / 100f;
            float scaleZ = ScaleZ / 100f;
            return Matrix4x4.CreateScale(scale * scaleX, scale * scaleY, scale * scaleZ);
        }
    }
}
