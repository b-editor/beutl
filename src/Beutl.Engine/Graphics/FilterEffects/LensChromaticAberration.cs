using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
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
        layout(push_constant) uniform Parameters { vec2 center; vec2 inverseScale; float repeatEdgePixels; } pc;

        vec4 sampleAt(vec2 position) {
            return textureLod(srcTexture, (position - source.raster.xy) / source.raster.zw, 0);
        }

        void main() {
            vec2 position = outputFrame.raster.xy + gl_FragCoord.xy / outputFrame.pixels.z;
            vec2 offset = position - pc.center;
            vec2 redAt = pc.center + offset * pc.inverseScale.x;
            vec2 greenAt = position;
            vec2 blueAt = pc.center + offset * pc.inverseScale.y;
            bvec2 narrow = bvec2(false);
            if (pc.repeatEdgePixels != 0.0) {
                // Reads stop at the centers of the outermost source pixels that lie wholly inside its bounds, so a read
                // past an edge repeats that edge, never a padded apron or a partly covered pixel. The tolerance absorbs
                // rounding where an edge falls on a pixel boundary.
                vec2 first = ceil((source.bounds.xy - source.raster.xy) * source.pixels.z - 1.0 / 64.0);
                vec2 last = floor((source.bounds.xy + source.bounds.zw - source.raster.xy) * source.pixels.z + 1.0 / 64.0) - 1.0;
                // A picture narrower than a whole pixel repeats its middle.
                narrow = lessThan(last, first);
                vec2 middle = source.bounds.xy + source.bounds.zw * 0.5;
                vec2 low = mix(source.raster.xy + (first + 0.5) / source.pixels.z, middle, narrow);
                vec2 high = mix(source.raster.xy + (last + 0.5) / source.pixels.z, middle, narrow);
                redAt = clamp(redAt, low, high);
                greenAt = clamp(greenAt, low, high);
                blueAt = clamp(blueAt, low, high);
            }
            vec4 red = sampleAt(redAt);
            vec4 green = sampleAt(greenAt);
            vec4 blue = sampleAt(blueAt);
            vec4 color = vec4(red.r, green.g, blue.b, max(max(red.a, green.a), blue.a));
            if (pc.repeatEdgePixels != 0.0) {
                // The reads above skip partly covered pixels, so give each output pixel the share of it that the
                // picture's bounds cover. A share within 1/64 of nothing or of a whole pixel is rounding.
                float halfPixel = 0.5 / outputFrame.pixels.z;
                vec2 covered = clamp(
                    (min(position + halfPixel, source.bounds.xy + source.bounds.zw) - max(position - halfPixel, source.bounds.xy))
                        * outputFrame.pixels.z,
                    0.0, 1.0);
                covered = mix(covered, round(covered), lessThan(min(covered, 1.0 - covered), vec2(1.0 / 64.0)));
                // Along an axis narrower than a pixel, the middle read already carries the picture's coverage, so only
                // keep the pixels the picture touches.
                covered = mix(covered, ceil(covered), narrow);
                color *= covered.x * covered.y;
            }
            outColor = color;
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

    [Display(Name = nameof(GraphicsStrings.LensChromaticAberration_RepeatEdgePixels),
        Description = nameof(GraphicsStrings.LensChromaticAberration_RepeatEdgePixels_Description), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> RepeatEdgePixels { get; } = Property.CreateAnimatable(true);

    // The center is written over with the one each request resolves.
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Parameters(Vector2 Center, Vector2 InverseScale, float RepeatEdgePixels);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (!float.IsFinite(Amount) || Amount == 0
                || !float.IsFinite(Center.Point.X) || !float.IsFinite(Center.Point.Y))
                return;

            // Interpolation and detached resources can bypass the editor's range.
            float amount = Math.Clamp(Amount, -10f, 10f) / 100f;
            context.ShaderGraph((Center, RedScale: 1 + amount, BlueScale: 1 - amount, RepeatEdgePixels), static (graph, state) =>
            {
                float inverseRed = 1 / state.RedScale;
                float inverseBlue = 1 / state.BlueScale;
                ShaderImage output = graph.Image2D("lens-chromatic-aberration", graph.Source);
                // Repeated edge pixels keep the picture within its bounds. Otherwise the scaled channels reach past them.
                if (!state.RepeatEdgePixels)
                {
                    output.BoundsTransforms =
                    [
                        Matrix.CreateScale(state.RedScale, state.RedScale),
                        Matrix.CreateScale(state.BlueScale, state.BlueScale),
                    ];
                    output.BoundsOrigin = state.Center;
                }

                graph.Fullscreen(output.Name, s_program, pass =>
                {
                    pass.SampledImage(0, graph.Source, SamplerState.LinearTransparent);
                    pass.ImageParameters(1, graph.Source);
                    pass.ImageParameters(2, output);
                    pass.PushConstants(new Parameters(default, new Vector2(inverseRed, inverseBlue), state.RepeatEdgePixels ? 1 : 0));
                    pass.BoundsOriginConstants(state.Center, 0);
                    // A read held at an edge lies between the point and where it would have read, and this footprint,
                    // which holds the point too, covers all of that.
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
