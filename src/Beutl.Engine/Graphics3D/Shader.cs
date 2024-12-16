using System.Diagnostics;
using System.Numerics;
using Beutl.Graphics;
using Beutl.Threading;
using OpenTK.Graphics.OpenGL;

namespace Beutl.Graphics3D;

public sealed class Shader : IDisposable
{
    private readonly Dictionary<string, int> _uniformLocations;
    private readonly Dispatcher _dispatcher = Dispatcher.Current;

    public Shader(string vertSource, string fragSource)
    {
        ArgumentNullException.ThrowIfNull(vertSource);
        ArgumentNullException.ThrowIfNull(fragSource);

        int vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, vertSource);
        CompileShader(vertexShader);

        int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentShader, fragSource);
        CompileShader(fragmentShader);

        Handle = GL.CreateProgram();

        GL.AttachShader(Handle, vertexShader);
        GL.AttachShader(Handle, fragmentShader);

        LinkProgram(Handle);

        GL.DetachShader(Handle, vertexShader);
        GL.DetachShader(Handle, fragmentShader);
        GL.DeleteShader(fragmentShader);
        GL.DeleteShader(vertexShader);

        GL.GetProgrami(Handle, ProgramProperty.ActiveUniforms, out int numberOfUniforms);

        _uniformLocations = new Dictionary<string, int>();

        for (uint i = 0; i < numberOfUniforms; i++)
        {
            string key = GL.GetActiveUniform(Handle, i, 256, out _, out _, out _);

            int location = GL.GetUniformLocation(Handle, key);

            _uniformLocations.Add(key, location);
        }
    }

    ~Shader()
    {
        if (!IsDisposed) Dispose();
    }

    public int Handle { get; }

    public bool IsDisposed { get; private set; }

    public static Shader FromFile(string vertPath, string fragPath)
    {
        return new Shader(File.ReadAllText(vertPath), File.ReadAllText(fragPath));
    }

    public void Use()
    {
        GL.UseProgram(Handle);
        GlErrorHelper.CheckGlError();
    }

    public int GetAttribLocation(string attribName)
    {
        return GL.GetAttribLocation(Handle, attribName);
    }

    public void SetInt(string name, int data)
    {
        GL.UseProgram(Handle);
        GlErrorHelper.CheckGlError();

        GL.Uniform1i(_uniformLocations[name], data);
        GlErrorHelper.CheckGlError();
    }

    public void SetFloat(string name, float data)
    {
        GL.UseProgram(Handle);
        GlErrorHelper.CheckGlError();

        GL.Uniform1f(_uniformLocations[name], data);
        GlErrorHelper.CheckGlError();
    }

    public void SetMatrix4(string name, ref readonly Matrix4x4 data)
    {
        GL.UseProgram(Handle);
        GlErrorHelper.CheckGlError();

        GL.UniformMatrix4f(_uniformLocations[name], 1, true, in data);
        GlErrorHelper.CheckGlError();
    }

    public void SetVector3(string name, ref readonly Vector3 data)
    {
        GL.UseProgram(Handle);
        GlErrorHelper.CheckGlError();

        GL.Uniform3f(_uniformLocations[name], 1, in data);
        GlErrorHelper.CheckGlError();
    }

    public void SetVector4(string name, Vector4 data)
    {
        GL.UseProgram(Handle);
        GlErrorHelper.CheckGlError();

        GL.Uniform4f(_uniformLocations[name], 1, in data);
        GlErrorHelper.CheckGlError();
    }

    public void Dispose()
    {
        if (IsDisposed) return;

        _dispatcher.Run(() => GL.DeleteProgram(Handle));

        GC.SuppressFinalize(this);

        IsDisposed = true;
    }

    private static void CompileShader(int shader)
    {
        GL.CompileShader(shader);

        GL.GetShaderi(shader, ShaderParameterName.CompileStatus, out int code);
        if (code == (int)All.True) return;

        // We can use `GL.GetShaderInfoLog(shader)` to get information about the error.
        GL.GetShaderInfoLog(shader, out string infoLog);
        Debug.Fail(string.Empty);
        throw new GraphicsException($"{shader} {infoLog}");
        // throw new GraphicsException(string.Format(Strings.ErrorOccurredWhilistCompilingShader, shader, infoLog));
    }

    private static void LinkProgram(int program)
    {
        GL.LinkProgram(program);

        GL.GetProgrami(program, ProgramProperty.LinkStatus, out int code);
        if (code == (int)All.True) return;

        Debug.Fail(string.Empty);
        throw new GraphicsException($"Error occurred whilst linking program {program}");
        // throw new GraphicsException(string.Format(Strings.ErrorOccurredWhilstLinkingProgram, program));
    }
}
