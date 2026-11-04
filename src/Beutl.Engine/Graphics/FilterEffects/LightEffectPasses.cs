using System.Numerics;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Effects;

/// <summary>Shared linear-light extraction and composition for bloom and light-ray effects, and a blur for any effect.</summary>
internal static class LightEffectPasses
{
    // softKnee is the transition half-width relative to threshold, in [0, 1].
    public static void ExtractHighlights(ShaderGraphBuilder graph, ShaderImage source, ShaderImage highlights,
        ShaderImage mask, float threshold, float softKnee)
        => graph.Fullscreen("extract", s_extract, pass =>
        {
            pass.SampledImage(0, source);
            pass.ImageParameters(1, source);
            pass.ImageParameters(2, highlights);
            pass.PushConstants(new Vector2(threshold, softKnee));
            pass.InputRegion(0, ShaderInputRegion.Identity);
            pass.ColorAttachment(0, highlights);
            pass.ColorAttachment(1, mask);
        });

    public static void Composite(ShaderGraphBuilder graph, ShaderImage source, ShaderImage light, ShaderImage mask,
        ShaderImage output, float intensity, bool preserveAlpha)
        => graph.Fullscreen("composite", s_composite, pass =>
        {
            pass.SampledImage(0, source);
            pass.SampledImage(1, light);
            pass.SampledImage(2, mask);
            pass.ImageParameters(3, source);
            pass.ImageParameters(4, light);
            pass.ImageParameters(5, output);
            pass.ImageParameters(6, mask);
            pass.PushConstants(new Vector2(intensity, preserveAlpha ? 1 : 0));
            pass.InputRegion(0, ShaderInputRegion.Identity);
            pass.InputRegion(1, ShaderInputRegion.Identity);
            pass.InputRegion(2, ShaderInputRegion.Identity);
            pass.ColorAttachment(0, output);
        });

    /// <summary>Blurs in two directions over <paramref name="radius"/>, which is three standard deviations.</summary>
    public static ShaderImage GaussianBlur(ShaderGraphBuilder graph, ShaderImage image, float radius)
    {
        // Reduce in 2x steps so even a one-pixel highlight survives the filtering.
        // The final blur radius stays at most 16 texels per unit of working scale.
        ShaderImage reduced = image;
        for (int level = 1; radius * reduced.ResolutionScale > 16; level++)
            reduced = Downsample(graph, $"downsample-{level}", reduced);
        ShaderImage horizontal = graph.Image2D("horizontal", reduced, resolutionScale: reduced.ResolutionScale);
        ShaderImage blurred = graph.Image2D("blurred", reduced, resolutionScale: reduced.ResolutionScale);
        Blur(graph, "blur-horizontal", reduced, horizontal, new(radius, 1, 0, 0));
        Blur(graph, "blur-vertical", horizontal, blurred, new(radius, 0, 1, 0));
        return blurred;
    }

    private static void Blur(ShaderGraphBuilder graph, string name, ShaderImage source, ShaderImage output, Vector4 values)
        => ImagePass(graph, name, s_blur, source, output, values.X, pass => pass.PushConstants(values));

    /// <summary>Reduces <paramref name="source"/> into a new image of half its raster density, and returns that image.</summary>
    public static ShaderImage Downsample(ShaderGraphBuilder graph, string name, ShaderImage source, float padding = 0)
    {
        ShaderImage destination = graph.Image2D(name, source, padding: padding, resolutionScale: source.ResolutionScale / 2);
        ImagePass(graph, name, s_downsample, source, destination);
        return destination;
    }

    /// <summary>A fullscreen pass that reads one image, for a shader that binds it at 0 and the two frames at 1 and 2.</summary>
    /// <param name="reach">How far from an output point the shader reads, in composition units.</param>
    public static void ImagePass(ShaderGraphBuilder graph, string name, ShaderProgram program, ShaderImage source,
        ShaderImage destination, float reach = 0, Action<ShaderPassBuilder>? configure = null)
        => graph.Fullscreen(name, program, pass =>
        {
            pass.SampledImage(0, source);
            pass.ImageParameters(1, source);
            pass.ImageParameters(2, destination);
            pass.InputRegion(0, new(Matrix.Identity, reach));
            pass.ColorAttachment(0, destination);
            configure?.Invoke(pass);
        });

    // Sample count depends on working scale, but no longer grows with the logical radius.
    private static readonly ShaderProgram s_blur = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { vec4 value; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            color = vec4(0);
            float total = 0;
            int steps = max(16, int(ceil(pc.value.x * src.size.z)));
            for (int i = -steps; i <= steps; i++) {
                float x = float(i) / float(steps);
                float weight = exp(-4.5 * x * x);
                vec2 samplePosition = position + pc.value.yz * (x * pc.value.x);
                color += texture(image, (samplePosition - src.raster.xy) / src.raster.zw) * weight;
                total += weight;
            }
            color /= total;
        }
        """));

    // The separable [1, 3, 3, 1] / 8 filter preserves small highlights and their center
    // as the grid gets coarser. Four bilinear samples cover its 4x4 source footprint.
    private static readonly ShaderProgram s_downsample = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            vec2 uv = (position - src.raster.xy) / src.raster.zw;
            vec2 offset = vec2(0.75) / src.size.xy;
            color = (texture(image, uv + offset) + texture(image, uv - offset)
                + texture(image, uv + vec2(offset.x, -offset.y))
                + texture(image, uv + vec2(-offset.x, offset.y))) * 0.25;
        }
        """));

    private static readonly ShaderProgram s_extract = ShaderProgram.Fullscreen(new ShaderSource($$"""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { float threshold; float softKnee; } pc;
        layout(location = 0) out vec4 bright;
        layout(location = 1) out float mask;
        {{GlslSnippets.Luminance}}
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            vec4 color = texture(image, (position - src.raster.xy) / src.raster.zw);
            // Select by straight luminance, then scale premultiplied RGBA so translucent edges fade naturally.
            float luminance = straightLuminance(color);
            float excess = luminance - pc.threshold;
            float contribution = max(excess, 0);
            float knee = pc.threshold * pc.softKnee;
            if (knee > 0) {
                // Quadratic transition with matching value/slope at threshold +/- knee.
                // Normalize first to avoid squaring HDR luminance or dividing by a zero-width knee.
                float t = clamp(0.5 * (excess / knee) + 0.5, 0, 1);
                contribution = max(contribution, t * t * knee);
            }
            float weight = luminance > 0 ? contribution / luminance : 0;
            bright = color * weight;
            mask = color.a;
        }
        """));

    private static readonly ShaderProgram s_composite = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1) uniform sampler2D blurred;
        layout(binding = 2) uniform sampler2D mask;
        layout(binding = 3, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 4, std140) uniform Glow { vec4 logical; vec4 raster; vec4 size; } glow;
        layout(binding = 5, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(binding = 6, std140) uniform Mask { vec4 logical; vec4 raster; vec4 size; } maskFrame;
        layout(push_constant) uniform Values { float intensity; float preserveAlpha; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            vec4 original = texture(image, (position - src.raster.xy) / src.raster.zw);
            vec2 uv = (position - glow.raster.xy) / glow.raster.zw;
            vec4 light = texture(blurred, uv) * pc.intensity;
            if (pc.preserveAlpha != 0) {
                vec2 maskUv = (position - maskFrame.raster.xy) / maskFrame.raster.zw;
                color = vec4(original.rgb + light.rgb * texture(mask, maskUv).r, original.a);
            } else {
                color = vec4(original.rgb + light.rgb, original.a + clamp(light.a, 0, 1) * (1 - original.a));
            }
        }
        """));
}
