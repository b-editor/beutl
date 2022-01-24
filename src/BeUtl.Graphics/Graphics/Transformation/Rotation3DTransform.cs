using System.Numerics;

using BeUtl.Utilities;

namespace BeUtl.Graphics.Transformation;

public sealed class Rotation3DTransform : Transform
{
    public static readonly CoreProperty<float> RotationXProperty;
    public static readonly CoreProperty<float> RotationYProperty;
    public static readonly CoreProperty<float> RotationZProperty;
    public static readonly CoreProperty<float> CenterXProperty;
    public static readonly CoreProperty<float> CenterYProperty;
    public static readonly CoreProperty<float> CenterZProperty;
    public static readonly CoreProperty<float> DepthProperty;
    private float _rotationX;
    private float _rotationY;
    private float _rotationZ;
    private float _centerX;
    private float _centerY;
    private float _centerZ;
    private float _depth;

    static Rotation3DTransform()
    {
        RotationXProperty = ConfigureProperty<float, Rotation3DTransform>(nameof(RotationX))
            .Accessor(o => o.RotationX, (o, v) => o.RotationX = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(0)
            .Register();

        RotationYProperty = ConfigureProperty<float, Rotation3DTransform>(nameof(RotationY))
            .Accessor(o => o.RotationY, (o, v) => o.RotationY = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(0)
            .Register();

        RotationZProperty = ConfigureProperty<float, Rotation3DTransform>(nameof(RotationZ))
            .Accessor(o => o.RotationZ, (o, v) => o.RotationZ = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(0)
            .Register();

        CenterXProperty = ConfigureProperty<float, Rotation3DTransform>(nameof(CenterX))
            .Accessor(o => o.CenterX, (o, v) => o.CenterX = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(0)
            .Register();

        CenterYProperty = ConfigureProperty<float, Rotation3DTransform>(nameof(CenterY))
            .Accessor(o => o.CenterY, (o, v) => o.CenterY = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(0)
            .Register();

        CenterZProperty = ConfigureProperty<float, Rotation3DTransform>(nameof(CenterZ))
            .Accessor(o => o.CenterZ, (o, v) => o.CenterZ = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(0)
            .Register();

        DepthProperty = ConfigureProperty<float, Rotation3DTransform>(nameof(Depth))
            .Accessor(o => o.Depth, (o, v) => o.Depth = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(0)
            .Register();

        AffectsRender<Rotation3DTransform>(
            RotationXProperty, RotationYProperty, RotationZProperty,
            CenterXProperty, CenterYProperty, CenterZProperty,
            DepthProperty);
    }

    public Rotation3DTransform()
    {
    }

    public Rotation3DTransform(
        float rotationX,
        float rotationY,
        float rotationZ,
        float centerX,
        float centerY,
        float centerZ)
    {
        RotationX = rotationX;
        RotationY = rotationY;
        RotationZ = rotationZ;
        CenterX = centerX;
        CenterY = centerY;
        CenterZ = centerZ;
    }

    public float RotationX
    {
        get => _rotationX;
        set => SetAndRaise(RotationXProperty, ref _rotationX, value);
    }

    public float RotationY
    {
        get => _rotationY;
        set => SetAndRaise(RotationYProperty, ref _rotationY, value);
    }

    public float RotationZ
    {
        get => _rotationZ;
        set => SetAndRaise(RotationZProperty, ref _rotationZ, value);
    }

    public float CenterX
    {
        get => _centerX;
        set => SetAndRaise(CenterXProperty, ref _centerX, value);
    }

    public float CenterY
    {
        get => _centerY;
        set => SetAndRaise(CenterYProperty, ref _centerY, value);
    }

    public float CenterZ
    {
        get => _centerZ;
        set => SetAndRaise(CenterZProperty, ref _centerZ, value);
    }

    public float Depth
    {
        get => _depth;
        set => SetAndRaise(DepthProperty, ref _depth, value);
    }

    public override Matrix Value
    {
        get
        {
            Matrix4x4 matrix44 = Matrix4x4.Identity;

            matrix44 *= Matrix4x4.CreateTranslation(-_centerX, -_centerY, -_centerZ);

            matrix44 *= Matrix4x4.CreateRotationX(MathUtilities.ToRadians(_rotationX));
            matrix44 *= Matrix4x4.CreateRotationY(MathUtilities.ToRadians(_rotationY));
            matrix44 *= Matrix4x4.CreateRotationZ(MathUtilities.ToRadians(_rotationZ));

            matrix44 *= Matrix4x4.CreateTranslation(_centerX, _centerY, _centerZ);

            if (Depth != 0)
            {
                Matrix4x4 perspectiveMatrix = Matrix4x4.Identity;
                perspectiveMatrix.M34 = -1 / _depth;
                matrix44 *= perspectiveMatrix;
            }

            var matrix = new Matrix(
                matrix44.M11,
                matrix44.M12,
                matrix44.M14,
                matrix44.M21,
                matrix44.M22,
                matrix44.M24,
                matrix44.M41,
                matrix44.M42,
                matrix44.M44);

            return matrix;
        }
    }
}
