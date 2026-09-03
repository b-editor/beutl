namespace Beutl.Graphics.Shaders;

internal sealed record ShaderUniformValue(float[]? Floats, int[]? Integers, bool IsInteger);
