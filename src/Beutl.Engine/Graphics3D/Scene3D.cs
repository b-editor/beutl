using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Animation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dシーンクラス
/// 3Dオブジェクト、ライト、カメラ、環境設定を管理
/// </summary>
[Display(Name = "Scene3D")]
public class Scene3D : Animatable, I3DScene, IAffectsRender
{
    public static readonly CoreProperty<Drawable3Ds> ObjectsProperty;
    public static readonly CoreProperty<Light3Ds> LightsProperty;
    public static readonly CoreProperty<Camera3D?> CameraProperty;
    public static readonly CoreProperty<Environment3D?> EnvironmentProperty;
    public static readonly CoreProperty<Vector3> AmbientLightProperty;
    public static readonly CoreProperty<bool> EnableFogProperty;
    public static readonly CoreProperty<Vector3> FogColorProperty;
    public static readonly CoreProperty<float> FogDensityProperty;

    private readonly Drawable3Ds _objects = [];
    private readonly Light3Ds _lights = [];
    private Camera3D? _camera;
    private Environment3D? _environment;
    private Vector3 _ambientLight = new Vector3(0.1f);
    private bool _enableFog = false;
    private Vector3 _fogColor = new Vector3(0.5f);
    private float _fogDensity = 0.01f;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    static Scene3D()
    {
        ObjectsProperty = ConfigureProperty<Drawable3Ds, Scene3D>(nameof(Objects))
            .Accessor(o => o.Objects, (o, v) => o.Objects = v)
            .Register();

        LightsProperty = ConfigureProperty<Light3Ds, Scene3D>(nameof(Lights))
            .Accessor(o => o.Lights, (o, v) => o.Lights = v)
            .Register();

        CameraProperty = ConfigureProperty<Camera3D?, Scene3D>(nameof(Camera))
            .Accessor(o => o.Camera, (o, v) => o.Camera = v)
            .DefaultValue(null)
            .Register();

        EnvironmentProperty = ConfigureProperty<Environment3D?, Scene3D>(nameof(Environment))
            .Accessor(o => o.Environment, (o, v) => o.Environment = v)
            .DefaultValue(null)
            .Register();

        AmbientLightProperty = ConfigureProperty<Vector3, Scene3D>(nameof(AmbientLight))
            .Accessor(o => o.AmbientLight, (o, v) => o.AmbientLight = v)
            .DefaultValue(new Vector3(0.1f))
            .Register();

        EnableFogProperty = ConfigureProperty<bool, Scene3D>(nameof(EnableFog))
            .Accessor(o => o.EnableFog, (o, v) => o.EnableFog = v)
            .DefaultValue(false)
            .Register();

        FogColorProperty = ConfigureProperty<Vector3, Scene3D>(nameof(FogColor))
            .Accessor(o => o.FogColor, (o, v) => o.FogColor = v)
            .DefaultValue(new Vector3(0.5f))
            .Register();

        FogDensityProperty = ConfigureProperty<float, Scene3D>(nameof(FogDensity))
            .Accessor(o => o.FogDensity, (o, v) => o.FogDensity = v)
            .DefaultValue(0.01f)
            .Register();

        AffectsRender(ObjectsProperty, LightsProperty, CameraProperty, EnvironmentProperty,
            AmbientLightProperty, EnableFogProperty, FogColorProperty, FogDensityProperty);
    }

    public Scene3D()
    {
        _objects.Invalidated += (_, e) => RaiseInvalidated(e);
        _lights.Invalidated += (_, e) => RaiseInvalidated(e);

        // デフォルトカメラを作成
        _camera = new Camera3D
        {
            Position = new Vector3(0, 0, 5),
            Target = Vector3.Zero,
            Up = Vector3.UnitY
        };
    }

    /// <summary>
    /// 3Dオブジェクトのコレクション
    /// </summary>
    [NotAutoSerialized]
    public Drawable3Ds Objects
    {
        get => _objects;
        set => _objects.Replace(value);
    }

    /// <summary>
    /// ライトのコレクション
    /// </summary>
    [NotAutoSerialized]
    public Light3Ds Lights
    {
        get => _lights;
        set => _lights.Replace(value);
    }

    /// <summary>
    /// アクティブカメラ
    /// </summary>
    [Display(Name = "Camera",
        GroupName = nameof(Strings.Scene))]
    public Camera3D? Camera
    {
        get => _camera;
        set => SetAndRaise(CameraProperty, ref _camera, value);
    }

    /// <summary>
    /// 環境設定
    /// </summary>
    [Display(Name = "Environment",
        GroupName = nameof(Strings.Scene))]
    public Environment3D? Environment
    {
        get => _environment;
        set => SetAndRaise(EnvironmentProperty, ref _environment, value);
    }

    /// <summary>
    /// 環境光
    /// </summary>
    [Display(Name = "AmbientLight",
        GroupName = nameof(Strings.Lighting))]
    public Vector3 AmbientLight
    {
        get => _ambientLight;
        set => SetAndRaise(AmbientLightProperty, ref _ambientLight, Vector3.Max(value, Vector3.Zero));
    }

    /// <summary>
    /// フォグを有効にするかどうか
    /// </summary>
    [Display(Name = "EnableFog", GroupName = "Atmosphere")]
    public bool EnableFog
    {
        get => _enableFog;
        set => SetAndRaise(EnableFogProperty, ref _enableFog, value);
    }

    /// <summary>
    /// フォグの色
    /// </summary>
    [Display(Name = "FogColor", GroupName = "Atmosphere")]
    public Vector3 FogColor
    {
        get => _fogColor;
        set => SetAndRaise(FogColorProperty, ref _fogColor, Vector3.Clamp(value, Vector3.Zero, Vector3.One));
    }

