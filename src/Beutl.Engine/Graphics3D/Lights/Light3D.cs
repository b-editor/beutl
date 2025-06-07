using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Animation;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dライトオブジェクトのベースクラス
/// </summary>
public abstract class Light3D : Drawable3D, ILight
{
    public static readonly CoreProperty<Vector3> ColorProperty;
    public static readonly CoreProperty<float> IntensityProperty;
    public static readonly CoreProperty<bool> EnabledProperty;

    private Vector3 _color = Vector3.One;
    private float _intensity = 1.0f;
    private bool _enabled = true;

    static Light3D()
    {
        ColorProperty = ConfigureProperty<Vector3, Light3D>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Vector3.One)
            .Register();

        IntensityProperty = ConfigureProperty<float, Light3D>(nameof(Intensity))
            .Accessor(o => o.Intensity, (o, v) => o.Intensity = v)
            .DefaultValue(1.0f)
            .Register();

        EnabledProperty = ConfigureProperty<bool, Light3D>(nameof(Enabled))
            .Accessor(o => o.Enabled, (o, v) => o.Enabled = v)
            .DefaultValue(true)
            .Register();

        AffectsRender<Light3D>(ColorProperty, IntensityProperty, EnabledProperty);
    }

    /// <summary>
    /// ライトの色
    /// </summary>
    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Lighting))]
    public Vector3 Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, Vector3.Max(value, Vector3.Zero));
    }

    /// <summary>
    /// ライトの強度
    /// </summary>
    [Display(Name = "Intensity",
        GroupName = nameof(Strings.Lighting))]
    [Range(0.0f, float.MaxValue)]
    public float Intensity
    {
        get => _intensity;
        set => SetAndRaise(IntensityProperty, ref _intensity, Math.Max(0.0f, value));
    }

    /// <summary>
    /// ライトが有効かどうか
    /// </summary>
    [Display(Name = "Enabled",
        GroupName = nameof(Strings.Lighting))]
    public bool Enabled
    {
        get => _enabled;
        set => SetAndRaise(EnabledProperty, ref _enabled, value);
    }

    // ILightインターフェースの実装
    public abstract LightType Type { get; }

    // ライトオブジェクトは通常メッシュを持たないが、
    // デバッグ表示用にアイコンメッシュを提供
    public override I3DMeshResource Mesh => GetDebugMesh();

    protected override void RenderCore3D(I3DCanvas canvas)
    {
        // ライトのデバッグ表示（アイコンなど）を描画
        if (Scene3DManager.Current?.ShowDebugInfo == true)
        {
            canvas.DrawMesh(Mesh, GetDebugMaterial());
        }
    }

    /// <summary>
    /// デバッグ表示用のメッシュを取得
    /// </summary>
    protected abstract I3DMeshResource GetDebugMesh();

    /// <summary>
    /// デバッグ表示用のマテリアルを取得
    /// </summary>
    protected virtual I3DMaterialResource GetDebugMaterial()
    {
        var material = new UnlitMaterial3D
        {
            Color = _color
        };

        var renderer = Scene3DManager.Current?.Renderer;
        if (renderer == null)
            throw new InvalidOperationException("3D renderer is not available");

        return renderer.CreateMaterial(material.ToBasicMaterial());
    }
}
