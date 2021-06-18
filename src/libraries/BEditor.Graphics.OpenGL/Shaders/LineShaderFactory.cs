// LineShaderFactory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics.OpenGL
{
    internal sealed class LineShaderFactory : ShaderFactory
    {
        private const string LineVert =
              "#version 330 core\n" +
              "layout (location = 0) in vec3 aPos;\n" +

              "uniform mat4 model;\n" +
              "uniform mat4 view;\n" +
              "uniform mat4 projection;\n" +

              "void main()\n" +
              "{\n" +
              "   gl_Position = vec4(aPos.x, aPos.y, aPos.z, 1.0) * model * view * projection;\n" +
              "}";

        private const string LineFrag =
            "#version 330 core\n" +

            "out vec4 FragColor;\n" +
            "uniform vec4 color;\n" +

            "void main()\n" +
            "{\n" +
            "   FragColor = color;\n" +
            "}";

        public override Shader Create()
        {
            return new(LineVert, LineFrag);
        }
    }
}