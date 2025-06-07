using System.Numerics;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using Beutl.Logging;

namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// OpenGLバックエンドレンダラー
/// </summary>
public class OpenGLRenderer : I3DRenderer
{
    private static readonly ILogger s_logger = Log.CreateLogger<OpenGLRenderer>();
    
    private bool _isInitialized;
    private readonly Dictionary<I3DMesh, OpenGLMeshResource> _meshCache = [];
    private readonly Dictionary<I3DMaterial, OpenGLMaterialResource> _materialCache = [];
    private readonly Dictionary<(int, int, TextureFormat), OpenGLTextureResource> _textureCache = [];

    // フレームバッファとレンダーターゲット
    private uint _gBufferFBO;
    private uint _colorTexture;
    private uint _normalTexture;
    private uint _positionTexture;
    private uint _materialTexture;
    private uint _depthTexture;

    // シェーダープログラム
    private OpenGLShaderProgram? _geometryPassShader;
    private OpenGLShaderProgram? _lightingPassShader;
    private OpenGLShaderProgram? _forwardShader;

    // 画面描画用クワッド
    private uint _quadVAO;
    private uint _quadVBO;

    public string Name => "OpenGL";

    public bool Initialize()
    {
        if (_isInitialized)
            return true;

        try
        {
            // OpenGLバージョンとレンダラー情報をログ出力
            string version = GL.GetString(StringName.Version);
            string renderer = GL.GetString(StringName.Renderer);
            string vendor = GL.GetString(StringName.Vendor);
            
            s_logger.LogInformation("OpenGL Version: {Version}", version);
            s_logger.LogInformation("OpenGL Renderer: {Renderer}", renderer);
            s_logger.LogInformation("OpenGL Vendor: {Vendor}", vendor);

            // 必要な拡張機能をチェック
            if (!CheckRequiredExtensions())
            {
                s_logger.LogError("Required OpenGL extensions not available");
                return false;
            }

            // デフォルトシェーダーを作成
            CreateDefaultShaders();

            // 画面描画用クワッドを作成
            CreateScreenQuad();

            // OpenGLの状態を設定
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.FrontFace(FrontFaceDirection.Ccw);

            _isInitialized = true;
            s_logger.LogInformation("OpenGL renderer initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to initialize OpenGL renderer");
            return false;
        }
    }

    public void Shutdown()
    {
        if (!_isInitialized)
            return;

        try
        {
            // キャッシュをクリア
            foreach (var resource in _meshCache.Values)
                resource.Dispose();
            _meshCache.Clear();

            foreach (var resource in _materialCache.Values)
                resource.Dispose();
            _materialCache.Clear();

            foreach (var resource in _textureCache.Values)
                resource.Dispose();
            _textureCache.Clear();

            // フレームバッファを削除
            if (_gBufferFBO != 0)
            {
                GL.DeleteFramebuffer(_gBufferFBO);
                GL.DeleteTexture(_colorTexture);
                GL.DeleteTexture(_normalTexture);
                GL.DeleteTexture(_positionTexture);
                GL.DeleteTexture(_materialTexture);
                GL.DeleteTexture(_depthTexture);
            }

            // シェーダーを削除
            _geometryPassShader?.Dispose();
            _lightingPassShader?.Dispose();
            _forwardShader?.Dispose();

            // クワッドを削除
            if (_quadVAO != 0)
            {
                GL.DeleteVertexArray(_quadVAO);
                GL.DeleteBuffer(_quadVBO);
            }

            _isInitialized = false;
            s_logger.LogInformation("OpenGL renderer shut down successfully");
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Error during OpenGL renderer shutdown");
        }
    }

    public void BeginFrame()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("Renderer not initialized");

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    public void EndFrame()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("Renderer not initialized");

