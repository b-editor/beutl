using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Animation;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dレンダリング可能オブジェクトのベースクラス
/// 2DのDrawableクラスに対応する3D版
/// </summary>
[DummyType(typeof(DummyDrawable3D))]
public abstract class Drawable3D : Renderable, I3DRenderableObject
{
    public static readonly CoreProperty<Transform3D?> TransformProperty;
    public static readonly CoreProperty<Material3D?> MaterialProperty;
    public static readonly CoreProperty<bool> CastShadowsProperty;
    public static readonly CoreProperty<bool> ReceiveShadowsProperty;
    public static readonly CoreProperty<float> OpacityProperty;
    public static readonly CoreProperty<BlendMode> BlendModeProperty;
    public static readonly CoreProperty<Vector3> ScaleProperty;
    public static readonly CoreProperty<Vector3> RotationProperty;
    public static readonly CoreProperty<Vector3> TranslationProperty;

    private Transform3D? _transform;
    private Material3D? _material;
    private bool _castShadows = true;
    private bool _receiveShadows = true;
    private float _opacity = 1.0f;
    private BlendMode _blendMode = BlendMode.SrcOver;
    private Vector3 _scale = Vector3.One;
    private Vector3 _rotation = Vector3.Zero;
    private Vector3 _translation = Vector3.Zero;

    // I3DRenderableObjectの実装
    public abstract I3DMeshResource Mesh { get; }
    I3DMaterialResource I3DRenderableObject.Material => _materialResource ??= CreateMaterialResource();
    Matrix4x4 I3DRenderableObject.Transform => CalculateTransformMatrix();
    bool I3DRenderableObject.CastShadows => _castShadows;
    bool I3DRenderableObject.ReceiveShadows => _receiveShadows;

    private I3DMaterialResource? _materialResource;

    static Drawable3D()
    {
        TransformProperty = ConfigureProperty<Transform3D?, Drawable3D>(nameof(Transform3D))
            .Accessor(o => o._transform, (o, v) => o._transform = v)
            .DefaultValue(null)
            .Register();

        MaterialProperty = ConfigureProperty<Material3D?, Drawable3D>(nameof(Material3D))
            .Accessor(o => o._material, (o, v) => o._material = v)
            .DefaultValue(null)
            .Register();

        CastShadowsProperty = ConfigureProperty<bool, Drawable3D>(nameof(CastShadows))
            .Accessor(o => o._castShadows, (o, v) => o._castShadows = v)
            .DefaultValue(true)
            .Register();

        ReceiveShadowsProperty = ConfigureProperty<bool, Drawable3D>(nameof(ReceiveShadows))
            .Accessor(o => o._receiveShadows, (o, v) => o._receiveShadows = v)
            .DefaultValue(true)
            .Register();

        OpacityProperty = ConfigureProperty<float, Drawable3D>(nameof(Opacity))
            .Accessor(o => o._opacity, (o, v) => o._opacity = v)
            .DefaultValue(1.0f)
            .Register();

        BlendModeProperty = ConfigureProperty<BlendMode, Drawable3D>(nameof(BlendMode))
            .Accessor(o => o._blendMode, (o, v) => o._blendMode = v)
            .DefaultValue(BlendMode.SrcOver)
            .Register();

        ScaleProperty = ConfigureProperty<Vector3, Drawable3D>(nameof(Scale))
            .Accessor(o => o._scale, (o, v) => o._scale = v)
            .DefaultValue(Vector3.One)
            .Register();

        RotationProperty = ConfigureProperty<Vector3, Drawable3D>(nameof(Rotation))
            .Accessor(o => o._rotation, (o, v) => o._rotation = v)
            .DefaultValue(Vector3.Zero)
            .Register();

        TranslationProperty = ConfigureProperty<Vector3, Drawable3D>(nameof(Translation))
            .Accessor(o => o._translation, (o, v) => o._translation = v)
            .DefaultValue(Vector3.Zero)
            .Register();

        AffectsRender<Drawable3D>(
            TransformProperty, MaterialProperty,
            CastShadowsProperty, ReceiveShadowsProperty,
            OpacityProperty, BlendModeProperty,
            ScaleProperty, RotationProperty, TranslationProperty);

        Hierarchy<Drawable3D>(
            TransformProperty, MaterialProperty);
    }

