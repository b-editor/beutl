using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal static class SkslUniformAssignment
{
    /// <summary>Writes one bound value into a runtime effect's uniform block.</summary>
    public static void SetUniform(
        SKRuntimeEffectUniforms uniforms,
        string name,
        SkslUniformDeclaration declaration,
        ShaderUniformValue value)
    {
        if (value.IsInteger)
        {
            uniforms[name] = declaration.ArrayExtent is null
                && declaration.Type is "int" or "bool"
                    ? value.Integers![0]
                    : value.Integers!;
        }
        else
        {
            uniforms[name] = declaration.ArrayExtent is null
                && declaration.Type is "float" or "half"
                    ? value.Floats![0]
                    : value.Floats!;
        }
    }
}
