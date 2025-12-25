namespace Beutl.Graphics3D;

/// <summary>
/// Provides GLSL shader source code for basic 3D rendering.
/// </summary>
internal static class BasicShaderSources
{
    /// <summary>
    /// Gets the basic vertex shader source code.
    /// </summary>
    /// <remarks>
    /// This shader transforms vertices to clip space and passes
    /// normals, positions, and texture coordinates to the fragment shader.
    /// </remarks>
    public static string VertexShader => """
        #version 450

        layout(location = 0) in vec3 inPosition;
        layout(location = 1) in vec3 inNormal;
        layout(location = 2) in vec2 inTexCoord;

        layout(binding = 0) uniform UniformBufferObject {
            mat4 model;
            mat4 view;
            mat4 projection;
            vec3 lightDirection;
            float _pad1;
            vec3 lightColor;
            float _pad2;
            vec3 ambientColor;
            float _pad3;
            vec3 viewPosition;
            float _pad4;
            vec4 objectColor;
        } ubo;

        layout(location = 0) out vec3 fragNormal;
        layout(location = 1) out vec3 fragPosition;
        layout(location = 2) out vec2 fragTexCoord;

        void main() {
            vec4 worldPos = ubo.model * vec4(inPosition, 1.0);
            gl_Position = ubo.projection * ubo.view * worldPos;

            fragNormal = mat3(transpose(inverse(ubo.model))) * inNormal;
            fragPosition = worldPos.xyz;
            fragTexCoord = inTexCoord;
        }
        """;

    /// <summary>
    /// Gets the basic fragment shader source code.
    /// </summary>
    /// <remarks>
    /// This shader implements Blinn-Phong lighting with ambient,
    /// diffuse, and specular components.
    /// </remarks>
    public static string FragmentShader => """
        #version 450

        layout(location = 0) in vec3 fragNormal;
        layout(location = 1) in vec3 fragPosition;
        layout(location = 2) in vec2 fragTexCoord;

        layout(binding = 0) uniform UniformBufferObject {
            mat4 model;
            mat4 view;
            mat4 projection;
            vec3 lightDirection;
            float _pad1;
            vec3 lightColor;
            float _pad2;
            vec3 ambientColor;
            float _pad3;
            vec3 viewPosition;
            float _pad4;
            vec4 objectColor;
        } ubo;

        layout(location = 0) out vec4 outColor;

        void main() {
            vec3 normal = normalize(fragNormal);
            vec3 lightDir = normalize(-ubo.lightDirection);

            // Ambient
            vec3 ambient = ubo.ambientColor * ubo.objectColor.rgb;

            // Diffuse
            float diff = max(dot(normal, lightDir), 0.0);
            vec3 diffuse = diff * ubo.lightColor * ubo.objectColor.rgb;

            // Simple specular (Blinn-Phong)
            vec3 viewDir = normalize(ubo.viewPosition - fragPosition);
            vec3 halfwayDir = normalize(lightDir + viewDir);
            float spec = pow(max(dot(normal, halfwayDir), 0.0), 32.0);
            vec3 specular = spec * ubo.lightColor * 0.5;

            vec3 result = ambient + diffuse + specular;
            outColor = vec4(result, ubo.objectColor.a);
        }
        """;
}
