using System.Numerics;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Nodes;

namespace Beutl.Graphics3D;

/// <summary>
/// Manages shadow rendering for all shadow-casting lights.
/// </summary>
internal sealed class ShadowManager : IDisposable
{
    public const int MaxShadowMaps2D = 4;
    public const int MaxShadowMapsCube = 4;

    private readonly IGraphicsContext _context;
    private readonly IShaderCompiler _shaderCompiler;

    // Shadow passes pool
    private readonly ShadowPass[] _shadowPasses2D;
    private readonly PointShadowPass[] _pointShadowPasses;

    // Shadow map textures for shader binding
    private ITextureArray? _shadowMapArray;
    private ITextureCubeArray? _shadowMapCubeArray;

    // Shadow info for shader
    private readonly ShadowInfo[] _shadowInfos;
    private int _activeShadowCount2D;
    private int _activeShadowCountCube;

    private bool _initialized;
    private bool _disposed;

    public ShadowManager(IGraphicsContext context, IShaderCompiler shaderCompiler)
    {
        _context = context;
        _shaderCompiler = shaderCompiler;

        _shadowPasses2D = new ShadowPass[MaxShadowMaps2D];
        _pointShadowPasses = new PointShadowPass[MaxShadowMapsCube];
        _shadowInfos = new ShadowInfo[ShadowInfoArray.MaxShadows];
    }

    /// <summary>
    /// Gets the number of active 2D shadow maps.
    /// </summary>
    public int ActiveShadowCount2D => _activeShadowCount2D;

    /// <summary>
    /// Gets the number of active cube shadow maps.
    /// </summary>
    public int ActiveShadowCountCube => _activeShadowCountCube;

    /// <summary>
    /// Gets the 2D shadow map array texture.
    /// </summary>
    public ITextureArray? ShadowMapArray => _shadowMapArray;

    /// <summary>
    /// Gets the cube shadow map array texture.
    /// </summary>
    public ITextureCubeArray? ShadowMapCubeArray => _shadowMapCubeArray;

    /// <summary>
    /// Initializes the shadow manager.
    /// </summary>
    public void Initialize()
    {
        if (_initialized) return;

        // Create shadow passes
        for (int i = 0; i < MaxShadowMaps2D; i++)
        {
            _shadowPasses2D[i] = new ShadowPass(_context, _shaderCompiler);
            _shadowPasses2D[i].Initialize(ShadowPass.DefaultShadowMapSize, ShadowPass.DefaultShadowMapSize);
        }

        for (int i = 0; i < MaxShadowMapsCube; i++)
        {
            _pointShadowPasses[i] = new PointShadowPass(_context, _shaderCompiler);
            _pointShadowPasses[i].Initialize(PointShadowPass.DefaultCubeFaceSize, PointShadowPass.DefaultCubeFaceSize);
        }

        // Create shadow map array for 2D shadows
        _shadowMapArray = _context.CreateTextureArray(
            ShadowPass.DefaultShadowMapSize,
            ShadowPass.DefaultShadowMapSize,
            MaxShadowMaps2D,
            TextureFormat.Depth32Float);

        // Create shadow cube map array for point light shadows
        _shadowMapCubeArray = _context.CreateTextureCubeArray(
            PointShadowPass.DefaultCubeFaceSize,
            MaxShadowMapsCube,
            TextureFormat.Depth32Float);

        _initialized = true;
    }

    /// <summary>
    /// Renders shadow maps for all shadow-casting lights.
    /// </summary>
    /// <param name="lights">The light resources.</param>
    /// <param name="objects">The objects to render shadows for.</param>
    /// <param name="sceneCenter">Center of the scene for directional light shadows.</param>
    /// <param name="sceneRadius">Radius of the scene for directional light shadows.</param>
    /// <returns>A mapping of light index to shadow info index.</returns>
    public Dictionary<int, int> RenderShadows(
        IReadOnlyList<Light3D.Resource> lights,
        IReadOnlyList<Object3D.Resource> objects,
        Vector3 sceneCenter,
        float sceneRadius)
    {
        if (!_initialized)
            Initialize();

        var lightToShadowIndex = new Dictionary<int, int>();
        _activeShadowCount2D = 0;
        _activeShadowCountCube = 0;

        // Clear shadow infos
        Array.Clear(_shadowInfos);

        int shadowInfoIndex = 0;

        for (int lightIndex = 0; lightIndex < lights.Count && shadowInfoIndex < ShadowInfoArray.MaxShadows; lightIndex++)
        {
            var light = lights[lightIndex];

            // Check if this light casts shadows
            if (!light.CastsShadow || !light.IsEnabled)
                continue;

            ShadowInfo shadowInfo;

            switch (light)
            {
                case DirectionalLight3D.Resource directionalLight:
                    if (_activeShadowCount2D >= MaxShadowMaps2D)
                        continue;

                    shadowInfo = RenderDirectionalLightShadow(directionalLight, objects, sceneCenter, sceneRadius);
                    shadowInfo.ShadowMapIndex = _activeShadowCount2D;
                    shadowInfo.ShadowType = (int)ShadowType.Map2D;
                    _activeShadowCount2D++;
                    break;

                case SpotLight3D.Resource spotLight:
                    if (_activeShadowCount2D >= MaxShadowMaps2D)
                        continue;

                    shadowInfo = RenderSpotLightShadow(spotLight, objects);
                    shadowInfo.ShadowMapIndex = _activeShadowCount2D;
                    shadowInfo.ShadowType = (int)ShadowType.Map2D;
                    _activeShadowCount2D++;
                    break;

                case PointLight3D.Resource pointLight:
                    if (_activeShadowCountCube >= MaxShadowMapsCube)
                        continue;

                    shadowInfo = RenderPointLightShadow(pointLight, objects);
                    shadowInfo.ShadowMapIndex = _activeShadowCountCube;
                    shadowInfo.ShadowType = (int)ShadowType.Cube;
                    _activeShadowCountCube++;
                    break;

                default:
                    continue;
            }

            // Set common shadow properties
            shadowInfo.Bias = light.ShadowBias;
            shadowInfo.NormalBias = light.ShadowNormalBias;
            shadowInfo.ShadowStrength = light.ShadowStrength;

            _shadowInfos[shadowInfoIndex] = shadowInfo;
            lightToShadowIndex[lightIndex] = shadowInfoIndex;
            shadowInfoIndex++;
        }

        return lightToShadowIndex;
    }

