using System.Numerics;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 遅延レンダリングパイプライン
/// </summary>
public class DeferredRenderPipeline : IDisposable
{
    private readonly I3DRenderer _renderer;
    private readonly RenderPipelineSettings _settings;
    private bool _disposed;

    // レンダーターゲット
    private I3DRenderTarget? _gBuffer;
    private I3DRenderTarget? _shadowMap;
    private I3DRenderTarget? _finalTarget;

    // シェーダー
    private IShaderProgram? _geometryPassShader;
    private IShaderProgram? _lightingPassShader;
    private IShaderProgram? _shadowMapShader;
    private IShaderProgram? _postProcessShader;

    public DeferredRenderPipeline(I3DRenderer renderer, RenderPipelineSettings? settings = null)
    {
        _renderer = renderer;
        _settings = settings ?? new RenderPipelineSettings();
        Initialize();
    }

    private void Initialize()
    {
        // シェーダーを作成
        CreateShaders();
    }

    private void CreateShaders()
    {
        // ジオメトリパスシェーダー
        _geometryPassShader = _renderer.CreateShaderProgram(
            GetGeometryPassVertexShader(),
            GetGeometryPassFragmentShader()
        );

        // ライティングパスシェーダー
        _lightingPassShader = _renderer.CreateShaderProgram(
            GetLightingPassVertexShader(),
            GetLightingPassFragmentShader()
        );

        // シャドウマップシェーダー
        if (_settings.EnableShadows)
        {
            _shadowMapShader = _renderer.CreateShaderProgram(
                GetShadowMapVertexShader(),
                GetShadowMapFragmentShader()
            );
        }

        // ポストプロセスシェーダー
        _postProcessShader = _renderer.CreateShaderProgram(
            GetPostProcessVertexShader(),
            GetPostProcessFragmentShader()
        );
    }

    /// <summary>
    /// レンダーターゲットを設定
    /// </summary>
    public void SetupRenderTargets(int width, int height)
    {
        // 既存のレンダーターゲットを破棄
        _gBuffer?.Dispose();
        _shadowMap?.Dispose();
        _finalTarget?.Dispose();

        // G-Bufferを作成
        _gBuffer = _renderer.CreateRenderTarget(width, height, TextureFormat.Rgba16F, TextureFormat.Depth32F);

        // シャドウマップを作成
        if (_settings.EnableShadows)
        {
            _shadowMap = _renderer.CreateRenderTarget(_settings.ShadowMapSize, _settings.ShadowMapSize, TextureFormat.R32F, TextureFormat.Depth32F);
        }

        // 最終レンダーターゲットを作成
        _finalTarget = _renderer.CreateRenderTarget(width, height, TextureFormat.Rgba8, TextureFormat.Depth24);
    }

    /// <summary>
    /// シーンをレンダリング
    /// </summary>
    public void Render(DeferredRenderContext context)
    {
        if (_gBuffer == null || _finalTarget == null)
        {
            throw new InvalidOperationException("Render targets not set up. Call SetupRenderTargets first.");
        }

        // 1. シャドウマップの生成
        if (_settings.EnableShadows && _shadowMap != null)
        {
            RenderShadowMaps(context);
        }

        // 2. ジオメトリパス（G-Bufferに描画）
        RenderGeometryPass(context);

        // 3. ライティングパス（G-Bufferからライティング計算）
        RenderLightingPass(context);

        // 4. フォワードパス（透明オブジェクトなど）
        RenderForwardPass(context);

        // 5. ポストプロセス
        RenderPostProcess(context);
    }

    private void RenderShadowMaps(DeferredRenderContext context)
    {
        if (_shadowMap == null || _shadowMapShader == null)
            return;

        // 方向光源のシャドウマップ
        foreach (var light in context.Scene.Lights.OfType<DirectionalLight>())
        {
            if (!light.CastShadows || !light.Enabled)
                continue;

            RenderDirectionalLightShadowMap(light, context);
        }

        // 点光源のシャドウマップ（キューブマップ）
        foreach (var light in context.Scene.Lights.OfType<PointLight>())
        {
            if (!light.CastShadows || !light.Enabled)
                continue;

            RenderPointLightShadowMap(light, context);
        }
    }