        // フレーム終了時の処理（スワップバッファなど）
        GL.Flush();
    }

    public I3DRenderTarget CreateRenderTarget(int width, int height, TextureFormat colorFormat, TextureFormat? depthFormat = null)
    {
        return new OpenGLRenderTarget(width, height, colorFormat, depthFormat);
    }

    public I3DMeshResource CreateMesh(I3DMesh mesh)
    {
        if (_meshCache.TryGetValue(mesh, out OpenGLMeshResource? cached))
            return cached;

        var resource = new OpenGLMeshResource(mesh);
        _meshCache[mesh] = resource;
        return resource;
    }

    public ITextureResource CreateTexture(int width, int height, TextureFormat format, ReadOnlySpan<byte> data)
    {
        var key = (width, height, format);
        if (_textureCache.TryGetValue(key, out OpenGLTextureResource? cached))
            return cached;

        var resource = new OpenGLTextureResource(width, height, format, data);
        _textureCache[key] = resource;
        return resource;
    }

    public I3DMaterialResource CreateMaterial(I3DMaterial material)
    {
        if (_materialCache.TryGetValue(material, out OpenGLMaterialResource? cached))
            return cached;

        var resource = new OpenGLMaterialResource(material, this);
        _materialCache[material] = resource;
        return resource;
    }

    public IShaderProgram CreateShaderProgram(string vertexShader, string fragmentShader)
    {
        return new OpenGLShaderProgram(vertexShader, fragmentShader);
    }

    public void RenderDeferred(I3DScene scene, I3DCamera camera, I3DRenderTarget target)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("Renderer not initialized");

        var glTarget = (OpenGLRenderTarget)target;
        
        // G-Buffer Pass
        PerformGeometryPass(scene, camera, glTarget);
        
        // Lighting Pass
        PerformLightingPass(scene, camera, glTarget);
    }

    public void RenderForward(I3DScene scene, I3DCamera camera, I3DRenderTarget target)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("Renderer not initialized");

        var glTarget = (OpenGLRenderTarget)target;
        
        // フォワードレンダリングパス
        PerformForwardPass(scene, camera, glTarget);
    }

    private bool CheckRequiredExtensions()
    {
        // 必要な拡張機能をチェック
        // 例: GL_ARB_framebuffer_object, GL_ARB_vertex_array_object など
        return true; // 簡略化
    }

    private void CreateDefaultShaders()
    {
        // 基本的なPBRシェーダーを作成
        string geometryVertexShader = GetGeometryVertexShader();
        string geometryFragmentShader = GetGeometryFragmentShader();
        _geometryPassShader = new OpenGLShaderProgram(geometryVertexShader, geometryFragmentShader);

        string lightingVertexShader = GetLightingVertexShader();
        string lightingFragmentShader = GetLightingFragmentShader();
        _lightingPassShader = new OpenGLShaderProgram(lightingVertexShader, lightingFragmentShader);

        string forwardVertexShader = GetForwardVertexShader();
        string forwardFragmentShader = GetForwardFragmentShader();
        _forwardShader = new OpenGLShaderProgram(forwardVertexShader, forwardFragmentShader);
    }

    private void CreateScreenQuad()
    {
        float[] quadVertices = [
            // 位置        // テクスチャ座標
            -1.0f,  1.0f, 0.0f, 1.0f,
            -1.0f, -1.0f, 0.0f, 0.0f,
             1.0f, -1.0f, 1.0f, 0.0f,
            -1.0f,  1.0f, 0.0f, 1.0f,
             1.0f, -1.0f, 1.0f, 0.0f,
             1.0f,  1.0f, 1.0f, 1.0f
        ];

        _quadVAO = GL.GenVertexArray();
        _quadVBO = GL.GenBuffer();

        GL.BindVertexArray(_quadVAO);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _quadVBO);
        GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * sizeof(float), quadVertices, BufferUsageHint.StaticDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

        GL.BindVertexArray(0);
    }

    private void PerformGeometryPass(I3DScene scene, I3DCamera camera, OpenGLRenderTarget target)
    {
        // G-Bufferにレンダリング
        SetupGBuffer(target.Width, target.Height);
        
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _gBufferFBO);
        GL.Viewport(0, 0, target.Width, target.Height);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _geometryPassShader!.Use();
        _geometryPassShader.SetUniform("u_viewMatrix", camera.ViewMatrix);
        _geometryPassShader.SetUniform("u_projectionMatrix", camera.ProjectionMatrix);

        foreach (var obj in scene.Objects)
        {
            _geometryPassShader.SetUniform("u_modelMatrix", obj.Transform);
            RenderObject(obj);
        }
    }

    private void PerformLightingPass(I3DScene scene, I3DCamera camera, OpenGLRenderTarget target)
    {
        // 最終フレームバッファにレンダリング
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, target.FramebufferId);
        GL.Viewport(0, 0, target.Width, target.Height);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _lightingPassShader!.Use();

        // G-Bufferテクスチャをバインド
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _colorTexture);
        _lightingPassShader.SetUniform("u_gPosition", 0);

        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _normalTexture);
        _lightingPassShader.SetUniform("u_gNormal", 1);

        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, _positionTexture);
        _lightingPassShader.SetUniform("u_gAlbedo", 2);

        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, _materialTexture);
        _lightingPassShader.SetUniform("u_gMaterial", 3);

        // ライト情報を設定
        SetupLights(scene.Lights);

        // カメラ位置を設定
        _lightingPassShader.SetUniform("u_cameraPos", camera.Position);

        // フルスクリーンクワッドを描画
        GL.BindVertexArray(_quadVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);
    }

    private void PerformForwardPass(I3DScene scene, I3DCamera camera, OpenGLRenderTarget target)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, target.FramebufferId);
        GL.Viewport(0, 0, target.Width, target.Height);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _forwardShader!.Use();
        _forwardShader.SetUniform("u_viewMatrix", camera.ViewMatrix);
        _forwardShader.SetUniform("u_projectionMatrix", camera.ProjectionMatrix);
        _forwardShader.SetUniform("u_cameraPos", camera.Position);

        // ライト情報を設定
        SetupLights(scene.Lights);

        foreach (var obj in scene.Objects)
        {
            _forwardShader.SetUniform("u_modelMatrix", obj.Transform);
            RenderObject(obj);
        }
    }

    private void RenderObject(I3DRenderableObject obj)
    {
        var meshResource = (OpenGLMeshResource)obj.Mesh;
        var materialResource = (OpenGLMaterialResource)obj.Material;

        // マテリアルテクスチャをバインド
        materialResource.Bind();

        // メッシュを描画
        GL.BindVertexArray(meshResource.VAO);
        GL.DrawElements(PrimitiveType.Triangles, meshResource.IndexCount, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);
    }

    private void SetupGBuffer(int width, int height)
    {
        if (_gBufferFBO == 0 || _colorTexture == 0)
        {
            CreateGBuffer(width, height);
        }
    }

    private void CreateGBuffer(int width, int height)
    {
        // フレームバッファを作成
        _gBufferFBO = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _gBufferFBO);

        // Position texture (RGB32F)
        _positionTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _positionTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb32f, width, height, 0, PixelFormat.Rgb, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _positionTexture, 0);

        // Normal texture (RGB32F)
        _normalTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _normalTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb32f, width, height, 0, PixelFormat.Rgb, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, _normalTexture, 0);

        // Albedo texture (RGBA8)
        _colorTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _colorTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment2, TextureTarget.Texture2D, _colorTexture, 0);

        // Material texture (RGBA8) - Metallic, Roughness, AO, etc.
        _materialTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _materialTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment3, TextureTarget.Texture2D, _materialTexture, 0);

        // Depth texture
        _depthTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _depthTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent32f, width, height, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, _depthTexture, 0);

        // 描画バッファを指定
        DrawBuffersEnum[] drawBuffers = [
            DrawBuffersEnum.ColorAttachment0,
            DrawBuffersEnum.ColorAttachment1,
            DrawBuffersEnum.ColorAttachment2,
            DrawBuffersEnum.ColorAttachment3
        ];
        GL.DrawBuffers(drawBuffers.Length, drawBuffers);

        // フレームバッファの完成度をチェック
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
        {
            s_logger.LogError("G-Buffer framebuffer not complete");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void SetupLights(IReadOnlyList<ILight> lights)
    {
        // ライト情報をシェーダーに送信する実装
        // 簡略化のため、最初のライトのみを処理
        if (lights.Count > 0 && lights[0] is DirectionalLight directionalLight)
        {
            var shader = _lightingPassShader ?? _forwardShader;
            shader?.SetUniform("u_lightDirection", directionalLight.Direction);
            shader?.SetUniform("u_lightColor", directionalLight.Color);
            shader?.SetUniform("u_lightIntensity", directionalLight.Intensity);
        }
    }

    public void Dispose()
    {
        Shutdown();
    }

    // シェーダーソースコードの取得メソッド
    private static string GetGeometryVertexShader() => """
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

    private static string GetGeometryFragmentShader() => """
        #version 330 core
        layout (location = 0) out vec3 gPosition;
        layout (location = 1) out vec3 gNormal;
        layout (location = 2) out vec4 gAlbedo;
        layout (location = 3) out vec4 gMaterial;

        in vec3 FragPos;
        in vec3 Normal;
        in vec2 TexCoord;

        uniform vec3 u_albedo;
        uniform float u_metallic;
        uniform float u_roughness;
        uniform sampler2D u_albedoTexture;
        uniform sampler2D u_normalTexture;
        uniform sampler2D u_metallicRoughnessTexture;

        void main()
        {
            gPosition = FragPos;
            gNormal = normalize(Normal);
            gAlbedo = vec4(u_albedo, 1.0) * texture(u_albedoTexture, TexCoord);
            
            vec3 metallicRoughness = texture(u_metallicRoughnessTexture, TexCoord).rgb;
            gMaterial = vec4(u_metallic * metallicRoughness.b, u_roughness * metallicRoughness.g, 0.0, 1.0);
        }
        """;

    private static string GetLightingVertexShader() => """
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

    private static string GetLightingFragmentShader() => """
        #version 330 core
        out vec4 FragColor;

        in vec2 TexCoord;

        uniform sampler2D u_gPosition;
        uniform sampler2D u_gNormal;
        uniform sampler2D u_gAlbedo;
        uniform sampler2D u_gMaterial;

        uniform vec3 u_cameraPos;
        uniform vec3 u_lightDirection;
        uniform vec3 u_lightColor;
        uniform float u_lightIntensity;

        vec3 calculatePBR(vec3 albedo, float metallic, float roughness, vec3 normal, vec3 lightDir, vec3 viewDir)
        {
            // 簡略化されたPBR計算
            float NdotL = max(dot(normal, lightDir), 0.0);
            vec3 diffuse = albedo * NdotL;
            return diffuse * u_lightColor * u_lightIntensity;
        }

        void main()
        {
            vec3 FragPos = texture(u_gPosition, TexCoord).rgb;
            vec3 Normal = texture(u_gNormal, TexCoord).rgb;
            vec3 Albedo = texture(u_gAlbedo, TexCoord).rgb;
            vec4 Material = texture(u_gMaterial, TexCoord);
            
            float metallic = Material.r;
            float roughness = Material.g;

            vec3 lightDir = normalize(-u_lightDirection);
            vec3 viewDir = normalize(u_cameraPos - FragPos);

            vec3 color = calculatePBR(Albedo, metallic, roughness, Normal, lightDir, viewDir);
            
            FragColor = vec4(color, 1.0);
        }
        """;

    private static string GetForwardVertexShader() => GetGeometryVertexShader();

    private static string GetForwardFragmentShader() => """
        #version 330 core
        out vec4 FragColor;

        in vec3 FragPos;
        in vec3 Normal;
        in vec2 TexCoord;

        uniform vec3 u_cameraPos;
        uniform vec3 u_lightDirection;
        uniform vec3 u_lightColor;
        uniform float u_lightIntensity;

        uniform vec3 u_albedo;
        uniform float u_metallic;
        uniform float u_roughness;
        uniform sampler2D u_albedoTexture;

        vec3 calculatePBR(vec3 albedo, float metallic, float roughness, vec3 normal, vec3 lightDir, vec3 viewDir)
        {
            // 簡略化されたPBR計算
            float NdotL = max(dot(normal, lightDir), 0.0);
            vec3 diffuse = albedo * NdotL;
            return diffuse * u_lightColor * u_lightIntensity;
        }

        void main()
        {
            vec3 albedo = u_albedo * texture(u_albedoTexture, TexCoord).rgb;
            vec3 normal = normalize(Normal);
            
            vec3 lightDir = normalize(-u_lightDirection);
            vec3 viewDir = normalize(u_cameraPos - FragPos);

            vec3 color = calculatePBR(albedo, u_metallic, u_roughness, normal, lightDir, viewDir);
            
            FragColor = vec4(color, 1.0);
        }
        """;
}