    private ShadowInfo RenderDirectionalLightShadow(
        DirectionalLight3D.Resource light,
        IReadOnlyList<Object3D.Resource> objects,
        Vector3 sceneCenter,
        float sceneRadius)
    {
        var shadowPass = _shadowPasses2D[_activeShadowCount2D];
        shadowPass.SetupForDirectionalLight(light, sceneCenter, sceneRadius);
        shadowPass.Execute(objects);

        // Copy shadow map to array
        if (shadowPass.ShadowDepthTexture != null && _shadowMapArray != null)
        {
            _context.CopyTextureToArrayLayer(shadowPass.ShadowDepthTexture, _shadowMapArray, _activeShadowCount2D);
        }

        return new ShadowInfo
        {
            LightViewProjection = shadowPass.LightViewProjection,
            LightPosition = Vector3.Zero, // Not used for directional
            FarPlane = light.ShadowDistance
        };
    }

    private ShadowInfo RenderSpotLightShadow(
        SpotLight3D.Resource light,
        IReadOnlyList<Object3D.Resource> objects)
    {
        var shadowPass = _shadowPasses2D[_activeShadowCount2D];
        shadowPass.SetupForSpotLight(light);
        shadowPass.Execute(objects);

        // Copy shadow map to array
        if (shadowPass.ShadowDepthTexture != null && _shadowMapArray != null)
        {
            _context.CopyTextureToArrayLayer(shadowPass.ShadowDepthTexture, _shadowMapArray, _activeShadowCount2D);
        }

        return new ShadowInfo
        {
            LightViewProjection = shadowPass.LightViewProjection,
            LightPosition = light.Position,
            FarPlane = light.Range
        };
    }

    private ShadowInfo RenderPointLightShadow(
        PointLight3D.Resource light,
        IReadOnlyList<Object3D.Resource> objects)
    {
        var shadowPass = _pointShadowPasses[_activeShadowCountCube];
        shadowPass.SetupForPointLight(light);
        shadowPass.Execute(objects);

        // Copy each cube face to the cube array
        if (_shadowMapCubeArray != null)
        {
            var faceTextures = shadowPass.FaceDepthTextures;
            for (int faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                var faceTexture = faceTextures[faceIndex];
                if (faceTexture != null)
                {
                    _context.CopyTextureToCubeArrayFace(faceTexture, _shadowMapCubeArray, _activeShadowCountCube, faceIndex);
                }
            }
        }

        return new ShadowInfo
        {
            LightViewProjection = Matrix4x4.Identity, // Not used for point lights
            LightPosition = light.Position,
            FarPlane = light.Range
        };
    }

    /// <summary>
    /// Gets the shadow data for uploading to GPU.
    /// </summary>
    public ShadowUBO GetShadowUBO()
    {
        var ubo = new ShadowUBO
        {
            ShadowCount2D = _activeShadowCount2D,
            ShadowCountCube = _activeShadowCountCube
        };

        for (int i = 0; i < Math.Min(_shadowInfos.Length, ShadowInfoArray.MaxShadows); i++)
        {
            ubo.Shadows[i] = _shadowInfos[i];
        }

        return ubo;
    }

    /// <summary>
    /// Gets the shadow info array for use in shaders.
    /// </summary>
    public ReadOnlySpan<ShadowInfo> GetShadowInfos()
    {
        return _shadowInfos.AsSpan(0, Math.Min(_activeShadowCount2D + _activeShadowCountCube, ShadowInfoArray.MaxShadows));
    }

    /// <summary>
    /// Gets the 2D shadow map for the specified index.
    /// </summary>
    public ITexture2D? GetShadowMap2D(int index)
    {
        if (index < 0 || index >= _activeShadowCount2D)
            return null;

        return _shadowPasses2D[index].ShadowDepthTexture;
    }

    /// <summary>
    /// Prepares all active shadow maps for sampling in the lighting pass.
    /// Must be called after RenderShadows and before the lighting pass.
    /// </summary>
    public void PrepareForSampling()
    {
        // Prepare 2D shadow maps
        for (int i = 0; i < _activeShadowCount2D; i++)
        {
            _shadowPasses2D[i].PrepareForSampling();
        }

        // Note: CopyTextureToArrayLayer and CopyTextureToCubeArrayFace already transition
        // each layer/face to ShaderReadOnlyOptimal, so we don't need to call
        // TransitionAllToSampled here. Doing so would actually be harmful because it would
        // transition from Undefined (stale internal state) and potentially discard the data.

        // Transition the cube array to sampled state
        if (_activeShadowCountCube > 0)
        {
            _shadowMapCubeArray?.TransitionToSampled();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var pass in _shadowPasses2D)
        {
            pass?.Dispose();
        }

        foreach (var pass in _pointShadowPasses)
        {
            pass?.Dispose();
        }

        _shadowMapArray?.Dispose();
        _shadowMapCubeArray?.Dispose();
    }
}