    /// <summary>
    /// カスタム変換行列
    /// </summary>
    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Transform))]
    public Transform3D? Transform3D
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    /// <summary>
    /// 3Dマテリアル
    /// </summary>
    [Display(Name = "Material", GroupName = "Material")]
    public Material3D? Material3D
    {
        get => _material;
        set
        {
            if (SetAndRaise(MaterialProperty, ref _material, value))
            {
                // マテリアルが変更されたらリソースを再作成
                _materialResource?.Dispose();
                _materialResource = null;
            }
        }
    }

    public bool CastShadows
    {
        get => _castShadows;
        set => SetAndRaise(CastShadowsProperty, ref _castShadows, value);
    }

    public bool ReceiveShadows
    {
        get => _receiveShadows;
        set => SetAndRaise(ReceiveShadowsProperty, ref _receiveShadows, value);
    }

    /// <summary>
    /// 不透明度 (0.0-1.0)
    /// </summary>
    [Display(Name = nameof(Strings.Opacity), ResourceType = typeof(Strings))]
    [Range(0.0f, 1.0f)]
    public float Opacity
    {
        get => _opacity;
        set => SetAndRaise(OpacityProperty, ref _opacity, Math.Clamp(value, 0.0f, 1.0f));
    }

    /// <summary>
    /// ブレンドモード
    /// </summary>
    [Display(Name = nameof(Strings.BlendMode), ResourceType = typeof(Strings))]
    public BlendMode BlendMode
    {
        get => _blendMode;
        set => SetAndRaise(BlendModeProperty, ref _blendMode, value);
    }

    /// <summary>
    /// スケール
    /// </summary>
    [Display(Name = nameof(Strings.Scale), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Transform))]
    public Vector3 Scale
    {
        get => _scale;
        set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    /// <summary>
    /// 回転（ラジアン）
    /// </summary>
    [Display(Name = nameof(Strings.Rotation), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Transform))]
    public Vector3 Rotation
    {
        get => _rotation;
        set => SetAndRaise(RotationProperty, ref _rotation, value);
    }

    /// <summary>
    /// 平行移動
    /// </summary>
    [Display(Name = "Translation",
        GroupName = nameof(Strings.Transform))]
    public Vector3 Translation
    {
        get => _translation;
        set => SetAndRaise(TranslationProperty, ref _translation, value);
    }

    /// <summary>
    /// 3Dシーンでレンダリングする
    /// </summary>
    public virtual void Render3D(I3DCanvas canvas)
    {
        if (!IsVisible)
            return;

        // カスタム変換の適用
        if (_transform != null)
        {
            canvas.PushTransform(_transform.Value);
        }

        try
        {
            // 不透明度の適用
            if (_opacity < 1.0f)
            {
                canvas.PushOpacity(_opacity);
            }

            try
            {
                // 実際のレンダリング
                RenderCore3D(canvas);
            }
            finally
            {
                if (_opacity < 1.0f)
                {
                    canvas.PopOpacity();
                }
            }
        }
        finally
        {
            if (_transform != null)
            {
                canvas.PopTransform();
            }
        }
    }

    /// <summary>
    /// 実際の3Dレンダリング処理
    /// サブクラスでオーバーライドする
    /// </summary>
    protected abstract void RenderCore3D(I3DCanvas canvas);

    /// <summary>
    /// 3Dバウンディングボックスを計算
    /// </summary>
    public virtual BoundingBox GetBounds3D()
    {
        // デフォルトでは原点中心の単位立方体
        return new BoundingBox(new Vector3(-0.5f), new Vector3(0.5f));
    }

    /// <summary>
    /// 変換行列を計算
    /// </summary>
    protected virtual Matrix4x4 CalculateTransformMatrix()
    {
        // スケール → 回転 → 平行移動の順で変換行列を構築
        var scaleMatrix = Matrix4x4.CreateScale(_scale);
        var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(_rotation.Y, _rotation.X, _rotation.Z);
        var translationMatrix = Matrix4x4.CreateTranslation(_translation);

        var result = scaleMatrix * rotationMatrix * translationMatrix;

        // カスタム変換があれば適用
        if (_transform != null)
        {
            result = result * _transform.Value;
        }

        return result;
    }

    /// <summary>
    /// マテリアルリソースを作成
    /// </summary>
    protected virtual I3DMaterialResource CreateMaterialResource()
    {
        // デフォルトマテリアルまたは設定されたマテリアルを使用
        var material = _material?.ToBasicMaterial() ?? BasicMaterial.CreateDefault();

        // 現在のレンダラーからマテリアルリソースを作成
        var renderer = Scene3DManager.Current?.Renderer;
        if (renderer != null)
        {
            return renderer.CreateMaterial(material);
        }

        throw new InvalidOperationException("3D renderer is not available");
    }

    /// <summary>
    /// リソースのクリーンアップ
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _materialResource?.Dispose();
            _materialResource = null;
        }

        base.Dispose(disposing);
    }
}
