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
    /// <summary>Every family from <see cref="Create"/> not yet disposed, so context teardown can reach what they retain.</summary>
    /// <remarks>Nothing enumerates the live renderers, and a borrowing family owns nothing to reach.</remarks>
    private static readonly HashSet<RenderProgramCaches> s_owning = new(ReferenceEqualityComparer.Instance);

    public ProgramCache<CachedSkRuntimeEffect> Sksl { get; } = sksl;

    public ProgramCache<GLSLFilterPipeline> Spirv { get; } = spirv;

    public ProgramCache<ShaderGraphPipeline> ShaderGraph { get; } = shaderGraph;

    /// <summary>The images shader graphs return for later executions, or null to allocate them every time.</summary>
    public ShaderGraphImagePool? ShaderGraphImages { get; } = shaderGraphImages;

    public ProgramCacheStatistics Statistics
        => Combine(Combine(Sksl.Statistics, Spirv.Statistics), ShaderGraph.Statistics);

    public static RenderProgramCaches Create()
    {
        var caches = new RenderProgramCaches(
            SkRuntimeEffectProgramCache.Create(), SpirvShaderProgramCache.Create(), ShaderGraphPipeline.CreateCache(),
            new ShaderGraphImagePool());
        lock (s_owning)
            s_owning.Add(caches);
        return caches;
    }

    /// <summary>
    /// Disposes what every live family retains while the device is still installed, so the releases reach it. A
    /// renderer that outlives the teardown compiles its programs again.
    /// </summary>
    internal static void RetireRetainedBeforeContextTeardown()
        => CleanupFailures.RetireLive(s_owning, static family =>
        {
            family.Sksl.EvictAll();
            family.Spirv.EvictAll();
            family.ShaderGraph.EvictAll();
            family.ShaderGraphImages?.Clear();
        }, "One or more program caches failed to release what they retain.");

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
        lock (s_owning)
            s_owning.Remove(this);
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
