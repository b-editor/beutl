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
    public int ActiveShadowCount2D
    {
        get
        {
            ThrowIfNotInitialized();
            return _activeShadowCount2D;
        }
    }

    /// <summary>
    /// Gets the number of active cube shadow maps.
    /// </summary>
    public int ActiveShadowCountCube
    {
        get
        {
            ThrowIfNotInitialized();
            return _activeShadowCountCube;
        }
    }

    /// <summary>
    /// Gets the 2D shadow map array texture.
    /// </summary>
    public ITextureArray? ShadowMapArray
    {
        get
        {
            ThrowIfNotInitialized();
            return _shadowMapArray;
        }
    }

    /// <summary>
    /// Gets the cube shadow map array texture.
    /// </summary>
    public ITextureCubeArray? ShadowMapCubeArray
    {
        get
        {
            ThrowIfNotInitialized();
            return _shadowMapCubeArray;
        }
    }

    /// <summary>
    /// Initializes the shadow manager.
    /// </summary>
    public void Initialize()
    {
        ThrowIfDisposed();
        if (_initialized)
            throw new InvalidOperationException($"{nameof(ShadowManager)} is already initialized.");

        var shadowPasses2D = new ShadowPass?[MaxShadowMaps2D];
        var pointShadowPasses = new PointShadowPass?[MaxShadowMapsCube];
        ITextureArray? shadowMapArray = null;
        ITextureCubeArray? shadowMapCubeArray = null;
        try
        {
            // Create shadow passes
            for (int i = 0; i < MaxShadowMaps2D; i++)
            {
                var pass = new ShadowPass(_context, _shaderCompiler);
                shadowPasses2D[i] = pass;
                pass.Initialize(ShadowPass.DefaultShadowMapSize, ShadowPass.DefaultShadowMapSize);
            }

            for (int i = 0; i < MaxShadowMapsCube; i++)
            {
                var pass = new PointShadowPass(_context, _shaderCompiler);
                pointShadowPasses[i] = pass;
                pass.Initialize(PointShadowPass.DefaultCubeFaceSize, PointShadowPass.DefaultCubeFaceSize);
            }

            // Create shadow map array for 2D shadows
            shadowMapArray = _context.CreateTextureArray(
                ShadowPass.DefaultShadowMapSize,
                ShadowPass.DefaultShadowMapSize,
                MaxShadowMaps2D,
                TextureFormat.Depth32Float);

            // Create shadow cube map array for point light shadows
            shadowMapCubeArray = _context.CreateTextureCubeArray(
                PointShadowPass.DefaultCubeFaceSize,
                MaxShadowMapsCube,
                TextureFormat.Depth32Float);
        }
        catch
        {
            Exception? ignoredCleanupFailure = null;
            for (int i = shadowPasses2D.Length - 1; i >= 0; i--)
            {
                Graphics3DDisposal.Capture(shadowPasses2D[i], ref ignoredCleanupFailure);
            }

            for (int i = pointShadowPasses.Length - 1; i >= 0; i--)
            {
                Graphics3DDisposal.Capture(pointShadowPasses[i], ref ignoredCleanupFailure);
            }

            Graphics3DDisposal.Capture(shadowMapArray, ref ignoredCleanupFailure);
            Graphics3DDisposal.Capture(shadowMapCubeArray, ref ignoredCleanupFailure);
            throw;
        }

        for (int i = 0; i < shadowPasses2D.Length; i++)
        {
            _shadowPasses2D[i] = shadowPasses2D[i]!;
        }

        for (int i = 0; i < pointShadowPasses.Length; i++)
        {
            _pointShadowPasses[i] = pointShadowPasses[i]!;
        }

        _shadowMapArray = shadowMapArray;
        _shadowMapCubeArray = shadowMapCubeArray;

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
        ThrowIfDisposed();
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
        ThrowIfNotInitialized();
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
        ThrowIfNotInitialized();
        return _shadowInfos.AsSpan(0, Math.Min(_activeShadowCount2D + _activeShadowCountCube, ShadowInfoArray.MaxShadows));
    }

    /// <summary>
    /// Gets the 2D shadow map for the specified index.
    /// </summary>
    public ITexture2D? GetShadowMap2D(int index)
    {
        ThrowIfNotInitialized();
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
        ThrowIfNotInitialized();
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
        _initialized = false;
        _activeShadowCount2D = 0;
        _activeShadowCountCube = 0;
        Array.Clear(_shadowInfos);

        Exception? cleanupFailure = null;
        for (int i = 0; i < _shadowPasses2D.Length; i++)
        {
            ShadowPass? pass = _shadowPasses2D[i];
            _shadowPasses2D[i] = null!;
            Graphics3DDisposal.Capture(pass, ref cleanupFailure);
        }

        for (int i = 0; i < _pointShadowPasses.Length; i++)
        {
            PointShadowPass? pass = _pointShadowPasses[i];
            _pointShadowPasses[i] = null!;
            Graphics3DDisposal.Capture(pass, ref cleanupFailure);
        }

        ITextureArray? shadowMapArray = _shadowMapArray;
        ITextureCubeArray? shadowMapCubeArray = _shadowMapCubeArray;
        _shadowMapArray = null;
        _shadowMapCubeArray = null;
        Graphics3DDisposal.Capture(shadowMapArray, ref cleanupFailure);
        Graphics3DDisposal.Capture(shadowMapCubeArray, ref cleanupFailure);

        Graphics3DDisposal.ThrowIfFailed(cleanupFailure);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private void ThrowIfNotInitialized()
    {
        ThrowIfDisposed();
        if (!_initialized)
            throw new InvalidOperationException($"{nameof(ShadowManager)} is not initialized.");
    }
}
