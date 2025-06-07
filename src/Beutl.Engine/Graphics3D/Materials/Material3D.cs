using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dマテリアルのベースクラス
/// PBR（物理ベースレンダリング）マテリアルをサポート
/// </summary>
public abstract class Material3D : Animatable, IAffectsRender
{
    public static readonly CoreProperty<bool> IsEnabledProperty;

    private bool _isEnabled = true;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    static Material3D()
    {
        IsEnabledProperty = ConfigureProperty<bool, Material3D>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();

        AffectsRender(IsEnabledProperty);
    }

    /// <summary>
    /// マテリアルが有効かどうか
    /// </summary>
    [Display(Name = nameof(Strings.IsEnabled), ResourceType = typeof(Strings))]
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    /// <summary>
    /// BasicMaterialに変換
    /// </summary>
    public abstract BasicMaterial ToBasicMaterial();

    /// <summary>
    /// レンダリング無効化処理
    /// </summary>
    private static void AffectsRender(params CoreProperty[] properties)
    {
        foreach (var property in properties)
        {
            property.Changed.Subscribe(e =>
            {
                if (e.Sender is Material3D material)
                {
                    material.RaiseInvalidated();
                }
            });
        }
    }

    private void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this));
    }
}
