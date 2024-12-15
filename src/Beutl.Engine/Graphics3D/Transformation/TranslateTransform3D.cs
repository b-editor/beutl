using System.Numerics;

namespace Beutl.Graphics3D.Transformation;

public sealed class TranslateTransform3D : Transform3D
{
    public static readonly CoreProperty<float> XProperty;
    public static readonly CoreProperty<float> YProperty;
    public static readonly CoreProperty<float> ZProperty;
    private float _y;
    private float _x;
    private float _z;

    static TranslateTransform3D()
    {
        XProperty = ConfigureProperty<float, TranslateTransform3D>(nameof(X))
            .Accessor(o => o.X, (o, v) => o.X = v)
            .DefaultValue(0)
            .Register();

        YProperty = ConfigureProperty<float, TranslateTransform3D>(nameof(Y))
            .Accessor(o => o.Y, (o, v) => o.Y = v)
            .DefaultValue(0)
            .Register();

        ZProperty = ConfigureProperty<float, TranslateTransform3D>(nameof(Z))
            .Accessor(o => o.Z, (o, v) => o.Z = v)
            .DefaultValue(0)
            .Register();

        AffectsRender<TranslateTransform3D>(XProperty, YProperty, ZProperty);
    }

    public TranslateTransform3D()
    {
    }

    public TranslateTransform3D(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public TranslateTransform3D(Vector3 vector)
    {
        X = vector.X;
        Y = vector.Y;
        Z = vector.Z;
    }

    public float X
    {
        get => _x;
        set => SetAndRaise(XProperty, ref _x, value);
    }

    public float Y
    {
        get => _y;
        set => SetAndRaise(YProperty, ref _y, value);
    }

    public float Z
    {
        get => _z;
        set => SetAndRaise(ZProperty, ref _z, value);
    }

    public override Matrix4x4 Value => Matrix4x4.CreateTranslation(X, Y, Z);
}