    private void RenderDirectionalLightShadowMap(DirectionalLight light, DeferredRenderContext context)
    {
        // ライト空間の変換行列を計算
        var lightViewMatrix = CalculateLightViewMatrix(light, context.Scene);
        var lightProjectionMatrix = CalculateLightProjectionMatrix(light);
        var lightSpaceMatrix = lightViewMatrix * lightProjectionMatrix;

        // シャドウマップにレンダリング
        BindShadowMapTarget();
        _shadowMapShader!.Use();
        _shadowMapShader.SetUniform("u_lightSpaceMatrix", lightSpaceMatrix);

        foreach (var obj in context.Scene.Objects)
        {
            if (!obj.CastShadows)
                continue;

            _shadowMapShader.SetUniform("u_modelMatrix", obj.Transform);
            RenderObjectGeometry(obj);
        }
    }

    private void RenderPointLightShadowMap(PointLight light, DeferredRenderContext context)
    {
        // 6面のキューブマップを生成
        var lightPosition = light.Position;

        // 各面の方向とアップベクトル
        var directions = new (Vector3 target, Vector3 up)[]
        {
            (Vector3.UnitX, Vector3.UnitY),   // +X
            (-Vector3.UnitX, Vector3.UnitY),  // -X
            (Vector3.UnitY, Vector3.UnitZ),   // +Y
            (-Vector3.UnitY, -Vector3.UnitZ), // -Y
            (Vector3.UnitZ, Vector3.UnitY),   // +Z
            (-Vector3.UnitZ, Vector3.UnitY)   // -Z
        };

        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2, 1.0f, 0.1f, light.Range);

