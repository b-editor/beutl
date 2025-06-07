using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3D環境設定クラス
/// </summary>
public class Environment3D : Animatable, IAffectsRender
{
    public static readonly CoreProperty<IEnvironmentMap?> EnvironmentMapProperty;
    public static readonly CoreProperty<float> ExposureProperty;
    public static readonly CoreProperty<float> RotationProperty;

    private IEnvironmentMap? _environmentMap;
    private float _exposure = 1.0f;
    private float _rotation = 0.0f;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    static Environment3D()
    {
        EnvironmentMapProperty = ConfigureProperty<IEnvironmentMap?, Environment3D>(nameof(EnvironmentMap))
            .Accessor(o => o.EnvironmentMap, (o, v) => o.EnvironmentMap = v)
            .DefaultValue(null)
            .Register();

        ExposureProperty = ConfigureProperty<float, Environment3D>(nameof(Exposure))
            .Accessor(o => o.Exposure, (o, v) => o.Exposure = v)
            .DefaultValue(1.0f)
            .Register();

        RotationProperty = ConfigureProperty<float, Environment3D>(nameof(Rotation))
            .Accessor(o => o.Rotation, (o, v) => o.Rotation = v)
            .DefaultValue(0.0f)
            .Register();

        AffectsRender(EnvironmentMapProperty, ExposureProperty, RotationProperty);
    }

    /// <summary>
    /// 環境マップ
    /// </summary>
    [Display(Name = nameof(Strings.EnvironmentMap), ResourceType = typeof(Strings))]
    public IEnvironmentMap? EnvironmentMap
    {
        get => _environmentMap;
        set => SetAndRaise(EnvironmentMapProperty, ref _environmentMap, value);
    }

    /// <summary>
    /// 露出
    /// </summary>
    [Display(Name = nameof(Strings.Exposure), ResourceType = typeof(Strings))]
    [Range(0.1f, 10.0f)]
    public float Exposure
    {
        get => _exposure;
        set => SetAndRaise(ExposureProperty, ref _exposure, Math.Max(0.1f, value));
    }

    /// <summary>
    /// 回転（ラジアン）
    /// </summary>
    [Display(Name = nameof(Strings.Rotation), ResourceType = typeof(Strings))]
    public float Rotation
    {
        get => _rotation;
        set => SetAndRaise(RotationProperty, ref _rotation, value);
    }

    private static void AffectsRender(params CoreProperty[] properties)
    {
        foreach (var property in properties)
        {
            property.Changed.Subscribe(e =>
            {
                if (e.Sender is Environment3D env)
                {
                    env.RaiseInvalidated();
                }
            });
        }
    }

    private void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this));
    }
}
