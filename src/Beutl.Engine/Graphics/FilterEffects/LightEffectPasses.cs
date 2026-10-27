using System.Numerics;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Effects;

/// <summary>Shared linear-light extraction and composition for bloom and light-ray effects.</summary>
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

    private static readonly ShaderProgram s_extract = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { float threshold; float softKnee; } pc;
        layout(location = 0) out vec4 bright;
        layout(location = 1) out float mask;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            vec4 color = texture(image, (position - src.raster.xy) / src.raster.zw);
            // Select by straight luminance, then scale premultiplied RGBA so translucent edges fade naturally.
            float luminance = color.a > 0 ? dot(color.rgb / color.a, vec3(0.2126, 0.7152, 0.0722)) : 0;
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
