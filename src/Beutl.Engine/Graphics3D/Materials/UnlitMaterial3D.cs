using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// アンリットマテリアル（照明なし）
/// </summary>
public class UnlitMaterial3D : Material3D
{
    public static readonly CoreProperty<Vector3> ColorProperty;
    public static readonly CoreProperty<ITextureResource?> TextureProperty;

    private Vector3 _color = Vector3.One;
    private ITextureResource? _texture;

    static UnlitMaterial3D()
    {
        ColorProperty = ConfigureProperty<Vector3, UnlitMaterial3D>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Vector3.One)
            .Register();

        TextureProperty = ConfigureProperty<ITextureResource?, UnlitMaterial3D>(nameof(Texture))
            .Accessor(o => o.Texture, (o, v) => o.Texture = v)
            .DefaultValue(null)
            .Register();

        AffectsRender(ColorProperty, TextureProperty);
    }

    /// <summary>
    /// 基本色
    /// </summary>
    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public Vector3 Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    /// <summary>
    /// テクスチャ
    /// </summary>
    [Display(Name = nameof(Strings.Texture), ResourceType = typeof(Strings))]
    public ITextureResource? Texture
    {
        get => _texture;
        set => SetAndRaise(TextureProperty, ref _texture, value);
    }

    public override BasicMaterial ToBasicMaterial()
    {
        return new BasicMaterial
        {
            Albedo = _color,
            Metallic = 0.0f,
            Roughness = 1.0f,
            Emission = Vector3.Zero,
            AlbedoTexture = _texture
        };
    }

    /// <summary>
    /// レンダリング無効化処理
    /// </summary>
    private static void AffectsRender(params CoreProperty[] properties)
    {
        foreach (var property in properties)
        {
            property.Changed.Subscribe(e =>
            {
                if (e.Sender is UnlitMaterial3D material)
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
