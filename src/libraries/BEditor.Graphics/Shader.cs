// Shader.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;

using BEditor.Graphics.Resources;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an OpenGL shader.
    /// </summary>
    public class Shader : IDisposable
    {
        private readonly Dictionary<string, int> _uniformLocations;
        private readonly SynchronizationContext _synchronization;
        private readonly GraphicsHandle _handle;

        /// <summary>
        /// Initializes a new instance of the <see cref="Shader"/> class.
        /// </summary>
        /// <param name="vertSource">The source of the vertex shader.</param>
        /// <param name="fragSource">The source of the fragment shader.</param>
        public Shader(string vertSource, string fragSource)
        {
            if (vertSource is null) throw new ArgumentNullException(nameof(vertSource));
            if (fragSource is null) throw new ArgumentNullException(nameof(fragSource));

            _synchronization = AsyncOperationManager.SynchronizationContext;

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

        /// <summary>
        /// Finalizes an instance of the <see cref="Shader"/> class.
        /// </summary>
        ~Shader()
        {
            if (!IsDisposed) Dispose();
        }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Create a <see cref="Shader"/> from a shader files.
        /// </summary>
        /// <param name="vertPath">The path to the vertex shader source file.</param>
        /// <param name="fragPath">The path to the fragment shader source file.</param>
        /// <returns>The shader that was created.</returns>
        public static Shader FromFile(string vertPath, string fragPath)
        {
            return new Shader(File.ReadAllText(vertPath), File.ReadAllText(fragPath));
        }

        /// <summary>
        /// Use this shader.
        /// </summary>
        public void Use()
        {
            GL.UseProgram(_handle);
            Tool.ThrowGLError();
        }

        /// <summary>
        /// Returns the location of an attribute variable.
        /// </summary>
        /// <param name="attribName">Points to a null terminated string containing the name of the attribute variable whose location is to be queried.</param>
        /// <returns>Returns the location of an attribute variable.</returns>
        public int GetAttribLocation(string attribName)
        {
            return GL.GetAttribLocation(_handle, attribName);
        }

        /// <summary>
        /// Specify the value of a uniform variable for the current program object.
        /// </summary>
        /// <param name="name">The name of the uniform variable.</param>
        /// <param name="data">The value of the uniform variable to set.</param>
        public void SetInt(string name, int data)
        {
            GL.UseProgram(_handle);
            Tool.ThrowGLError();

            GL.Uniform1(_uniformLocations[name], data);
            Tool.ThrowGLError();
        }

        /// <summary>
        /// Specify the value of a uniform variable for the current program object.
        /// </summary>
        /// <param name="name">The name of the uniform variable.</param>
        /// <param name="data">The value of the uniform variable to set.</param>
        public void SetFloat(string name, float data)
        {
            GL.UseProgram(_handle);
            Tool.ThrowGLError();

            GL.Uniform1(_uniformLocations[name], data);
            Tool.ThrowGLError();
        }

        /// <summary>
        /// Specify the value of a uniform variable for the current program object.
        /// </summary>
        /// <param name="name">The name of the uniform variable.</param>
        /// <param name="data">The value of the uniform variable to set.</param>
        public void SetMatrix4(string name, Matrix4x4 data)
        {
            var mat = data.ToOpenTK();
            GL.UseProgram(_handle);
            Tool.ThrowGLError();

            GL.UniformMatrix4(_uniformLocations[name], true, ref mat);
            Tool.ThrowGLError();
        }

        /// <summary>
        /// Specify the value of a uniform variable for the current program object.
        /// </summary>
        /// <param name="name">The name of the uniform variable.</param>
        /// <param name="data">The value of the uniform variable to set.</param>
        public void SetVector3(string name, Vector3 data)
        {
            var vec = data.ToOpenTK();

            GL.UseProgram(_handle);
            Tool.ThrowGLError();

            GL.Uniform3(_uniformLocations[name], ref vec);
            Tool.ThrowGLError();
        }

        /// <summary>
        /// Specify the value of a uniform variable for the current program object.
        /// </summary>
        /// <param name="name">The name of the uniform variable.</param>
        /// <param name="data">The value of the uniform variable to set.</param>
        public void SetVector4(string name, Vector4 data)
        {
            var vec = data.ToOpenTK();

            GL.UseProgram(_handle);
            Tool.ThrowGLError();

            GL.Uniform4(_uniformLocations[name], ref vec);
            Tool.ThrowGLError();
        }

        /// <inheritdoc/>
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