using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

/// <summary>Draws lines where the picture's lightness, color or transparency changes, for line art and masks.</summary>
[Display(Name = nameof(GraphicsStrings.EdgeDetection), ResourceType = typeof(GraphicsStrings))]
public sealed partial class EdgeDetection : FilterEffect
{
    private const float MaxThickness = 1000;

    public EdgeDetection() => ScanProperties<EdgeDetection>();

    [Display(Name = nameof(GraphicsStrings.EdgeDetection_Target), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<EdgeDetectionTarget> Target { get; } = Property.CreateAnimatable(EdgeDetectionTarget.Luminance);

    /// <summary>How far the line reaches to each side of an edge, in composition units.</summary>
    [Display(Name = nameof(GraphicsStrings.EdgeDetection_Thickness), ResourceType = typeof(GraphicsStrings))]
    [Range(0, MaxThickness)]
    public IExpressionProperty<float> Thickness { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.EdgeDetection_Threshold), Description = nameof(GraphicsStrings.EdgeDetection_Threshold_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Threshold { get; } = Property.CreateAnimatable(10f);

    /// <summary>
    /// How dark a change past the threshold draws its line, in percent. At 100 a change just past the threshold draws a
    /// solid line, and lower, the line darkens with the size of the change.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Strength), Description = nameof(GraphicsStrings.EdgeDetection_Strength_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.EdgeDetection_Original), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<EdgeDetectionOriginal> Original { get; } = Property.CreateAnimatable(EdgeDetectionOriginal.Hidden);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (!float.IsFinite(Thickness) || !float.IsFinite(Threshold) || !float.IsFinite(Strength))
                return;

            context.ShaderGraph(
                (Target, Thickness, Threshold: Threshold / 100, Gain: Strength / 100, Line: Color.ToLinearPremultiplied(), Original),
                static (graph, values) =>
                {
                    float offset = Math.Min(values.Thickness, 1);
                    // The line reaches past the picture on an outline. One more for filtering.
                    ShaderImage edges = graph.Image2D("edges", graph.Source, TextureFormat.R16Float, padding: values.Thickness + 1);
                    LightEffectPasses.ImagePass(graph, "edges", s_edges, graph.Source, edges, offset,
                        pass => pass.PushConstants(new Vector4(offset, values.Threshold, (float)values.Target, values.Gain)));

                    // Doubling the steps keeps a thick line cheap, and each stays within how wide the line already is,
                    // which leaves no gaps.
                    ShaderImage thick = edges;
                    int level = 0;
                    for (float step = 1, left = values.Thickness - offset; left > 0; step *= 2)
                    {
                        float distance = Math.Min(step, left);
                        left -= distance;
                        ShaderImage source = thick;
                        thick = graph.Image2D($"thicken-{++level}", source, TextureFormat.R16Float);
                        LightEffectPasses.ImagePass(graph, thick.Name, s_thicken, source, thick, distance,
                            pass => pass.PushConstants(distance));
                    }

                    ShaderImage output = graph.Image2D("output", thick);
                    graph.Fullscreen("composite", s_composite, pass =>
                    {
                        pass.SampledImage(0, graph.Source);
                        pass.SampledImage(1, thick);
                        pass.ImageParameters(2, graph.Source);
                        pass.ImageParameters(3, thick);
                        pass.ImageParameters(4, output);
                        pass.PushConstants(new CompositeValues(values.Line, (float)values.Original));
                        pass.InputRegion(0, ShaderInputRegion.Identity);
                        pass.InputRegion(1, ShaderInputRegion.Identity);
                        pass.PartialImages();
                        pass.ColorAttachment(0, output);
                    });
                    return graph.Output(output);
                });
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CompositeValues(Vector4 Line, float Original);

    // Sobel filter, scaled so that a full step gives 1. Lightness is gamma encoded, so that a threshold means the same
    // in the shadows and the highlights. Color is measured in Oklab, where black to white is a full step and two hues of
    // one lightness differ about as much as they look. A transparent neighbour has nothing to differ from, which keeps
    // the frame of a picture from drawing a line around it.
    private static readonly ShaderProgram s_edges = ShaderProgram.Fullscreen(new ShaderSource($$"""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { float offset; float threshold; float target; float gain; } pc;
        layout(location = 0) out float coverage;

        {{GlslSnippets.Luminance}}

        // Oklab, of a premultiplied linear color with its alpha taken out.
        vec3 oklab(vec4 premultiplied) {
            vec3 rgb = premultiplied.a > 0 ? premultiplied.rgb / premultiplied.a : vec3(0);
            vec3 lms = mat3(
                0.4122214708, 0.2119034982, 0.0883024619,
                0.5363325363, 0.6806995451, 0.2817188376,
                0.0514459929, 0.1073969566, 0.6299787005) * rgb;
            lms = sign(lms) * pow(abs(lms), vec3(1.0 / 3.0));
            return mat3(
                0.2104542553, 1.9779984951, 0.0259040371,
                0.7936177850, -2.4285922050, 0.7827717662,
                -0.0040720468, 0.4505937099, -0.8086757660) * lms;
        }

        vec4 read(vec2 position) {
            return texture(image, (position - src.raster.xy) / src.raster.zw);
        }

        // Lightness or transparency in the first component, or the color.
        vec3 measure(vec4 color) {
            return pc.target == 2 ? oklab(color) : vec3(pc.target == 1 ? color.a : gammaLightness(color), 0, 0);
        }

        vec3 value(vec2 position, vec3 own) {
            vec4 color = read(position);
            return pc.target == 1 ? vec3(color.a, 0, 0) : mix(own, measure(color), color.a);
        }

        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            vec4 center = read(position);
            vec3 own = measure(center);
            float d = pc.offset;
            vec3 tl = value(position + vec2(-d, -d), own);
            vec3 t = value(position + vec2(0, -d), own);
            vec3 tr = value(position + vec2(d, -d), own);
            vec3 l = value(position + vec2(-d, 0), own);
            vec3 r = value(position + vec2(d, 0), own);
            vec3 bl = value(position + vec2(-d, d), own);
            vec3 b = value(position + vec2(0, d), own);
            vec3 br = value(position + vec2(d, d), own);
            vec3 gx = tr + 2 * r + br - tl - 2 * l - bl;
            vec3 gy = bl + 2 * b + br - tl - 2 * t - tr;

            // Filtering takes a share of a change as small as the fraction of a pixel measured over, which would lose
            // the lines in a small preview. A short ramp above the threshold keeps them smooth. A lower strength
            // lengthens the ramp, so that a line darkens with the size of its change.
            float change = sqrt(dot(gx, gx) + dot(gy, gy)) / 4 / clamp(d * src.size.z, 0.001, 1);
            coverage = clamp((change - pc.threshold) / 0.1 * pc.gain, 0, 1);
            if (pc.target != 1) coverage *= center.a;
        }
        """));

    private static readonly ShaderProgram s_thicken = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { float distance; } pc;
        layout(location = 0) out float coverage;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            coverage = texture(image, (position - src.raster.xy) / src.raster.zw).r;

            // Enough directions for a thick line to stay round to within about a pixel: a step reaches furthest along
            // one and falls short between two, which fixed directions would turn into corners.
            float pixels = pc.distance * dst.size.z;
            int count = pixels < 8 ? 8 : pixels < 32 ? 16 : pixels < 128 ? 32 : 64;
            float angle = 6.2831853 / count;
            vec2 direction = vec2(1, 0);
            for (int i = 0; i < count; i++) {
                vec2 samplePosition = position + direction * pc.distance;
                coverage = max(coverage, texture(image, (samplePosition - src.raster.xy) / src.raster.zw).r);
                direction = vec2(direction.x * cos(angle) - direction.y * sin(angle), direction.x * sin(angle) + direction.y * cos(angle));
            }
        }
        """));

    private static readonly ShaderProgram s_composite = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1) uniform sampler2D edges;
        layout(binding = 2, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 3, std140) uniform Edges { vec4 logical; vec4 raster; vec4 size; } lines;
        layout(binding = 4, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { vec4 line; float original; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            vec4 line = pc.line * texture(edges, (position - lines.raster.xy) / lines.raster.zw).r;
            vec4 picture = pc.original != 0 ? texture(image, (position - src.raster.xy) / src.raster.zw) : vec4(0);
            color = pc.original == 2 ? picture + line * (1 - picture.a) : line + picture * (1 - line.a);
        }
        """));
}
