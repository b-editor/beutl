// LampShaderFactory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics
{
    internal sealed class LampShaderFactory : ShaderFactory
    {
        private const string LampFrag =
            "#version 330 core\n" +
            "out vec4 FragColor;\n" +

            "uniform vec4 color;\n" +

            "void main()\n" +
            "{\n" +
            "   FragColor = color;\n" +
            "}";

        private const string LampVert =
            "#version 330 core\n" +
            "layout (location = 0) in vec3 aPos;\n" +
            "layout (location = 1) in vec3 aNormal;\n" +

            "uniform mat4 model;\n" +
            "uniform mat4 view;\n" +
            "uniform mat4 projection;\n" +

            "out vec3 Normal;\n" +
            "out vec3 FragPos;\n" +

            "void main()\n" +
            "{\n" +
            "    gl_Position = vec4(aPos, 1.0) * model * view * projection;\n" +
            "    FragPos = vec3(vec4(aPos, 1.0) * model);\n" +
            "    Normal = aNormal * mat3(transpose(inverse(model)));\n" +
            "}";

        public override Shader Create()
        {
            return new(LampVert, LampFrag);
        }
    }
}