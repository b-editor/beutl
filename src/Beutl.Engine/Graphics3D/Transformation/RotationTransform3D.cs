using System.Numerics;
using Beutl.Utilities;

namespace Beutl.Graphics3D.Transformation;

public sealed class RotationTransform3D : Transform3D
{
    public static readonly CoreProperty<float> YawProperty;
    public static readonly CoreProperty<float> PitchProperty;
    public static readonly CoreProperty<float> RollProperty;
    private float _yaw;
    private float _pitch;
    private float _roll;

    static RotationTransform3D()
    {
        YawProperty = ConfigureProperty<float, RotationTransform3D>(nameof(Yaw))
            .Accessor(o => o.Yaw, (o, v) => o.Yaw = v)
            .DefaultValue(0)
            .Register();

        PitchProperty = ConfigureProperty<float, RotationTransform3D>(nameof(Pitch))
            .Accessor(o => o.Pitch, (o, v) => o.Pitch = v)
            .DefaultValue(0)
            .Register();

        RollProperty = ConfigureProperty<float, RotationTransform3D>(nameof(Roll))
            .Accessor(o => o.Roll, (o, v) => o.Roll = v)
            .DefaultValue(0)
            .Register();

        AffectsRender<RotationTransform3D>(YawProperty, PitchProperty, RollProperty);
    }

    public RotationTransform3D()
    {
    }

    public RotationTransform3D(float yaw)
    {
        Yaw = yaw;
    }

    public float Yaw
    {
        get => _yaw;
        set => SetAndRaise(YawProperty, ref _yaw, value);
    }

    public float Pitch
    {
        get => _pitch;
        set => SetAndRaise(PitchProperty, ref _pitch, value);
    }

    public float Roll
    {
        get => _roll;
        set => SetAndRaise(RollProperty, ref _roll, value);
    }

    public override Matrix4x4 Value => Matrix4x4.CreateFromYawPitchRoll(
        MathUtilities.ToRadians(Yaw), MathUtilities.ToRadians(Pitch), MathUtilities.ToRadians(Roll));
}
