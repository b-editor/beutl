using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Keeps the brightness between two limits and makes the rest transparent. Brightness is the luminance of linear light,
/// as <see cref="Threshold"/> reads it, and highlights above full scale count as full scale.
/// </summary>
[Display(Name = nameof(GraphicsStrings.LumaKey), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LumaKey : FilterEffect
{
    private const string ShaderSource =
        """
        uniform float lower;
        uniform float upper;
        uniform float softness;
        uniform float inverted;

        half4 apply(half4 color) {
            float alpha = color.a;
            if (alpha <= 0.0001) return half4(0.0);

            float luma = min(dot(color.rgb / alpha, float3(0.2126, 0.7152, 0.0722)), 1.0);

            // smoothstep over a zero-width band differs by backend (Skia's CPU returns NaN, Metal 0), so a band with
            // no width is written out as a hard edge.
            float aboveLower = softness > 0.0
                ? smoothstep(lower - softness, lower, luma)
                : (luma >= lower ? 1.0 : 0.0);
            float belowUpper = softness > 0.0
                ? 1.0 - smoothstep(upper, upper + softness, luma)
                : (luma <= upper ? 1.0 : 0.0);
            float keep = aboveLower * belowUpper;

            return color * half(mix(keep, 1.0 - keep, inverted));
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.CurrentPixel);

    public LumaKey()
    {
        ScanProperties<LumaKey>();
    }

    [Display(Name = nameof(GraphicsStrings.LumaKey_Lower), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Lower { get; } = Property.CreateAnimatable(25f);

    [Display(Name = nameof(GraphicsStrings.LumaKey_Upper), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Upper { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.LumaKey_Softness), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Softness { get; } = Property.CreateAnimatable(5f);

    [Display(Name = nameof(GraphicsStrings.Invert), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> Invert { get; } = Property.CreateAnimatable(false);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.Shader(ShaderDescription.CurrentPixel(
                s_shaderSource,
                bindings =>
                {
                    bindings.Uniform("lower", Lower / 100f);
                    bindings.Uniform("upper", Upper / 100f);
                    bindings.Uniform("softness", Softness / 100f);
                    bindings.Uniform("inverted", Invert ? 1f : 0f);
                }));
        }
    }
}
