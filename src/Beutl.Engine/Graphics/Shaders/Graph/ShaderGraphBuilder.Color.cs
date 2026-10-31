using System.Numerics;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics.Shaders;

public sealed partial class ShaderGraphBuilder
{
    /// <summary>Converts the renderer's linear-premultiplied source to an explicitly selected color encoding.</summary>
    public ShaderImage Input(string name, ColorEncoding color)
        => ConvertColor(name, Source, ColorEncoding.LinearPremultiplied, color);

    /// <summary>Converts a single 2D color image, retaining its bounds and raster density.</summary>
    /// <remarks>Conversion occurs per texel before subsequent filtering. Alpha is unchanged; unpremultiplying
    /// zero alpha produces zero RGB. Extended negative and HDR RGB values are retained, without gamut clipping.</remarks>
    public ShaderImage ConvertColor(string name, ShaderImage image, ColorEncoding from, ColorEncoding to)
    {
        EnsureRecording();
        ValidateResource(image);
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to))
            throw new ArgumentOutOfRangeException(nameof(from));
        if (!image.IsAttachment || image.Format.IsDepthFormat() || image.Format.GetNumericType() != NumericType.Float)
            throw new ArgumentException("Color conversion requires a single 2D image with floating-point sample values.", nameof(image));
        ShaderImage output = image.Description is { } description
            ? Image(name, description with { Format = TextureFormat.RGBA16Float })
            : Image2D(name, image, resolutionScale: image.ResolutionScale);
        output.RasterSource = image;
        Fullscreen(name, s_convertColor, pass =>
        {
            pass.SampledImage(0, image, SamplerState.NearestClamp);
            pass.InputRegion(0, ShaderInputRegion.Identity);
            pass.ColorAttachment(0, output);
            pass.PushConstants(new Vector4((int)from, (int)to, 0, 0));
        });
        return output;
    }

    private static readonly ShaderProgram s_convertColor = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        layout(binding = 0) uniform sampler2D image;
        layout(push_constant) uniform Encoding { vec4 value; } encoding;
        vec3 decode(vec3 v) {
            vec3 a = abs(v);
            return sign(v) * mix(pow((a + 0.055) / 1.055, vec3(2.4)), a / 12.92, lessThanEqual(a, vec3(0.04045)));
        }
        vec3 encode(vec3 v) {
            vec3 a = abs(v);
            return sign(v) * mix(1.055 * pow(a, vec3(1.0 / 2.4)) - 0.055, a * 12.92, lessThanEqual(a, vec3(0.0031308)));
        }
        void main() {
            color = texture(image, uv);
            int from = int(encoding.value.x), to = int(encoding.value.y);
            if (from == to) return;
            if ((from & 1) == 0) color.rgb = color.a != 0.0 ? color.rgb / color.a : vec3(0);
            if (from >= 2) color.rgb = decode(color.rgb);
            if (to >= 2) color.rgb = encode(color.rgb);
            if ((to & 1) == 0) color.rgb *= color.a;
        }
        """));
}
