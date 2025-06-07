using System.ComponentModel.DataAnnotations;
using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// スポットライト
/// </summary>
[Display(Name = "SpotLight3D")]
public class SpotLight3D : PointLight3D
{
    public static readonly CoreProperty<Vector3> DirectionProperty;
    public static readonly CoreProperty<float> InnerConeAngleProperty;
    public static readonly CoreProperty<float> OuterConeAngleProperty;

    private Vector3 _direction = new Vector3(0, -1, 0);
    private float _innerConeAngle = 30.0f;
    private float _outerConeAngle = 45.0f;

    static SpotLight3D()
    {
        DirectionProperty = ConfigureProperty<Vector3, SpotLight3D>(nameof(Direction))
            .Accessor(o => o.Direction, (o, v) => o.Direction = v)
            .DefaultValue(new Vector3(0, -1, 0))
            .Register();

        InnerConeAngleProperty = ConfigureProperty<float, SpotLight3D>(nameof(InnerConeAngle))
            .Accessor(o => o.InnerConeAngle, (o, v) => o.InnerConeAngle = v)
            .DefaultValue(30.0f)
            .Register();

        OuterConeAngleProperty = ConfigureProperty<float, SpotLight3D>(nameof(OuterConeAngle))
            .Accessor(o => o.OuterConeAngle, (o, v) => o.OuterConeAngle = v)
            .DefaultValue(45.0f)
            .Register();

        AffectsRender<SpotLight3D>(DirectionProperty, InnerConeAngleProperty, OuterConeAngleProperty);
    }

    /// <summary>
    /// ライトの方向
    /// </summary>
    [Display(Name = "Direction", GroupName = "Lighting")]
    public Vector3 Direction
    {
        get => _direction;
        set => SetAndRaise(DirectionProperty, ref _direction, Vector3.Normalize(value));
    }

    /// <summary>
    /// 内側コーン角度（度）
    /// </summary>
    [Display(Name = "InnerConeAngle", GroupName = "Lighting")]
    [Range(0.1f, 89.9f)]
    public float InnerConeAngle
    {
        get => _innerConeAngle;
        set => SetAndRaise(InnerConeAngleProperty, ref _innerConeAngle, Math.Max(0.1f, Math.Min(89.9f, value)));
    }

    /// <summary>
    /// 外側コーン角度（度）
    /// </summary>
    [Display(Name = "OuterConeAngle", GroupName = "Lighting")]
    [Range(0.2f, 90.0f)]
    public float OuterConeAngle
    {
        get => _outerConeAngle;
        set => SetAndRaise(OuterConeAngleProperty, ref _outerConeAngle, Math.Max(_innerConeAngle + 0.1f, Math.Min(90.0f, value)));
    }

    public override LightType Type => LightType.Spot;

    /// <summary>
    /// スポットライトを既存のSpotLightに変換
    /// </summary>
    public SpotLight ToSpotLight()
    {
        return new SpotLight
        {
            Position = Position,
            Direction = _direction,
            Color = Color,
            Intensity = Intensity,
            Range = Range,
            InnerConeAngle = _innerConeAngle,
            OuterConeAngle = _outerConeAngle,
            AttenuationConstant = AttenuationConstant,
            AttenuationLinear = AttenuationLinear,
            AttenuationQuadratic = AttenuationQuadratic,
            Enabled = Enabled,
            CastShadows = CastShadows
        };
    }

    protected override I3DMeshResource GetDebugMesh()
    {
        // スポットライト用のコーンメッシュを作成
        var mesh = CreateConeMesh();
        var renderer = Scene3DManager.Current?.Renderer;
        if (renderer == null)
            throw new InvalidOperationException("3D renderer is not available");

        return renderer.CreateMesh(mesh);
    }

    private I3DMesh CreateConeMesh()
    {
        // 簡単なコーンメッシュを作成（デバッグ表示用）
        float coneHeight = Range * 0.2f;
        float coneRadius = coneHeight * MathF.Tan(MathF.PI / 180.0f * _outerConeAngle);

        return BasicMesh.CreateCone(coneRadius, coneHeight, 8);
    }

    public override BoundingBox GetBounds3D()
    {
        // スポットライトのコーン形状を考慮したバウンディングボックス
        float coneRadius = Range * MathF.Tan(MathF.PI / 180.0f * _outerConeAngle);
        Vector3 extents = new Vector3(coneRadius, coneRadius, Range);
        return new BoundingBox(Position - extents, Position + extents);
    }
}
