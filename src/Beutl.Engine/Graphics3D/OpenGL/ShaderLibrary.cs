namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// シェーダーライブラリ（よく使用されるシェーダーの管理）
/// </summary>
public static class ShaderLibrary
{
    private static readonly Dictionary<string, (string vertex, string fragment)> s_shaders = [];
    private static readonly Dictionary<string, OpenGLShaderProgram> s_compiledShaders = [];

    static ShaderLibrary()
    {
        RegisterDefaultShaders();
    }

    private static void RegisterDefaultShaders()
    {
        // 基本的なPBRシェーダー
        s_shaders["pbr"] = (GetPbrVertexShader(), GetPbrFragmentShader());
        
        // アンリットシェーダー
        s_shaders["unlit"] = (GetUnlitVertexShader(), GetUnlitFragmentShader());
        
        // スカイボックスシェーダー
        s_shaders["skybox"] = (GetSkyboxVertexShader(), GetSkyboxFragmentShader());
        
        // シャドウマップシェーダー
        s_shaders["shadow"] = (GetShadowVertexShader(), GetShadowFragmentShader());
    }

    /// <summary>
    /// シェーダーを登録
    /// </summary>
    public static void Register(string name, string vertexShader, string fragmentShader)
    {
        s_shaders[name] = (vertexShader, fragmentShader);
    }

    /// <summary>
    /// シェーダープログラムを取得（キャッシュ）
    /// </summary>
    public static OpenGLShaderProgram GetShader(string name)
    {
        if (s_compiledShaders.TryGetValue(name, out OpenGLShaderProgram? cached))
        {
            return cached;
        }

        if (s_shaders.TryGetValue(name, out var shaderSource))
        {
            var program = new OpenGLShaderProgram(shaderSource.vertex, shaderSource.fragment);
            s_compiledShaders[name] = program;
            return program;
        }

        throw new ArgumentException($"Shader '{name}' not found");
    }

    /// <summary>
    /// 全てのシェーダーを解放
    /// </summary>
    public static void DisposeAll()
    {
        foreach (var shader in s_compiledShaders.Values)
        {
            shader.Dispose();
        }
        s_compiledShaders.Clear();
    }

    private static string GetPbrVertexShader() => """
                                                  #version 330 core
                                                  layout (location = 0) in vec3 aPos;
                                                  layout (location = 1) in vec3 aNormal;
                                                  layout (location = 2) in vec2 aTexCoord;

                                                  out vec3 FragPos;
                                                  out vec3 Normal;
                                                  out vec2 TexCoord;

                                                  uniform mat4 u_modelMatrix;
                                                  uniform mat4 u_viewMatrix;
                                                  uniform mat4 u_projectionMatrix;

                                                  void main()
                                                  {
                                                      vec4 worldPos = u_modelMatrix * vec4(aPos, 1.0);
                                                      FragPos = worldPos.xyz;
                                                      Normal = mat3(transpose(inverse(u_modelMatrix))) * aNormal;
                                                      TexCoord = aTexCoord;

                                                      gl_Position = u_projectionMatrix * u_viewMatrix * worldPos;
                                                  }
                                                  """;

