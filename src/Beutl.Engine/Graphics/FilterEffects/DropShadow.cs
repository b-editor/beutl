using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Serialization.Migration;

namespace Beutl.Graphics.Effects;

public sealed class DropShadow : FilterEffect
{
    public static readonly CoreProperty<Point> PositionProperty;
    public static readonly CoreProperty<Size> SigmaProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<bool> ShadowOnlyProperty;
    private Point _position;
    private Size _sigma;
    private Color _color;
    private bool _shadowOnly;

    static DropShadow()
    {
        PositionProperty = ConfigureProperty<Point, DropShadow>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue(new Point())
            .Register();

        SigmaProperty = ConfigureProperty<Size, DropShadow>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(Size.Empty)
            .Register();

        ColorProperty = ConfigureProperty<Color, DropShadow>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.Transparent)
            .Register();

        ShadowOnlyProperty = ConfigureProperty<bool, DropShadow>(nameof(ShadowOnly))
            .Accessor(o => o.ShadowOnly, (o, v) => o.ShadowOnly = v)
            .DefaultValue(false)
            .Register();

        AffectsRender<DropShadow>(PositionProperty, SigmaProperty, ColorProperty, ShadowOnlyProperty);
    }

    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
    public Point Position
    {
        get => _position;
        set => SetAndRaise(PositionProperty, ref _position, value);
    }

    [Display(Name = nameof(Strings.Sigma), ResourceType = typeof(Strings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public Size Sigma
    {
        get => _sigma;
        set => SetAndRaise(SigmaProperty, ref _sigma, value);
    }

    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public Color Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    [Display(Name = nameof(Strings.ShadowOnly), ResourceType = typeof(Strings))]
    public bool ShadowOnly
    {
        get => _shadowOnly;
        set => SetAndRaise(ShadowOnlyProperty, ref _shadowOnly, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        if (ShadowOnly)
            context.DropShadowOnly(Position, Sigma, Color);
        else
            context.DropShadow(Position, Sigma, Color);
    }

    public override Rect TransformBounds(Rect bounds)
    {
        Rect shadowBounds = bounds
            .Translate(_position)
            .Inflate(new Thickness(_sigma.Width * 3, _sigma.Height * 3));

        return _shadowOnly ? shadowBounds : bounds.Union(shadowBounds);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        // Todo: 互換性処理
        if (context is IJsonSerializationContext jsonContext)
        {
            JsonObject json = jsonContext.GetJsonObject();

            try
            {
                JsonNode? animations = json["Animations"] ?? json["animations"];
                JsonNode? sigma = animations?[nameof(Sigma)];

                if (sigma != null)
                {
                    Migration_ChangeSigmaType.Update(sigma);
                }
            }
            catch
            {
            }
        }

        base.Deserialize(context);
    }

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        try
        {
            JsonNode? animations = json["Animations"] ?? json["animations"];
            JsonNode? sigma = animations?[nameof(Sigma)];

            if (sigma != null)
            {
                Migration_ChangeSigmaType.Update(sigma);
            }
        }
        catch
        {
        }

        base.ReadFromJson(json);
    }
}
