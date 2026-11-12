using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// The compiled-program caches a render family executes with: SkSL runtime effects, SPIR-V filter pipelines and
/// shader-graph pipelines, with the images shader graphs keep between executions.
/// </summary>
/// <remarks>
/// Renderers borrow shared immutable programs and own their image pool. An isolated family owns all its caches.
/// An executor that borrows individual caches disposes only the ones it created.
/// </remarks>
internal sealed class RenderProgramCaches(
    ProgramCache<CachedSkRuntimeEffect> sksl,
    ProgramCache<GLSLFilterPipeline> spirv,
    ProgramCache<ShaderGraphPipeline> shaderGraph,
    ShaderGraphImagePool? shaderGraphImages = null,
    bool ownsPrograms = true) : IDisposable
{
    /// <summary>Every family with owned programs or images, so context teardown can reach what they retain.</summary>
    private static readonly HashSet<RenderProgramCaches> s_owning = new(ReferenceEqualityComparer.Instance);
    private static readonly Lazy<RenderProgramCaches> s_sharedPrograms = new(Create);

    /// <summary>Whether this family owns its programs, rather than borrowing the ones every renderer shares.</summary>
    public bool OwnsPrograms { get; } = ownsPrograms;

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

    public static RenderProgramCaches CreateWithSharedPrograms()
    {
        RenderProgramCaches shared = s_sharedPrograms.Value;
        var caches = new RenderProgramCaches(shared.Sksl, shared.Spirv, shared.ShaderGraph,
            new ShaderGraphImagePool(), ownsPrograms: false);
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
            if (family.OwnsPrograms)
            {
                family.Sksl.EvictAll();
                family.Spirv.EvictAll();
                family.ShaderGraph.EvictAll();
            }
            family.ShaderGraphImages?.Clear();
        }, "One or more program caches failed to release what they retain.");

    /// <summary>Releases this family's state when its renderer changes context.</summary>
    public void EvictContext(object deviceIdentity, object contextIdentity)
    {
        // A shared program may still be in use by another renderer on the previous, live context.
        if (OwnsPrograms)
        {
            Sksl.EvictContext(deviceIdentity, contextIdentity);
            ShaderGraph.EvictContext(deviceIdentity, contextIdentity);
            Spirv.EvictContext(deviceIdentity, contextIdentity);
        }
        // The pool matches images by context instance, not by these identities, so it drops them all.
        ShaderGraphImages?.Clear();
    }

    public void Dispose()
    {
        lock (s_owning)
            s_owning.Remove(this);
        if (OwnsPrograms)
        {
            Sksl.Dispose();
            Spirv.Dispose();
            ShaderGraph.Dispose();
        }
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
