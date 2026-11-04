namespace Beutl.Graphics.Effects;

/// <summary>GLSL functions that more than one built-in effect program concatenates.</summary>
internal static class GlslSnippets
{
    /// <summary>The luminance of a premultiplied color with its alpha taken out, and that luminance gamma encoded.</summary>
    public const string Luminance = """
        float straightLuminance(vec4 premultiplied) {
            return premultiplied.a > 0 ? dot(premultiplied.rgb / premultiplied.a, vec3(0.2126, 0.7152, 0.0722)) : 0;
        }

        float gammaLightness(vec4 premultiplied) {
            return pow(max(straightLuminance(premultiplied), 0), 1.0 / 2.2);
        }
        """;
}
