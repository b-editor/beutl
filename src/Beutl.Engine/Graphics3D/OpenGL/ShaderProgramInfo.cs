namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// シェーダープログラムの情報
/// </summary>
public class ShaderProgramInfo
{
    public uint ProgramId { get; init; }
    public int UniformCount { get; init; }
    public int AttributeCount { get; init; }
    public List<string> Uniforms { get; init; } = [];
    public bool IsValid { get; init; }
}
