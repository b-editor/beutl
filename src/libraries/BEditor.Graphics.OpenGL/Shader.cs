using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;

using BEditor.Graphics.OpenGL.Resources;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics.OpenGL
{
    public class Shader : IDisposable
    {
        private readonly Dictionary<string, int> _uniformLocations;
        private readonly SynchronizationContext _synchronization;
        private readonly GraphicsHandle _handle;

        public Shader(string vertSource, string fragSource)
        {
            if (vertSource is null) throw new ArgumentNullException(nameof(vertSource));
            if (fragSource is null) throw new ArgumentNullException(nameof(fragSource));

            _synchronization = GraphicsContextImpl.SyncContext;

            var vertexShader = GL.CreateShader(ShaderType.VertexShader);

            GL.ShaderSource(vertexShader, vertSource);

            CompileShader(vertexShader);

            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragSource);
            CompileShader(fragmentShader);

            _handle = GL.CreateProgram();

            GL.AttachShader(_handle, vertexShader);
            GL.AttachShader(_handle, fragmentShader);

            LinkProgram(_handle);

            GL.DetachShader(_handle, vertexShader);
            GL.DetachShader(_handle, fragmentShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteShader(vertexShader);

            GL.GetProgram(_handle, GetProgramParameterName.ActiveUniforms, out var numberOfUniforms);

            _uniformLocations = new Dictionary<string, int>();

            for (var i = 0; i < numberOfUniforms; i++)
            {
                var key = GL.GetActiveUniform(_handle, i, out _, out _);

                var location = GL.GetUniformLocation(_handle, key);

                _uniformLocations.Add(key, location);
            }
        }

        ~Shader()
        {
            if (!IsDisposed) Dispose();
        }

        public bool IsDisposed { get; private set; }

        public static Shader FromFile(string vertPath, string fragPath)
        {
            return new Shader(File.ReadAllText(vertPath), File.ReadAllText(fragPath));
        }

        public void Use()
        {
            GL.UseProgram(_handle);
            Tool.ThrowGLError();
        }

        public int GetAttribLocation(string attribName)
        {
            return GL.GetAttribLocation(_handle, attribName);
        }

        public void SetInt(string name, int data)
        {
            GL.UseProgram(_handle);
            Tool.ThrowGLError();

            GL.Uniform1(_uniformLocations[name], data);
            Tool.ThrowGLError();
        }

        public void SetFloat(string name, float data)
        {
            GL.UseProgram(_handle);
            Tool.ThrowGLError();

            GL.Uniform1(_uniformLocations[name], data);
            Tool.ThrowGLError();
        }

        public void SetMatrix4(string name, Matrix4x4 data)
        {
            var mat = data.ToOpenTK();
            GL.UseProgram(_handle);
            Tool.ThrowGLError();

            GL.UniformMatrix4(_uniformLocations[name], true, ref mat);
            Tool.ThrowGLError();
        }

        public void SetVector3(string name, Vector3 data)
        {
            var vec = data.ToOpenTK();

            GL.UseProgram(_handle);
            Tool.ThrowGLError();

            GL.Uniform3(_uniformLocations[name], ref vec);
            Tool.ThrowGLError();
        }

        public void SetVector4(string name, Vector4 data)
        {
            var vec = data.ToOpenTK();

            GL.UseProgram(_handle);
            Tool.ThrowGLError();

            GL.Uniform4(_uniformLocations[name], ref vec);
            Tool.ThrowGLError();
        }

        public void Dispose()
        {
            if (IsDisposed) return;

            _synchronization.Send(_ => GL.DeleteProgram(_handle), null);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }

        private static void CompileShader(int shader)
        {
            GL.CompileShader(shader);

            GL.GetShader(shader, ShaderParameter.CompileStatus, out var code);
            if (code != (int)All.True)
            {
                // We can use `GL.GetShaderInfoLog(shader)` to get information about the error.
                var infoLog = GL.GetShaderInfoLog(shader);
                Debug.Fail(string.Empty);
                throw new GraphicsException(string.Format(Strings.ErrorOccurredWhilistCompilingShader, shader, infoLog));
            }
        }

        private static void LinkProgram(int program)
        {
            GL.LinkProgram(program);

            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var code);
            if (code != (int)All.True)
            {
                Debug.Fail(string.Empty);
                throw new GraphicsException(string.Format(Strings.ErrorOccurredWhilstLinkingProgram, program));
            }
        }
    }
}