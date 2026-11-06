using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

/// <summary>Draws lines where the picture's lightness or transparency changes, for line art and masks.</summary>
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

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.EdgeDetection_Original), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<EdgeDetectionOriginal> Original { get; } = Property.CreateAnimatable(EdgeDetectionOriginal.Hidden);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (!float.IsFinite(Thickness) || !float.IsFinite(Threshold))
                return;

            context.ShaderGraph(
                (Target, Thickness, Threshold: Threshold / 100, Line: Color.ToLinearPremultiplied(), Original),
                static (graph, values) =>
                {
                    float offset = Math.Min(values.Thickness, 1);
                    // The line reaches past the picture on an outline. One more for filtering.
                    ShaderImage edges = graph.Image2D("edges", graph.Source, TextureFormat.R16Float, padding: values.Thickness + 1);
                    LightEffectPasses.ImagePass(graph, "edges", s_edges, graph.Source, edges, offset,
                        pass => pass.PushConstants(new Vector3(offset, values.Threshold, values.Target == EdgeDetectionTarget.Alpha ? 1 : 0)));

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
    // in the shadows and the highlights. A transparent neighbour has no lightness to differ from, which keeps the frame
    // of a picture from drawing a line around it.
    private static readonly ShaderProgram s_edges = ShaderProgram.Fullscreen(new ShaderSource($$"""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { float offset; float threshold; float alpha; } pc;
        layout(location = 0) out float coverage;

        {{GlslSnippets.Luminance}}

        vec4 read(vec2 position) {
            return texture(image, (position - src.raster.xy) / src.raster.zw);
        }

        float value(vec2 position, float own) {
            vec4 color = read(position);
            return pc.alpha != 0 ? color.a : mix(own, gammaLightness(color), color.a);
        }

        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            vec4 center = read(position);
            float own = pc.alpha != 0 ? center.a : gammaLightness(center);
            float d = pc.offset;
            float tl = value(position + vec2(-d, -d), own);
            float t = value(position + vec2(0, -d), own);
            float tr = value(position + vec2(d, -d), own);
            float l = value(position + vec2(-d, 0), own);
            float r = value(position + vec2(d, 0), own);
            float bl = value(position + vec2(-d, d), own);
            float b = value(position + vec2(0, d), own);
            float br = value(position + vec2(d, d), own);
            float gx = tr + 2 * r + br - tl - 2 * l - bl;
            float gy = bl + 2 * b + br - tl - 2 * t - tr;

            // Filtering takes a share of a change as small as the fraction of a pixel measured over, which would lose
            // the lines in a small preview. A short ramp above the threshold keeps them smooth.
            float strength = length(vec2(gx, gy)) / 4 / clamp(d * src.size.z, 0.001, 1);
            coverage = clamp((strength - pc.threshold) / 0.1, 0, 1);
            if (pc.alpha == 0) coverage *= center.a;
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
