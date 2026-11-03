using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Gives each pixel the color of a gradient at the place that its lightness points to. Lightness is the sRGB encoded
/// luminance, so that the middle of the gradient falls on the middle tones, and it stops at both ends of the gradient.
/// The pixel's alpha is kept, and multiplied by the gradient's.
/// </summary>
[Display(Name = nameof(GraphicsStrings.GradientMap), ResourceType = typeof(GraphicsStrings))]
public sealed partial class GradientMap : FilterEffect
{
    private const string ShaderSource =
        SkslSnippets.SrgbTransfer
        + """
        uniform shader ramp;
        uniform float inputBlack;
        uniform float inputWhite;
        uniform float strength;

        half4 apply(half4 color) {
            if (color.a <= 0.0) return color;

            float luminance = dot(float3(color.rgb) / color.a, float3(0.2126, 0.7152, 0.0722));
            float lightness = linearToSrgb(float3(luminance)).r;
            float range = inputWhite - inputBlack;
            float position = abs(range) > 0.00001 ? saturate((lightness - inputBlack) / range) : step(inputBlack, lightness);
            half4 mapped = ramp.eval(float2(position, 0.5)) * color.a;
            return mix(color, mapped, half(strength));
        }
        """;

    private static readonly SkslSource s_shaderSource = new(ShaderSource, ShaderDescriptionKind.CurrentPixel);

    public GradientMap()
    {
        ScanProperties<GradientMap>();
        GradientStops.Add(new GradientStop(Colors.Black, 0));
        GradientStops.Add(new GradientStop(Colors.White, 1));
    }

    [Display(Name = nameof(GraphicsStrings.GradientBrush_GradientStops), ResourceType = typeof(GraphicsStrings))]
    public IListProperty<GradientStop> GradientStops { get; } = Property.CreateList<GradientStop>();

    // Mixing in sRGB leaves a gray picture as it is with a gradient from black to white.
    [Display(Name = nameof(GraphicsStrings.GradientBrush_InterpolationSpace), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<GradientInterpolationSpace> InterpolationSpace { get; }
        = Property.CreateExpression(GradientInterpolationSpace.Srgb);

    [Display(Name = nameof(GraphicsStrings.GradientBrush_InterpolatePremultiplied), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> InterpolatePremultiplied { get; } = Property.CreateExpression(false);

    [Display(Name = nameof(GraphicsStrings.GradientMap_InputBlack), Description = nameof(GraphicsStrings.GradientMap_InputBlack_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> InputBlack { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.GradientMap_InputWhite), Description = nameof(GraphicsStrings.GradientMap_InputWhite_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> InputWhite { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.GradientMap_Strength), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (GradientStops.Count == 0 || !float.IsFinite(Strength) || Strength <= 0)
                return;

            (SKColor[] colors, float[] offsets) = GradientStopInterpolation.Expand(
                GradientStops.Select(stop => stop.Color.ToSKColor()).ToArray(),
                GradientStops.Select(stop => stop.Offset).ToArray(),
                InterpolationSpace,
                InterpolatePremultiplied);
            RenderResource<Ramp> ramp = context.Borrow(new Ramp(colors, offsets));

            context.Shader(ShaderDescription.CurrentPixel(s_shaderSource, bindings =>
            {
                bindings.Resource(
                    "ramp", ramp, ShaderResourceCoordinateSpace.Value, static (writer, value, _) => writer.Set(value.CreateShader()));
                bindings.Uniform("inputBlack", InputBlack / 100f);
                bindings.Uniform("inputWhite", InputWhite / 100f);
                bindings.Uniform("strength", Math.Min(Strength, 100) / 100f);
            }));
        }
    }

    // The renderer asks for a shader of its own each time it binds one.
    private sealed class Ramp(SKColor[] colors, float[] offsets)
    {
        public SKShader CreateShader()
            => SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0), colors, offsets, SKShaderTileMode.Clamp);
    }
}
