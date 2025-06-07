using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// シェーダープログラム
/// </summary>
public interface IShaderProgram : IDisposable
{
    uint ProgramId { get; }
    void Use();
    void SetUniform(string name, float value);
    void SetUniform(string name, Vector2 value);
    void SetUniform(string name, Vector3 value);
    void SetUniform(string name, Vector4 value);
    void SetUniform(string name, Matrix4x4 value);
    void SetTexture(string name, ITextureResource texture, int slot);
}
