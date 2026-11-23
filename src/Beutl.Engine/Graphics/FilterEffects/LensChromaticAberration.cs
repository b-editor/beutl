using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.LensChromaticAberration), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LensChromaticAberration : FilterEffect
{
    private static readonly ShaderProgram s_program = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(location = 0) out vec4 outColor;
        layout(binding = 0) uniform sampler2D srcTexture;
        layout(binding = 1) uniform SourceFrame { vec4 bounds; vec4 raster; vec4 pixels; } source;
        layout(binding = 2) uniform OutputFrame { vec4 bounds; vec4 raster; vec4 pixels; } outputFrame;
        layout(push_constant) uniform Parameters { vec2 center; vec2 inverseScale; } pc;

        vec4 sampleAt(vec2 position) {
            return textureLod(srcTexture, (position - source.raster.xy) / source.raster.zw, 0);
        }

        void main() {
            vec2 position = outputFrame.raster.xy + gl_FragCoord.xy / outputFrame.pixels.z;
            vec2 offset = position - pc.center;
            vec4 red = sampleAt(pc.center + offset * pc.inverseScale.x);
            vec4 green = sampleAt(position);
            vec4 blue = sampleAt(pc.center + offset * pc.inverseScale.y);
            outColor = vec4(red.r, green.g, blue.b, max(max(red.a, green.a), blue.a));
        }
        """));

    public LensChromaticAberration()
    {
        ScanProperties<LensChromaticAberration>();
    }

    [Display(Name = nameof(GraphicsStrings.Amount),
        Description = nameof(GraphicsStrings.LensChromaticAberration_Amount_Description), ResourceType = typeof(GraphicsStrings))]
    [Range(-10, 10)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.Center), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> Center { get; } = Property.CreateAnimatable(RelativePoint.Center);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (!float.IsFinite(Amount) || Amount == 0
                || !float.IsFinite(Center.Point.X) || !float.IsFinite(Center.Point.Y))
                return;

            // Interpolation and detached resources can bypass the editor's range.
            float amount = Math.Clamp(Amount, -10f, 10f) / 100f;
            context.ShaderGraph((Center, RedScale: 1 + amount, BlueScale: 1 - amount), static (graph, state) =>
            {
                float inverseRed = 1 / state.RedScale;
                float inverseBlue = 1 / state.BlueScale;
                ShaderImage output = graph.Image2D("lens-chromatic-aberration", graph.Source);
                output.BoundsTransforms =
                [
                    Matrix.CreateScale(state.RedScale, state.RedScale),
                    Matrix.CreateScale(state.BlueScale, state.BlueScale),
                ];
                output.BoundsOrigin = state.Center;

                graph.Fullscreen(output.Name, s_program, pass =>
                {
                    pass.SampledImage(0, graph.Source, SamplerState.LinearTransparent);
                    pass.ImageParameters(1, graph.Source);
                    pass.ImageParameters(2, output);
                    pass.PushConstants(new Vector4(0, 0, inverseRed, inverseBlue));
                    pass.BoundsOriginConstants(state.Center, 0);
                    pass.InputRegionAbout(0, state.Center,
                    [
                        Matrix.CreateScale(inverseRed, inverseRed),
                        Matrix.Identity,
                        Matrix.CreateScale(inverseBlue, inverseBlue),
                    ]);
                    pass.PartialImages();
                    pass.ColorAttachment(0, output);
                });

                return graph.Output(output);
            });
        }
    }
}
