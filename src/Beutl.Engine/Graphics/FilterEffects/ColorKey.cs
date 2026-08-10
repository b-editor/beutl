using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.ColorKey), ResourceType = typeof(GraphicsStrings))]
public partial class ColorKey : FilterEffect
{
    private const string ShaderSource =
        """
        uniform float3 keyColor;
        uniform float range;
        uniform float boundary;

        half calcLuma(half3 value) {
            return dot(value, half3(0.2126, 0.7152, 0.0722));
        }

        half4 apply(half4 color) {
            half alpha = color.a;
            if (alpha <= 0.0001) return half4(0.0);
            half3 rgb = color.rgb / alpha;

            half luma = calcLuma(rgb);
            half keyLuma = calcLuma(keyColor);

            half diff = abs(luma - keyLuma);
            half mask = smoothstep(range, range + boundary, diff);

            return color * mask;
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.CurrentPixel);

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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        Vector4 linear = r.Color.ToLinear();
        context.Shader(ShaderDescription.CurrentPixel(
            s_shaderSource,
            bindings =>
            {
                bindings.Uniform("keyColor", new Vector3(linear.X, linear.Y, linear.Z));
                bindings.Uniform("range", r.Range / 100f);
                bindings.Uniform("boundary", r.Boundary / 100f);
            }));
    }
}
