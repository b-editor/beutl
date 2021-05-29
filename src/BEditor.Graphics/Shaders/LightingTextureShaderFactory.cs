// LightingTextureShaderFactory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics
{
    internal sealed class LightingTextureShaderFactory : ShaderFactory
    {
        private const string TextureFrag =
            "#version 330\n" +
            "struct Material {\n" +
            "    vec4 ambient;\n" +
            "    vec4 diffuse;\n" +
            "    vec4 specular;\n" +
            "    float shininess;\n" +
            "};\n" +

            "struct Light {\n" +
            "    vec3 position;\n" +

            "    vec4 ambient;\n" +
            "    vec4 diffuse;\n" +
            "    vec4 specular;\n" +
            "};\n" +

            "uniform Light light;\n" +
            "uniform Material material;\n" +
            "uniform vec4 color;\n" +
            "uniform sampler2D texture0;\n" +
            "uniform vec3 viewPos;\n" +

            "out vec4 outputColor;\n" +

            "in vec2 texCoord;\n" +
            "in vec3 Normal;\n" +
            "in vec3 FragPos;\n" +

            "void main()\n" +
            "{\n" +

            // ambient
            "    vec4 ambient = light.ambient * material.ambient;\n" +

            // diffuse
            "    vec3 norm = normalize(Normal);\n" +
            "    vec3 lightDir = normalize(light.position - FragPos);\n" +
            "    float diff = max(dot(norm, lightDir), 0.0);\n" +
            "    vec4 diffuse = light.diffuse * (diff * material.diffuse);\n" +

            // specular
            "    vec3 viewDir = normalize(viewPos - FragPos);\n" +
            "    vec3 reflectDir = reflect(-lightDir, norm);\n" +
            "    float spec = pow(max(dot(viewDir, reflectDir), 0.0), material.shininess);\n" +
            "    vec4 specular = light.specular * (spec * material.specular);\n" +

            "    vec4 result = ambient + diffuse + specular;\n" +
            "    outputColor = texture(texture0, texCoord) * result * color;\n" +
            "}";

        private const string TextureVert =
            "#version 330 core\n" +

            "layout(location = 0) in vec3 aPosition;\n" +
            "layout(location = 1) in vec2 aTexCoord;\n" +
            "layout(location = 2) in vec3 aNormal;\n" +

            "uniform mat4 model;\n" +
            "uniform mat4 view;\n" +
            "uniform mat4 projection;\n" +

            "out vec2 texCoord;\n" +
            "out vec3 Normal;\n" +
            "out vec3 FragPos;\n" +

            "void main(void)\n" +
            "{\n" +
            "    texCoord = aTexCoord;\n" +

            "    gl_Position = vec4(aPosition, 1.0) * model * view * projection;\n" +
            "    FragPos = vec3(vec4(aPosition, 1.0) * model);\n" +
            "    Normal = aNormal * mat3(transpose(inverse(model)));\n" +
            "}";

        public override Shader Create()
        {
            return new(TextureVert, TextureFrag);
        }
    }
}