using System.Numerics;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using Beutl.Logging;

namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// OpenGL用シェーダープログラム
/// </summary>
public class OpenGLShaderProgram : IShaderProgram
{
    private static readonly ILogger s_logger = Log.CreateLogger<OpenGLShaderProgram>();

    private bool _disposed;
    private readonly Dictionary<string, int> _uniformLocations = [];

    public uint ProgramId { get; private set; }
    public string VertexShaderSource { get; }
    public string FragmentShaderSource { get; }

    public OpenGLShaderProgram(string vertexShader, string fragmentShader)
    {
        VertexShaderSource = vertexShader;
        FragmentShaderSource = fragmentShader;
        CompileAndLink();
    }

    private void CompileAndLink()
    {
        // 頂点シェーダーをコンパイル
        uint vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        if (vertexShader == 0)
        {
            throw new InvalidOperationException("Failed to compile vertex shader");
        }

        // フラグメントシェーダーをコンパイル
        uint fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);
        if (fragmentShader == 0)
        {
            GL.DeleteShader(vertexShader);
            throw new InvalidOperationException("Failed to compile fragment shader");
        }

        // プログラムを作成してシェーダーをアタッチ
        ProgramId = GL.CreateProgram();
        GL.AttachShader(ProgramId, vertexShader);
        GL.AttachShader(ProgramId, fragmentShader);

        // プログラムをリンク
        GL.LinkProgram(ProgramId);

        // リンク結果をチェック
        GL.GetProgram(ProgramId, GetProgramParameterName.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string infoLog = GL.GetProgramInfoLog(ProgramId);
            s_logger.LogError("Shader program linking failed: {InfoLog}", infoLog);

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteProgram(ProgramId);

            throw new InvalidOperationException($"Failed to link shader program: {infoLog}");
        }

        // シェーダーはもう不要なので削除
        GL.DetachShader(ProgramId, vertexShader);
        GL.DetachShader(ProgramId, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        // ユニフォームの場所を事前に取得
        CacheUniformLocations();
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);

        // コンパイル結果をチェック
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compileStatus);
        if (compileStatus == 0)
        {
            string infoLog = GL.GetShaderInfoLog(shader);
            s_logger.LogError("Shader compilation failed ({Type}): {InfoLog}", type, infoLog);
            GL.DeleteShader(shader);
            return 0;
        }

        return shader;
    }

    private void CacheUniformLocations()
    {
        GL.GetProgram(ProgramId, GetProgramParameterName.ActiveUniforms, out int uniformCount);

        for (int i = 0; i < uniformCount; i++)
        {
            GL.GetActiveUniform(ProgramId, i, 256, out _, out _, out _, out string name);
            int location = GL.GetUniformLocation(ProgramId, name);
            if (location >= 0)
            {
                _uniformLocations[name] = location;
            }
        }
    }

    private int GetUniformLocation(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
        {
            return location;
        }

        location = GL.GetUniformLocation(ProgramId, name);
        _uniformLocations[name] = location;

        if (location == -1)
        {
            s_logger.LogWarning("Uniform '{Name}' not found in shader program", name);
        }

        return location;
    }

    public void Use()
    {
        GL.UseProgram(ProgramId);
    }

    public void SetUniform(string name, float value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            GL.Uniform1(location, value);
        }
    }

    public void SetUniform(string name, Vector2 value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            GL.Uniform2(location, value.X, value.Y);
        }
    }

    public void SetUniform(string name, Vector3 value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            GL.Uniform3(location, value.X, value.Y, value.Z);
        }
    }

    public void SetUniform(string name, Vector4 value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            GL.Uniform4(location, value.X, value.Y, value.Z, value.W);
        }
    }

    public void SetUniform(string name, Matrix4x4 value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            // Matrix4x4を4x4の float 配列に変換
            float[] matrix = [
                value.M11, value.M12, value.M13, value.M14,
                value.M21, value.M22, value.M23, value.M24,
                value.M31, value.M32, value.M33, value.M34,
                value.M41, value.M42, value.M43, value.M44
            ];
            GL.UniformMatrix4(location, 1, false, matrix);
        }
    }

    public void SetUniform(string name, int value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            GL.Uniform1(location, value);
        }
    }

    public void SetUniform(string name, bool value)
    {
        SetUniform(name, value ? 1 : 0);
    }

    public void SetTexture(string name, ITextureResource texture, int slot)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + slot);

        if (texture is OpenGLTextureResource glTexture)
        {
            GL.BindTexture(TextureTarget.Texture2D, glTexture.TextureId);
        }

        SetUniform(name, slot);
    }

    /// <summary>
    /// 配列ユニフォームを設定
    /// </summary>
    public void SetUniformArray(string name, float[] values)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            GL.Uniform1(location, values.Length, values);
        }
    }

    public void SetUniformArray(string name, Vector3[] values)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            float[] flatArray = new float[values.Length * 3];
            for (int i = 0; i < values.Length; i++)
            {
                flatArray[i * 3 + 0] = values[i].X;
                flatArray[i * 3 + 1] = values[i].Y;
                flatArray[i * 3 + 2] = values[i].Z;
            }
            GL.Uniform3(location, values.Length, flatArray);
        }
    }

    public void SetUniformArray(string name, Vector4[] values)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            float[] flatArray = new float[values.Length * 4];
            for (int i = 0; i < values.Length; i++)
            {
                flatArray[i * 4 + 0] = values[i].X;
                flatArray[i * 4 + 1] = values[i].Y;
                flatArray[i * 4 + 2] = values[i].Z;
                flatArray[i * 4 + 3] = values[i].W;
            }
            GL.Uniform4(location, values.Length, flatArray);
        }
    }

    /// <summary>
    /// ユニフォームブロックをバインド
    /// </summary>
    public void BindUniformBlock(string blockName, uint bindingPoint)
    {
        uint blockIndex = GL.GetUniformBlockIndex(ProgramId, blockName);
        if (blockIndex != GL.INVALID_INDEX)
        {
            GL.UniformBlockBinding(ProgramId, blockIndex, bindingPoint);
        }
        else
        {
            s_logger.LogWarning("Uniform block '{BlockName}' not found in shader program", blockName);
        }
    }

    /// <summary>
    /// プログラムの詳細情報を取得
    /// </summary>
    public ShaderProgramInfo GetInfo()
    {
        GL.GetProgram(ProgramId, GetProgramParameterName.ActiveUniforms, out int uniformCount);
        GL.GetProgram(ProgramId, GetProgramParameterName.ActiveAttributes, out int attributeCount);

        return new ShaderProgramInfo
        {
            ProgramId = ProgramId,
            UniformCount = uniformCount,
            AttributeCount = attributeCount,
            Uniforms = _uniformLocations.Keys.ToList(),
            IsValid = GL.IsProgram(ProgramId)
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        GL.DeleteProgram(ProgramId);
        ProgramId = 0;
        _uniformLocations.Clear();
        _disposed = true;
    }
}