        for (int i = 0; i < 6; i++)
        {
            var view = Matrix4x4.CreateLookAt(lightPosition, lightPosition + directions[i].target, directions[i].up);
            var lightSpaceMatrix = view * projection;

            // 各面をレンダリング
            BindShadowMapTarget();
            _shadowMapShader!.Use();
            _shadowMapShader.SetUniform("u_lightSpaceMatrix", lightSpaceMatrix);
            _shadowMapShader.SetUniform("u_lightPos", lightPosition);
            _shadowMapShader.SetUniform("u_farPlane", light.Range);

            foreach (var obj in context.Scene.Objects)
            {
                if (!obj.CastShadows)
                    continue;

                _shadowMapShader.SetUniform("u_modelMatrix", obj.Transform);
                RenderObjectGeometry(obj);
            }
        }
    }

    private void RenderGeometryPass(DeferredRenderContext context)
    {
        // G-Bufferをバインド
        BindGBufferTarget();

        _geometryPassShader!.Use();
        _geometryPassShader.SetUniform("u_viewMatrix", context.Camera.ViewMatrix);
        _geometryPassShader.SetUniform("u_projectionMatrix", context.Camera.ProjectionMatrix);

        foreach (var obj in context.Scene.Objects)
        {
            _geometryPassShader.SetUniform("u_modelMatrix", obj.Transform);

            // マテリアルパラメータを設定
            SetMaterialUniforms(_geometryPassShader, obj.Material);

            RenderObjectGeometry(obj);
        }
    }

    private void RenderLightingPass(DeferredRenderContext context)
    {
        // 最終ターゲットをバインド
        BindFinalTarget();

        _lightingPassShader!.Use();

        // G-Bufferテクスチャをバインド
        BindGBufferTextures();

        // シャドウマップをバインド
        if (_settings.EnableShadows && _shadowMap != null)
        {
            BindShadowMapTextures();
        }

        // カメラ情報を設定
        _lightingPassShader.SetUniform("u_cameraPos", context.Camera.Position);
        _lightingPassShader.SetUniform("u_viewMatrix", context.Camera.ViewMatrix);
        _lightingPassShader.SetUniform("u_projectionMatrix", context.Camera.ProjectionMatrix);

        // ライト情報を設定
        SetLightingUniforms(context.Scene.Lights);

        // 環境マッピング
        if (_settings.EnableEnvironmentMapping && context.Scene.EnvironmentMap != null)
        {
            SetEnvironmentMappingUniforms(context.Scene.EnvironmentMap);
        }

        // フルスクリーンクワッドを描画
        RenderFullscreenQuad();
    }

    private void RenderForwardPass(DeferredRenderContext context)
    {
        // 透明オブジェクトや特殊なマテリアルをフォワードレンダリング
        // 既存の最終ターゲットに追加で描画

        // アルファブレンディングを有効化
        EnableAlphaBlending();

        foreach (var obj in context.Scene.Objects)
        {
            // 透明なオブジェクトのみをレンダリング
            if (IsTransparentObject(obj))
            {
                RenderObjectForward(obj, context);
            }
        }

        DisableAlphaBlending();
    }

    private void RenderPostProcess(DeferredRenderContext context)
    {
        // トーンマッピング、ガンマ補正、その他のポストエフェクト
        _postProcessShader!.Use();

        // 最終ターゲットのテクスチャをバインド
        BindFinalTargetTexture();

        // ポストプロセスパラメータを設定
        _postProcessShader.SetUniform("u_exposure", context.PostProcessSettings.Exposure);
        _postProcessShader.SetUniform("u_gamma", context.PostProcessSettings.Gamma);

        RenderFullscreenQuad();
    }

    private Matrix4x4 CalculateLightViewMatrix(DirectionalLight light, I3DScene scene)
    {
        // シーンのバウンディングボックスを計算
        var sceneBounds = CalculateSceneBounds(scene);
        var center = sceneBounds.Center;
        var lightDirection = Vector3.Normalize(light.Direction);

        return Matrix4x4.CreateLookAt(center - lightDirection * 100f, center, Vector3.UnitY);
    }

    private Matrix4x4 CalculateLightProjectionMatrix(DirectionalLight light)
    {
        // 正射影行列を使用
        float size = 50f; // シーンサイズに応じて調整
        return Matrix4x4.CreateOrthographic(size, size, 0.1f, 200f);
    }

    private SceneBounds CalculateSceneBounds(I3DScene scene)
    {
        // 簡略化：固定のバウンディングボックス
        return new SceneBounds
        {
            Min = new Vector3(-25f, -25f, -25f),
            Max = new Vector3(25f, 25f, 25f),
            Center = Vector3.Zero
        };
    }

    private void BindShadowMapTarget()
    {
        // 実装：シャドウマップターゲットをバインド
    }

    private void BindGBufferTarget()
    {
        // 実装：G-Bufferターゲットをバインド
    }

    private void BindFinalTarget()
    {
        // 実装：最終ターゲットをバインド
    }

    private void BindGBufferTextures()
    {
        // 実装：G-Bufferテクスチャをバインド
    }

    private void BindShadowMapTextures()
    {
        // 実装：シャドウマップテクスチャをバインド
    }

    private void BindFinalTargetTexture()
    {
        // 実装：最終ターゲットのテクスチャをバインド
    }

    private void SetMaterialUniforms(IShaderProgram shader, I3DMaterialResource material)
    {
        // マテリアルパラメータをシェーダーに設定
        shader.SetUniform("u_albedo", material.SourceMaterial.Albedo);
        shader.SetUniform("u_metallic", material.SourceMaterial.Metallic);
        shader.SetUniform("u_roughness", material.SourceMaterial.Roughness);
        shader.SetUniform("u_emission", material.SourceMaterial.Emission);
    }

    private void SetLightingUniforms(IReadOnlyList<ILight> lights)
    {
        // ライト情報をシェーダーに設定
        int lightCount = Math.Min(lights.Count, _settings.MaxLights);
        _lightingPassShader!.SetUniform("u_lightCount", lightCount);

        for (int i = 0; i < lightCount; i++)
        {
            var light = lights[i];
            string prefix = $"u_lights[{i}].";

            _lightingPassShader.SetUniform(prefix + "type", (int)light.Type);
            _lightingPassShader.SetUniform(prefix + "color", light.Color);
            _lightingPassShader.SetUniform(prefix + "intensity", light.Intensity);
            _lightingPassShader.SetUniform(prefix + "enabled", light.Enabled);

            switch (light)
            {
                case DirectionalLight directionalLight:
                    _lightingPassShader.SetUniform(prefix + "direction", directionalLight.Direction);
                    break;
                case PointLight pointLight:
                    _lightingPassShader.SetUniform(prefix + "position", pointLight.Position);
                    _lightingPassShader.SetUniform(prefix + "range", pointLight.Range);
                    break;
                case SpotLight spotLight:
                    _lightingPassShader.SetUniform(prefix + "position", spotLight.Position);
                    _lightingPassShader.SetUniform(prefix + "direction", spotLight.Direction);
                    _lightingPassShader.SetUniform(prefix + "range", spotLight.Range);
                    _lightingPassShader.SetUniform(prefix + "innerCone", spotLight.InnerConeAngle);
                    _lightingPassShader.SetUniform(prefix + "outerCone", spotLight.OuterConeAngle);
                    break;
            }
        }
    }

    private void SetEnvironmentMappingUniforms(IEnvironmentMap environmentMap)
    {
        // 環境マッピングのテクスチャをバインド
        _lightingPassShader!.SetTexture("u_environmentMap", (ITextureResource)environmentMap.EnvironmentTexture, 10);
        if (environmentMap.IrradianceTexture != null)
            _lightingPassShader.SetTexture("u_irradianceMap", (ITextureResource)environmentMap.IrradianceTexture, 11);
        if (environmentMap.PrefilterTexture != null)
            _lightingPassShader.SetTexture("u_prefilterMap", (ITextureResource)environmentMap.PrefilterTexture, 12);
        if (environmentMap.BrdfLutTexture != null)
            _lightingPassShader.SetTexture("u_brdfLut", (ITextureResource)environmentMap.BrdfLutTexture, 13);
    }

    private void RenderObjectGeometry(I3DRenderableObject obj)
    {
        // メッシュジオメトリを描画
        // 実装は具体的なレンダラーに依存
    }

    private void RenderObjectForward(I3DRenderableObject obj, DeferredRenderContext context)
    {
        // フォワードレンダリングでオブジェクトを描画
    }

    private void RenderFullscreenQuad()
    {
        // フルスクリーンクワッドを描画
        // 実装は具体的なレンダラーに依存
    }

    private void EnableAlphaBlending()
    {
        // アルファブレンディングを有効化
    }

    private void DisableAlphaBlending()
    {
        // アルファブレンディングを無効化
    }

    private bool IsTransparentObject(I3DRenderableObject obj)
    {
        // オブジェクトが透明かどうかを判定
        return false; // 簡略化
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _gBuffer?.Dispose();
        _shadowMap?.Dispose();
        _finalTarget?.Dispose();

        _geometryPassShader?.Dispose();
        _lightingPassShader?.Dispose();
        _shadowMapShader?.Dispose();
        _postProcessShader?.Dispose();

        _disposed = true;
    }

    // シェーダーソースコード取得メソッド
    private static string GetGeometryPassVertexShader() => """
        #version 330 core
        layout (location = 0) in vec3 aPos;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aTexCoord;

        out vec3 FragPos;
        out vec3 Normal;
        out vec2 TexCoord;
        out vec4 FragPosLightSpace;

        uniform mat4 u_modelMatrix;
        uniform mat4 u_viewMatrix;
        uniform mat4 u_projectionMatrix;
        uniform mat4 u_lightSpaceMatrix;

        void main()
        {
            vec4 worldPos = u_modelMatrix * vec4(aPos, 1.0);
            FragPos = worldPos.xyz;
            Normal = mat3(transpose(inverse(u_modelMatrix))) * aNormal;
            TexCoord = aTexCoord;
            FragPosLightSpace = u_lightSpaceMatrix * worldPos;

            gl_Position = u_projectionMatrix * u_viewMatrix * worldPos;
        }
        """;

    private static string GetGeometryPassFragmentShader() => """
        #version 330 core
        layout (location = 0) out vec4 gPosition;
        layout (location = 1) out vec4 gNormal;
        layout (location = 2) out vec4 gAlbedo;
        layout (location = 3) out vec4 gMaterial;

        in vec3 FragPos;
        in vec3 Normal;
        in vec2 TexCoord;
        in vec4 FragPosLightSpace;

        uniform vec3 u_albedo;
        uniform float u_metallic;
        uniform float u_roughness;
        uniform vec3 u_emission;
        uniform sampler2D u_albedoTexture;
        uniform sampler2D u_normalTexture;
        uniform sampler2D u_metallicRoughnessTexture;

        void main()
        {
            gPosition = vec4(FragPos, 1.0);
            gNormal = vec4(normalize(Normal), 1.0);
            gAlbedo = vec4(u_albedo, 1.0) * texture(u_albedoTexture, TexCoord);

            vec3 metallicRoughness = texture(u_metallicRoughnessTexture, TexCoord).rgb;
            gMaterial = vec4(u_metallic * metallicRoughness.b, u_roughness * metallicRoughness.g, 0.0, 1.0);
        }
        """;

    private static string GetLightingPassVertexShader() => """
        #version 330 core
        layout (location = 0) in vec2 aPos;
        layout (location = 1) in vec2 aTexCoord;

        out vec2 TexCoord;

        void main()
        {
            TexCoord = aTexCoord;
            gl_Position = vec4(aPos, 0.0, 1.0);
        }
        """;

    private static string GetLightingPassFragmentShader() => """
        #version 330 core
        out vec4 FragColor;

        in vec2 TexCoord;

        uniform sampler2D u_gPosition;
        uniform sampler2D u_gNormal;
        uniform sampler2D u_gAlbedo;
        uniform sampler2D u_gMaterial;
        uniform sampler2D u_shadowMap;

        uniform vec3 u_cameraPos;
        uniform int u_lightCount;

        struct Light {
            int type;
            vec3 color;
            float intensity;
            bool enabled;
            vec3 position;
            vec3 direction;
            float range;
            float innerCone;
            float outerCone;
        };

        uniform Light u_lights[32];

        const float PI = 3.14159265359;

        vec3 calculatePBR(vec3 albedo, float metallic, float roughness, vec3 normal, vec3 lightDir, vec3 viewDir, vec3 lightColor, float lightIntensity)
        {
            // PBR計算の実装（前回のものと同様）
            float NdotL = max(dot(normal, lightDir), 0.0);
            vec3 diffuse = albedo * NdotL;
            return diffuse * lightColor * lightIntensity;
        }

        void main()
        {
            vec3 FragPos = texture(u_gPosition, TexCoord).rgb;
            vec3 Normal = texture(u_gNormal, TexCoord).rgb;
            vec3 Albedo = texture(u_gAlbedo, TexCoord).rgb;
            vec4 Material = texture(u_gMaterial, TexCoord);

            float metallic = Material.r;
            float roughness = Material.g;

            vec3 viewDir = normalize(u_cameraPos - FragPos);
            vec3 finalColor = vec3(0.0);

            // 全ライトに対してライティング計算
            for(int i = 0; i < u_lightCount && i < 32; i++)
            {
                if(!u_lights[i].enabled) continue;

                vec3 lightDir;
                float attenuation = 1.0;

                if(u_lights[i].type == 0) // Directional Light
                {
                    lightDir = normalize(-u_lights[i].direction);
                }
                else if(u_lights[i].type == 1) // Point Light
                {
                    lightDir = normalize(u_lights[i].position - FragPos);
                    float distance = length(u_lights[i].position - FragPos);
                    attenuation = 1.0 / (1.0 + 0.09 * distance + 0.032 * distance * distance);
                }
                else if(u_lights[i].type == 2) // Spot Light
                {
                    lightDir = normalize(u_lights[i].position - FragPos);
                    float distance = length(u_lights[i].position - FragPos);
                    attenuation = 1.0 / (1.0 + 0.09 * distance + 0.032 * distance * distance);

                    float theta = dot(lightDir, normalize(-u_lights[i].direction));
                    float epsilon = cos(u_lights[i].innerCone) - cos(u_lights[i].outerCone);
                    float intensity = clamp((theta - cos(u_lights[i].outerCone)) / epsilon, 0.0, 1.0);
                    attenuation *= intensity;
                }

                vec3 lightContribution = calculatePBR(Albedo, metallic, roughness, Normal, lightDir, viewDir, u_lights[i].color, u_lights[i].intensity);
                finalColor += lightContribution * attenuation;
            }

            // 環境光を追加
            vec3 ambient = vec3(0.03) * Albedo;
            finalColor += ambient;

            FragColor = vec4(finalColor, 1.0);
        }
        """;

    private static string GetShadowMapVertexShader() => """
        #version 330 core
        layout (location = 0) in vec3 aPos;

        uniform mat4 u_lightSpaceMatrix;
        uniform mat4 u_modelMatrix;

        void main()
        {
            gl_Position = u_lightSpaceMatrix * u_modelMatrix * vec4(aPos, 1.0);
        }
        """;

    private static string GetShadowMapFragmentShader() => """
        #version 330 core

        void main()
        {
            // デプスのみを書き込み
        }
        """;

    private static string GetPostProcessVertexShader() => """
        #version 330 core
        layout (location = 0) in vec2 aPos;
        layout (location = 1) in vec2 aTexCoord;

        out vec2 TexCoord;

        void main()
        {
            TexCoord = aTexCoord;
            gl_Position = vec4(aPos, 0.0, 1.0);
        }
        """;

    private static string GetPostProcessFragmentShader() => """
        #version 330 core
        out vec4 FragColor;

        in vec2 TexCoord;

        uniform sampler2D u_sceneTexture;
        uniform float u_exposure;
        uniform float u_gamma;

        void main()
        {
            vec3 hdrColor = texture(u_sceneTexture, TexCoord).rgb;

            // トーンマッピング (Reinhard)
            vec3 mapped = hdrColor / (hdrColor + vec3(1.0));

            // 露出補正
            mapped = vec3(1.0) - exp(-mapped * u_exposure);

            // ガンマ補正
            mapped = pow(mapped, vec3(1.0 / u_gamma));

            FragColor = vec4(mapped, 1.0);
        }
        """;
}