    /// <summary>
    /// フォグの密度
    /// </summary>
    [Display(Name = "FogDensity", GroupName = "Atmosphere")]
    [Range(0.0f, 1.0f)]
    public float FogDensity
    {
        get => _fogDensity;
        set => SetAndRaise(FogDensityProperty, ref _fogDensity, Math.Clamp(value, 0.0f, 1.0f));
    }

    // I3DSceneインターフェースの実装
    IReadOnlyList<I3DRenderableObject> I3DScene.Objects =>
        _objects.GetVisible().Where(obj => obj is I3DRenderableObject).Cast<I3DRenderableObject>().ToList();

    IReadOnlyList<ILight> I3DScene.Lights =>
        _lights.GetVisible().Where(light => light.Enabled).Cast<ILight>().ToList();

    IEnvironmentMap? I3DScene.EnvironmentMap => _environment?.EnvironmentMap;

    /// <summary>
    /// オブジェクトを追加
    /// </summary>
    public void AddObject(Drawable3D obj)
    {
        _objects.Add(obj);
    }

    /// <summary>
    /// オブジェクトを削除
    /// </summary>
    public bool RemoveObject(Drawable3D obj)
    {
        return _objects.Remove(obj);
    }

    /// <summary>
    /// ライトを追加
    /// </summary>
    public void AddLight(Light3D light)
    {
        _lights.Add(light);
    }

    /// <summary>
    /// ライトを削除
    /// </summary>
    public bool RemoveLight(Light3D light)
    {
        return _lights.Remove(light);
    }

    /// <summary>
    /// シーンをレンダリング
    /// </summary>
    public void Render(I3DCanvas canvas)
    {
        if (_camera == null)
            return;

        // カメラ設定
        canvas.SetCamera(_camera);

        // 環境光設定
        canvas.SetAmbientLight(_ambientLight);

        // フォグ設定
        if (_enableFog)
        {
            canvas.SetFog(_fogColor, _fogDensity);
        }

        // ライトのセットアップ
        var activeLights = _lights.GetVisible().Where(light => light.Enabled).ToList();
        canvas.SetLights(activeLights.Cast<ILight>().ToList());

        // オブジェクトをレンダリング（Z-インデックス順）
        var visibleObjects = _objects.GetVisible().OrderBy(obj => obj.ZIndex);
        foreach (var obj in visibleObjects)
        {
            obj.Render3D(canvas);
        }
    }

    /// <summary>
    /// シーンのバウンディングボックスを取得
    /// </summary>
    public BoundingBox GetBounds()
    {
        return _objects.GetBounds();
    }

    /// <summary>
    /// レイとシーンオブジェクトの交差判定
    /// </summary>
    public Drawable3D? RaycastFirst(Ray ray, out float distance)
    {
        distance = float.MaxValue;
        Drawable3D? hitObject = null;

        foreach (var obj in _objects.GetVisible())
        {
            var bounds = obj.GetBounds3D().Transform(((I3DRenderableObject)obj).Transform);
            if (bounds.Intersects(ray, out float objDistance) && objDistance < distance)
            {
                distance = objDistance;
                hitObject = obj;
            }
        }

        return hitObject;
    }

    /// <summary>
    /// 複数の交差オブジェクトを取得
    /// </summary>
    public IEnumerable<(Drawable3D obj, float distance)> RaycastAll(Ray ray)
    {
        var hits = new List<(Drawable3D, float)>();

        foreach (var obj in _objects.GetVisible())
        {
            var bounds = obj.GetBounds3D().Transform(((I3DRenderableObject)obj).Transform);
            if (bounds.Intersects(ray, out float distance))
            {
                hits.Add((obj, distance));
            }
        }

        return hits.OrderBy(hit => hit.Item2);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Objects), Objects);
        context.SetValue(nameof(Lights), Lights);
        context.SetValue(nameof(Camera), Camera);
        context.SetValue(nameof(Environment), Environment);
        context.SetValue(nameof(AmbientLight), AmbientLight);
        context.SetValue(nameof(EnableFog), EnableFog);
        context.SetValue(nameof(FogColor), FogColor);
        context.SetValue(nameof(FogDensity), FogDensity);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        if (context.GetValue<Drawable3Ds>(nameof(Objects)) is { } objects)
        {
            Objects = objects;
        }

        if (context.GetValue<Light3Ds>(nameof(Lights)) is { } lights)
        {
            Lights = lights;
        }

        if (context.GetValue<Camera3D>(nameof(Camera)) is { } camera)
        {
            Camera = camera;
        }

        if (context.GetValue<Environment3D>(nameof(Environment)) is { } environment)
        {
            Environment = environment;
        }

        if (context.GetValue<Vector3>(nameof(AmbientLight)) is { } ambientLight)
        {
            AmbientLight = ambientLight;
        }

        if (context.GetValue<bool>(nameof(EnableFog)) is { } enableFog)
        {
            EnableFog = enableFog;
        }

        if (context.GetValue<Vector3>(nameof(FogColor)) is { } fogColor)
        {
            FogColor = fogColor;
        }

        if (context.GetValue<float>(nameof(FogDensity)) is { } fogDensity)
        {
            FogDensity = fogDensity;
        }
    }

    private static void AffectsRender(params CoreProperty[] properties)
    {
        foreach (var property in properties)
        {
            property.Changed.Subscribe(e =>
            {
                if (e.Sender is Scene3D scene)
                {
                    scene.RaiseInvalidated();
                }
            });
        }
    }

    private void RaiseInvalidated(RenderInvalidatedEventArgs? args = null)
    {
        Invalidated?.Invoke(this, args ?? new RenderInvalidatedEventArgs(this));
    }
}
