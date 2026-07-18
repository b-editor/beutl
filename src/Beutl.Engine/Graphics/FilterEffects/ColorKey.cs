using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.ColorKey), ResourceType = typeof(GraphicsStrings))]
public partial class ColorKey : FilterEffect
{
    // Fusable snippet; `c` is the premultiplied linear-light source pixel (contract A2).
    private static readonly string s_snippet =
        """
        uniform float4 color;
        uniform float range;
        uniform float boundary;

        half calcLuma(half3 c) {
            return dot(c, half3(0.2126, 0.7152, 0.0722));
        }

        half4 apply(half4 c) {
            half alpha = c.a;
            if (alpha <= 0.0001) return half4(0.0);
            half3 rgb = c.rgb / alpha;

            half luma = calcLuma(rgb);
            half keyLuma = calcLuma(color.rgb);

            half diff = abs(luma - keyLuma);
            half mask = smoothstep(range, range + boundary, diff);

            return c * mask;
        }
        """;

    public ColorKey()
    {
        ScanProperties<ColorKey>();
    }

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable<Color>();

    [Display(Name = nameof(GraphicsStrings.ColorKey_Range), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Range { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ColorKey_Boundary), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Boundary { get; } = Property.CreateAnimatable(2f);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        Color color = r.Color;
        builder.Shader(ShaderNodeDescriptor.Snippet(
            s_snippet,
            u => u.Raw("color", (b, name) => b.Uniforms[name] = color.ToLinear().ToSKColorF())
                .Float("range", r.Range / 100f)
                .Float("boundary", r.Boundary / 100f)));
    }
}
