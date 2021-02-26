using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BEditor.Graphics.Properties;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    public class Shader : IDisposable
    {
        private readonly Dictionary<string, int> _uniformLocations;
        private readonly SynchronizationContext _synchronization;

        public Shader(string vertSource, string fragSource)
        {
            _synchronization = AsyncOperationManager.SynchronizationContext;

            var vertexShader = GL.CreateShader(ShaderType.VertexShader);

            GL.ShaderSource(vertexShader, vertSource);

            CompileShader(vertexShader);

            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
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

            GL.GetProgram(Handle, GetProgramParameterName.ActiveUniforms, out var numberOfUniforms);

            _uniformLocations = new Dictionary<string, int>();

            for (var i = 0; i < numberOfUniforms; i++)
            {
                var key = GL.GetActiveUniform(Handle, i, out _, out _);

                var location = GL.GetUniformLocation(Handle, key);

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
        private static void CompileShader(int shader)
        {
            GL.CompileShader(shader);

            GL.GetShader(shader, ShaderParameter.CompileStatus, out var code);
            if (code != (int)All.True)
            {
                // We can use `GL.GetShaderInfoLog(shader)` to get information about the error.
                var infoLog = GL.GetShaderInfoLog(shader);
                Debug.Assert(false);
                throw new GraphicsException(string.Format(Resources.ErrorOccurredWhilistCompilingShader, shader, infoLog));
            }
        }
        private static void LinkProgram(int program)
        {
            GL.LinkProgram(program);

            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var code);
            if (code != (int)All.True)
            {
                Debug.Assert(false);
                throw new GraphicsException(string.Format(Resources.ErrorOccurredWhilstLinkingProgram, program));
            }
        }
        public void Use()
        {
            GL.UseProgram(Handle);
        }
        public int GetAttribLocation(string attribName)
        {
            return GL.GetAttribLocation(Handle, attribName);
        }
        public void SetInt(string name, int data)
        {
            GL.UseProgram(Handle);
            GL.Uniform1(_uniformLocations[name], data);
        }
        public void SetFloat(string name, float data)
        {
            GL.UseProgram(Handle);
            GL.Uniform1(_uniformLocations[name], data);
        }
        public void SetMatrix4(string name, Matrix4x4 data)
        {
            var mat = data.ToOpenTK();
            GL.UseProgram(Handle);
            GL.UniformMatrix4(_uniformLocations[name], true, ref mat);
        }
        public void SetVector3(string name, Vector3 data)
        {
            var vec = data.ToOpenTK();

            GL.UseProgram(Handle);
            GL.Uniform3(_uniformLocations[name], ref vec);
        }
        public void SetVector4(string name, Vector4 data)
        {
            var vec = data.ToOpenTK();

            GL.UseProgram(Handle);
            GL.Uniform4(_uniformLocations[name], ref vec);
        }

        public void Dispose()
        {
            if (IsDisposed) return;

            _synchronization.Post(_ =>
            {
                GL.DeleteProgram(Handle);
            }, null);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }
}
