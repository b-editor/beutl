using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

using Beutl.Language;
using Beutl.Serialization;
using Beutl.Serialization.Migration;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class Blur : FilterEffect
{
    public static readonly CoreProperty<Size> SigmaProperty;
    private Size _sigma;

    static Blur()
    {
        SigmaProperty = ConfigureProperty<Size, Blur>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(Size.Empty)
            .Register();

        AffectsRender<Blur>(SigmaProperty);
    }

    [Display(Name = nameof(Strings.Sigma), ResourceType = typeof(Strings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public Size Sigma
    {
        get => _sigma;
        set => SetAndRaise(SigmaProperty, ref _sigma, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Blur(_sigma);
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return bounds.Inflate(new Thickness(_sigma.Width * 3, _sigma.Height * 3));
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
}
