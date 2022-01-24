using BeUtl.Graphics.Transformation;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations.Transform;

public sealed class Rotate3DTransform : TransformOperation
{
    public static readonly CoreProperty<float> RotationXProperty;
    public static readonly CoreProperty<float> RotationYProperty;
    public static readonly CoreProperty<float> RotationZProperty;
    public static readonly CoreProperty<float> CenterXProperty;
    public static readonly CoreProperty<float> CenterYProperty;
    public static readonly CoreProperty<float> CenterZProperty;
    public static readonly CoreProperty<float> DepthProperty;
    private readonly Rotation3DTransform _transform = new();

    static Rotate3DTransform()
    {
        RotationXProperty = ConfigureProperty<float, Rotate3DTransform>(nameof(RotationX))
            .Accessor(o => o.RotationX, (o, v) => o.RotationX = v)
            .OverrideMetadata(new OperationPropertyMetadata<float>
            {
                PropertyFlags = PropertyFlags.Designable,
                SerializeName = "rotationX",
                IsAnimatable = true,
                DefaultValue = 0
            })
            .Register();

        RotationYProperty = ConfigureProperty<float, Rotate3DTransform>(nameof(RotationY))
            .Accessor(o => o.RotationY, (o, v) => o.RotationY = v)
            .OverrideMetadata(new OperationPropertyMetadata<float>
            {
                PropertyFlags = PropertyFlags.Designable,
                SerializeName = "rotationY",
                IsAnimatable = true,
                DefaultValue = 0
            })
            .Register();

        RotationZProperty = ConfigureProperty<float, Rotate3DTransform>(nameof(RotationZ))
            .Accessor(o => o.RotationZ, (o, v) => o.RotationZ = v)
            .OverrideMetadata(new OperationPropertyMetadata<float>
            {
                PropertyFlags = PropertyFlags.Designable,
                SerializeName = "rotationZ",
                IsAnimatable = true,
                DefaultValue = 0
            })
            .Register();

        CenterXProperty = ConfigureProperty<float, Rotate3DTransform>(nameof(CenterX))
            .Accessor(o => o.CenterX, (o, v) => o.CenterX = v)
            .OverrideMetadata(new OperationPropertyMetadata<float>
            {
                PropertyFlags = PropertyFlags.Designable,
                SerializeName = "centerX",
                IsAnimatable = true,
                DefaultValue = 0
            })
            .Register();

        CenterYProperty = ConfigureProperty<float, Rotate3DTransform>(nameof(CenterY))
            .Accessor(o => o.CenterY, (o, v) => o.CenterY = v)
            .OverrideMetadata(new OperationPropertyMetadata<float>
            {
                PropertyFlags = PropertyFlags.Designable,
                SerializeName = "centerY",
                IsAnimatable = true,
                DefaultValue = 0
            })
            .Register();

        CenterZProperty = ConfigureProperty<float, Rotate3DTransform>(nameof(CenterZ))
            .Accessor(o => o.CenterZ, (o, v) => o.CenterZ = v)
            .OverrideMetadata(new OperationPropertyMetadata<float>
            {
                PropertyFlags = PropertyFlags.Designable,
                SerializeName = "centerZ",
                IsAnimatable = true,
                DefaultValue = 0
            })
            .Register();

        DepthProperty = ConfigureProperty<float, Rotate3DTransform>(nameof(Depth))
            .Accessor(o => o.Depth, (o, v) => o.Depth = v)
            .OverrideMetadata(new OperationPropertyMetadata<float>
            {
                PropertyFlags = PropertyFlags.Designable,
                SerializeName = "depth",
                IsAnimatable = true,
                DefaultValue = 0
            })
            .Register();
    }

    public float RotationX
    {
        get => _transform.RotationX;
        set => _transform.RotationX = value;
    }

    public float RotationY
    {
        get => _transform.RotationY;
        set => _transform.RotationY = value;
    }

    public float RotationZ
    {
        get => _transform.RotationZ;
        set => _transform.RotationZ = value;
    }

    public float CenterX
    {
        get => _transform.CenterX;
        set => _transform.CenterX = value;
    }

    public float CenterY
    {
        get => _transform.CenterY;
        set => _transform.CenterY = value;
    }

    public float CenterZ
    {
        get => _transform.CenterZ;
        set => _transform.CenterZ = value;
    }

    public float Depth
    {
        get => _transform.Depth;
        set => _transform.Depth = value;
    }

    public override Graphics.Transformation.Transform Transform => _transform;
}
