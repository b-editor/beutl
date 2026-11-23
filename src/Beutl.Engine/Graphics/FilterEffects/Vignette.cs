using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Vignette), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Vignette : FilterEffect
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform float2 center;
        uniform float2 inverseRadii;
        uniform float exponent;
        uniform float2 falloff;
        uniform float amount;

        float maskAt(float distance) {
            if (falloff.y > falloff.x) {
                return smoothstep(falloff.x, falloff.y, distance);
            }

            // A collapsed band is a hard step. Keep the center clear and the outer edge at full strength.
            if (distance <= 0.0 || distance >= 1.0) {
                return clamp(distance, 0.0, 1.0);
            }
            return distance < falloff.x ? 0.0 : (distance > falloff.x ? 1.0 : 0.5);
        }

        half4 main(float2 coord) {
            float2 offset = min(abs(coord - center) * inverseRadii, float2(1.0));
            float distance = exponent == 2.0
                ? length(offset)
                : pow(pow(offset.x, exponent) + pow(offset.y, exponent), 1.0 / exponent);
            float strength = abs(amount) * maskAt(distance);
            float4 color = src.eval(coord);
            if (amount > 0.0) {
                color.rgb *= 1.0 - strength;
            } else {
                color.rgb += (float3(color.a) - color.rgb) * strength;
            }
            return half4(color);
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.WholeSource);

    public Vignette()
    {
        ScanProperties<Vignette>();
    }

    [Display(Name = nameof(GraphicsStrings.Amount),
        Description = nameof(GraphicsStrings.Vignette_Amount_Description), ResourceType = typeof(GraphicsStrings))]
    [Range(-100, 100)]
    public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(GraphicsStrings.Vignette_Midpoint), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Midpoint { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(GraphicsStrings.Vignette_Roundness), ResourceType = typeof(GraphicsStrings))]
    [Range(-100, 100)]
    public IExpressionProperty<float> Roundness { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Vignette_Feather), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Feather { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(GraphicsStrings.Center), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> Center { get; } = Property.CreateAnimatable(RelativePoint.Center);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            float amount = ClampFinite(Amount, -100f, 100f, 0f) / 100f;
            if (amount == 0f)
                return;

            float midpoint = ClampFinite(Midpoint, 0f, 100f, 50f) / 100f;
            float feather = ClampFinite(Feather, 0f, 100f, 50f) / 100f;
            float roundness = ClampFinite(Roundness, -100f, 100f, 0f) / 100f;
            // Keep the band symmetric about its half-strength point and inside the center-to-corner interval.
            float halfWidth = MathF.Min(feather * 0.5f, MathF.Min(midpoint, 1f - midpoint));
            var falloff = new Vector2(midpoint - halfWidth, midpoint + halfWidth);
            float exponent = 2f + 6f * MathF.Max(-roundness, 0f);
            float circularity = MathF.Max(roundness, 0f);

            context.Shader(ShaderDescription.WholeSource(
                s_shaderSource,
                RenderBoundsContract.Identity,
                bindings =>
                {
                    bindings.Uniform("amount", amount);
                    bindings.Uniform("falloff", falloff);
                    bindings.Uniform("exponent", exponent);
                    bindings.Uniform("center", Center, static (writer, center, execution) =>
                    {
                        // Anchor to the complete logical source even when the output only requests part of it.
                        Rect bounds = execution.OutputBounds;
                        Vector2 position = RasterShaderMapping.ToStagePixels(
                            execution, bounds.Position + center.ToPixels(bounds.Size));
                        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
                            position = RasterShaderMapping.ToStagePixels(execution, bounds.Center);
                        writer.Set(position);
                    });
                    bindings.Uniform("inverseRadii", (Circularity: circularity, Exponent: exponent), static (writer, shape, execution) =>
                    {
                        // Positive roundness brings the two axes together. Negative roundness keeps the aspect
                        // ratio and raises the superellipse exponent. The centered corner is at distance one.
                        Rect bounds = execution.OutputBounds;
                        var halfSize = Vector2.Max(new Vector2(bounds.Width, bounds.Height) * 0.5f, new Vector2(1e-6f));
                        var circle = new Vector2(MathF.Max(halfSize.X, halfSize.Y));
                        Vector2 axes = Vector2.Lerp(halfSize, circle, shape.Circularity);
                        Vector2 corner = halfSize / axes;
                        float cornerDistance = shape.Exponent == 2f
                            ? corner.Length()
                            : MathF.Pow(2f, 1f / shape.Exponent);
                        writer.Set(new Vector2(1f) / (axes * (cornerDistance * execution.OutputPlacement.Scale.Value)));
                    });
                },
                SKShaderTileMode.Decal,
                hitTest: RenderHitTestContract.AnyInput));
        }

        private static float ClampFinite(float value, float min, float max, float fallback)
            => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    }
}
