using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// The compiled-program caches a render family executes with: SkSL runtime effects, SPIR-V filter pipelines and
/// shader-graph pipelines, with the images shader graphs keep between executions.
/// </summary>
/// <remarks>
/// <see cref="Dispose"/> disposes all of them, so only the owner of every cache calls it; an executor that borrows
/// some of them disposes only the ones it created.
/// </remarks>
internal sealed class RenderProgramCaches(
    ProgramCache<CachedSkRuntimeEffect> sksl,
    ProgramCache<GLSLFilterPipeline> spirv,
    ProgramCache<ShaderGraphPipeline> shaderGraph,
    ShaderGraphImagePool? shaderGraphImages = null) : IDisposable
{
    public ProgramCache<CachedSkRuntimeEffect> Sksl { get; } = sksl;

    public ProgramCache<GLSLFilterPipeline> Spirv { get; } = spirv;

    public ProgramCache<ShaderGraphPipeline> ShaderGraph { get; } = shaderGraph;

    /// <summary>The images shader graphs return for later executions, or null to allocate them every time.</summary>
    public ShaderGraphImagePool? ShaderGraphImages { get; } = shaderGraphImages;

    public ProgramCacheStatistics Statistics
        => Combine(Combine(Sksl.Statistics, Spirv.Statistics), ShaderGraph.Statistics);

    public static RenderProgramCaches Create()
        => new(SkRuntimeEffectProgramCache.Create(), SpirvShaderProgramCache.Create(), ShaderGraphPipeline.CreateCache(),
            new ShaderGraphImagePool());

    /// <summary>Invalidates every program compiled for one device context, and the images allocated on it.</summary>
    public void EvictContext(object deviceIdentity, object contextIdentity)
    {
        Sksl.EvictContext(deviceIdentity, contextIdentity);
        ShaderGraph.EvictContext(deviceIdentity, contextIdentity);
        Spirv.EvictContext(deviceIdentity, contextIdentity);
        // The pool matches images by context instance, not by these identities, so it drops them all.
        ShaderGraphImages?.Clear();
    }

    public void Dispose()
    {
        Sksl.Dispose();
        Spirv.Dispose();
        ShaderGraph.Dispose();
        ShaderGraphImages?.Dispose();
    }

    private static ProgramCacheStatistics Combine(ProgramCacheStatistics first, ProgramCacheStatistics second)
        => new(
            first.Hits + second.Hits,
            first.Misses + second.Misses,
            first.Creations + second.Creations,
            first.Evictions + second.Evictions,
            first.RetainedPrograms + second.RetainedPrograms,
            first.RetainedBytes + second.RetainedBytes);
}
