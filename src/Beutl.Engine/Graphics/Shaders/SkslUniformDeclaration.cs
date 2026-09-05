namespace Beutl.Graphics.Shaders;

internal readonly record struct SkslUniformDeclaration(string Type, int? ArrayExtent)
{
    public bool IsShader => Type == "shader";
}
