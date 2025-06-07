using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Language;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 点光源
/// </summary>
[Display(Name = "PointLight3D")]
public class PointLight3D : Light3D
{
    public static readonly CoreProperty<Vector3> PositionProperty;
    public static readonly CoreProperty<float> RangeProperty;
    public static readonly CoreProperty<float> AttenuationConstantProperty;
    public static readonly CoreProperty<float> AttenuationLinearProperty;
    public static readonly CoreProperty<float> AttenuationQuadraticProperty;

    private Vector3 _position = Vector3.Zero;
    private float _range = 10.0f;
    private float _attenuationConstant = 1.0f;
    private float _attenuationLinear = 0.1f;
    private float _attenuationQuadratic = 0.01f;

    static PointLight3D()
    {
        PositionProperty = ConfigureProperty<Vector3, PointLight3D>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue(Vector3.Zero)
            .Register();

        RangeProperty = ConfigureProperty<float, PointLight3D>(nameof(Range))
            .Accessor(o => o.Range, (o, v) => o.Range = v)
            .DefaultValue(10.0f)
            .Register();

        AttenuationConstantProperty = ConfigureProperty<float, PointLight3D>(nameof(AttenuationConstant))
            .Accessor(o => o.AttenuationConstant, (o, v) => o.AttenuationConstant = v)
            .DefaultValue(1.0f)
            .Register();

        AttenuationLinearProperty = ConfigureProperty<float, PointLight3D>(nameof(AttenuationLinear))
            .Accessor(o => o.AttenuationLinear, (o, v) => o.AttenuationLinear = v)
            .DefaultValue(0.1f)
            .Register();

        AttenuationQuadraticProperty = ConfigureProperty<float, PointLight3D>(nameof(AttenuationQuadratic))
            .Accessor(o => o.AttenuationQuadratic, (o, v) => o.AttenuationQuadratic = v)
            .DefaultValue(0.01f)
            .Register();

        AffectsRender<PointLight3D>(PositionProperty, RangeProperty,
            AttenuationConstantProperty, AttenuationLinearProperty, AttenuationQuadraticProperty);
    }

    /// <summary>
    /// ライトの位置
    /// </summary>
    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Lighting))]
    public Vector3 Position
    {
        get => _position;
        set => SetAndRaise(PositionProperty, ref _position, value);
    }

    /// <summary>
    /// ライトの有効範囲
    /// </summary>
    [Display(Name = "Range", GroupName = "Lighting")]
    [Range(0.001f, float.MaxValue)]
    public float Range
    {
        get => _range;
        set => SetAndRaise(RangeProperty, ref _range, Math.Max(0.001f, value));
    }

    /// <summary>
    /// 減衰：定数項
    /// </summary>
    [Display(Name = "AttenuationConstant", GroupName = "Attenuation")]
    [Range(0.0f, float.MaxValue)]
    public float AttenuationConstant
    {
        get => _attenuationConstant;
        set => SetAndRaise(AttenuationConstantProperty, ref _attenuationConstant, Math.Max(0.0f, value));
    }

    /// <summary>
    /// 減衰：線形項
    /// </summary>
    [Display(Name = "AttenuationLinear", GroupName = "Attenuation")]
    [Range(0.0f, float.MaxValue)]
    public float AttenuationLinear
    {
        get => _attenuationLinear;
        set => SetAndRaise(AttenuationLinearProperty, ref _attenuationLinear, Math.Max(0.0f, value));
    }

    /// <summary>
    /// 減衰：二次項
    /// </summary>
    [Display(Name = "AttenuationQuadratic", GroupName = "Attenuation")]
    [Range(0.0f, float.MaxValue)]
    public float AttenuationQuadratic
    {
        get => _attenuationQuadratic;
        set => SetAndRaise(AttenuationQuadraticProperty, ref _attenuationQuadratic, Math.Max(0.0f, value));
    }

    public override LightType Type => LightType.Point;

    /// <summary>
    /// 点光源を既存のPointLightに変換
    /// </summary>
    public PointLight ToPointLight()
    {
        return new PointLight
        {
            Position = _position,
            Color = Color,
            Intensity = Intensity,
            Range = _range,
            AttenuationConstant = _attenuationConstant,
            AttenuationLinear = _attenuationLinear,
            AttenuationQuadratic = _attenuationQuadratic,
            Enabled = Enabled,
            CastShadows = CastShadows
        };
    }

    protected override I3DMeshResource GetDebugMesh()
    {
        // 点光源用の球メッシュを作成
        var mesh = BasicMesh.CreateSphere(0.1f, 8, 4);
        var renderer = Scene3DManager.Current?.Renderer;
        if (renderer == null)
            throw new InvalidOperationException("3D renderer is not available");

        return renderer.CreateMesh(mesh);
    }

    public override BoundingBox GetBounds3D()
    {
        Vector3 extents = new Vector3(_range);
        return new BoundingBox(_position - extents, _position + extents);
    }
}
