// ShaderFactory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents the factory class for creating shaders.
    /// </summary>
    public class ShaderFactory
    {
        /// <summary>
        /// Represents the shader used to draw the texture.
        /// </summary>
        public static readonly ShaderFactory Texture = new TextureShaderFactory();

        /// <summary>
        /// Represents the shader used to draw the cube or ball when the light is enabled.
        /// </summary>
        public static readonly ShaderFactory Lighting = new LightingShaderFactory();

        /// <summary>
        /// Represents the shader used to draw the texture when the light is enabled.
        /// </summary>
        public static readonly ShaderFactory TextureLighting = new LightingTextureShaderFactory();

        /// <summary>
        /// Represents the shader used to draw the line.
        /// </summary>
        public static readonly ShaderFactory Line = new LineShaderFactory();

        /// <summary>
        /// Represents the default shader.
        /// </summary>
        public static readonly ShaderFactory Default = new();

        internal const string Frag =
            "#version 330 core\n" +
            "out vec4 FragColor;\n" +

            "uniform vec4 color;\n" +

            "void main()\n" +
            "{\n" +
            "   FragColor = color;\n" +
            "}";

        internal const string Vert =
            "#version 330 core\n" +
            "layout (location = 0) in vec3 aPos;\n" +

            "uniform mat4 model;\n" +
            "uniform mat4 view;\n" +
            "uniform mat4 projection;\n" +

            "void main()\n" +
            "{\n" +
            "   gl_Position = vec4(aPos, 1.0) * model * view * projection;\n" +
            "}";

        /// <summary>
        /// Create a shader.
        /// </summary>
        /// <returns>Returns the shaders created by this method.</returns>
        public virtual Shader Create()
        {
            return new(Vert, Frag);
        }
    }
}