    private static string GetPbrFragmentShader() => """
                                                    #version 330 core
                                                    out vec4 FragColor;

                                                    in vec3 FragPos;
                                                    in vec3 Normal;
                                                    in vec2 TexCoord;

                                                    // Material properties
                                                    uniform vec3 u_albedo;
                                                    uniform float u_metallic;
                                                    uniform float u_roughness;
                                                    uniform vec3 u_emission;

                                                    // Textures
                                                    uniform sampler2D u_albedoTexture;
                                                    uniform sampler2D u_normalTexture;
                                                    uniform sampler2D u_metallicRoughnessTexture;

                                                    // Lighting
                                                    uniform vec3 u_cameraPos;
                                                    uniform vec3 u_lightDirection;
                                                    uniform vec3 u_lightColor;
                                                    uniform float u_lightIntensity;

                                                    const float PI = 3.14159265359;

                                                    // Normal distribution function
                                                    float DistributionGGX(vec3 N, vec3 H, float roughness)
                                                    {
                                                        float a = roughness * roughness;
                                                        float a2 = a * a;
                                                        float NdotH = max(dot(N, H), 0.0);
                                                        float NdotH2 = NdotH * NdotH;

                                                        float num = a2;
                                                        float denom = (NdotH2 * (a2 - 1.0) + 1.0);
                                                        denom = PI * denom * denom;

                                                        return num / denom;
                                                    }

                                                    // Geometry function
                                                    float GeometrySchlickGGX(float NdotV, float roughness)
                                                    {
                                                        float r = (roughness + 1.0);
                                                        float k = (r * r) / 8.0;

                                                        float num = NdotV;
                                                        float denom = NdotV * (1.0 - k) + k;

                                                        return num / denom;
                                                    }

                                                    float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
                                                    {
                                                        float NdotV = max(dot(N, V), 0.0);
                                                        float NdotL = max(dot(N, L), 0.0);
                                                        float ggx2 = GeometrySchlickGGX(NdotV, roughness);
                                                        float ggx1 = GeometrySchlickGGX(NdotL, roughness);

                                                        return ggx1 * ggx2;
                                                    }

                                                    // Fresnel equation
                                                    vec3 fresnelSchlick(float cosTheta, vec3 F0)
                                                    {
                                                        return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
                                                    }

                                                    void main()
                                                    {
                                                        vec3 albedo = u_albedo * texture(u_albedoTexture, TexCoord).rgb;
                                                        vec3 metallicRoughness = texture(u_metallicRoughnessTexture, TexCoord).rgb;
                                                        float metallic = u_metallic * metallicRoughness.b;
                                                        float roughness = u_roughness * metallicRoughness.g;

                                                        vec3 N = normalize(Normal);
                                                        vec3 V = normalize(u_cameraPos - FragPos);
                                                        vec3 L = normalize(-u_lightDirection);
                                                        vec3 H = normalize(V + L);

                                                        // Calculate reflectance at normal incidence
                                                        vec3 F0 = vec3(0.04);
                                                        F0 = mix(F0, albedo, metallic);

                                                        // Reflectance equation
                                                        vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);
                                                        float NDF = DistributionGGX(N, H, roughness);
                                                        float G = GeometrySmith(N, V, L, roughness);

                                                        vec3 numerator = NDF * G * F;
                                                        float denominator = 4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.0001;
                                                        vec3 specular = numerator / denominator;

                                                        vec3 kS = F;
                                                        vec3 kD = vec3(1.0) - kS;
                                                        kD *= 1.0 - metallic;

                                                        float NdotL = max(dot(N, L), 0.0);
                                                        vec3 Lo = (kD * albedo / PI + specular) * u_lightColor * u_lightIntensity * NdotL;

                                                        vec3 ambient = vec3(0.03) * albedo;
                                                        vec3 color = ambient + Lo + u_emission;

                                                        // HDR tonemapping
                                                        color = color / (color + vec3(1.0));
                                                        // Gamma correction
                                                        color = pow(color, vec3(1.0/2.2));

                                                        FragColor = vec4(color, 1.0);
                                                    }
                                                    """;

    private static string GetUnlitVertexShader() => GetPbrVertexShader();

    private static string GetUnlitFragmentShader() => """
                                                      #version 330 core
                                                      out vec4 FragColor;

                                                      in vec2 TexCoord;

                                                      uniform vec3 u_albedo;
                                                      uniform sampler2D u_albedoTexture;

                                                      void main()
                                                      {
                                                          vec3 color = u_albedo * texture(u_albedoTexture, TexCoord).rgb;
                                                          FragColor = vec4(color, 1.0);
                                                      }
                                                      """;

    private static string GetSkyboxVertexShader() => """
                                                     #version 330 core
                                                     layout (location = 0) in vec3 aPos;

                                                     out vec3 TexCoords;

                                                     uniform mat4 u_projection;
                                                     uniform mat4 u_view;

                                                     void main()
                                                     {
                                                         TexCoords = aPos;
                                                         vec4 pos = u_projection * u_view * vec4(aPos, 1.0);
                                                         gl_Position = pos.xyww;
                                                     }
                                                     """;

    private static string GetSkyboxFragmentShader() => """
                                                       #version 330 core
                                                       out vec4 FragColor;

                                                       in vec3 TexCoords;

                                                       uniform samplerCube skybox;

                                                       void main()
                                                       {    
                                                           FragColor = texture(skybox, TexCoords);
                                                       }
                                                       """;

    private static string GetShadowVertexShader() => """
                                                     #version 330 core
                                                     layout (location = 0) in vec3 aPos;

                                                     uniform mat4 u_lightSpaceMatrix;
                                                     uniform mat4 u_modelMatrix;

                                                     void main()
                                                     {
                                                         gl_Position = u_lightSpaceMatrix * u_modelMatrix * vec4(aPos, 1.0);
                                                     }
                                                     """;

    private static string GetShadowFragmentShader() => """
                                                       #version 330 core

                                                       void main()
                                                       {
                                                           // Empty fragment shader for depth-only rendering
                                                       }
                                                       """;
}
