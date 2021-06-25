// TextureShaderFactory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics.OpenGL
{
    internal sealed class TextureShaderFactory : ShaderFactory
    {
        private const string TextureFrag =
            "#version 330\n" +

            "out vec4 outputColor;\n" +

            "in vec2 texCoord;\n" +

            "uniform vec4 color;\n" +
            "uniform sampler2D texture0;\n" +

            "void main()\n" +
            "{\n" +
            "   outputColor = texture(texture0, texCoord) * color;\n" +
            "   outputColor.rgb /= outputColor.a;\n" +
            "}";

        private const string TextureVert =
            "#version 330 core\n" +

            "layout(location = 0) in vec3 aPosition;\n" +
            "layout(location = 1) in vec2 aTexCoord;\n" +

            "out vec2 texCoord;\n" +
            "uniform mat4 model;\n" +
            "uniform mat4 view;\n" +
            "uniform mat4 projection;\n" +

            "void main(void)\n" +
            "{\n" +
            "   texCoord = aTexCoord;\n" +
            "   gl_Position = vec4(aPosition, 1.0) * model * view * projection;\n" +
            "}";

        public override Shader Create()
        {
            return new(TextureVert, TextureFrag);
        }
    }
}