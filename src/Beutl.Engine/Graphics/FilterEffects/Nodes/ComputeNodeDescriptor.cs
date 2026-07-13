using Beutl.Graphics.Backend;

namespace Beutl.Graphics.Effects;

/// <summary>
/// What a <see cref="ComputeNodeDescriptor"/> does on a context without Vulkan compute support
/// (<c>Supports3DRendering == false</c>). The author MUST declare one (contract A7).
/// </summary>
public enum ComputeFallback
{
    /// <summary>Pass the input through unchanged (the pass is a no-op). PixelSort's historic behavior.</summary>
    Identity,

    /// <summary>Drop the pass's output (the content vanishes) — for effects whose absence is preferable to identity.</summary>
    Skip,

    /// <summary>Invoke the declared CPU callback instead of the GPU stages.</summary>
    CpuCallback,
}

/// <summary>
/// The scratch/output resources the executor hands a <see cref="ComputeNodeDescriptor.Dispatch"/> callback
/// (feature 004, T040). The executor owns every buffer: <see cref="Source"/> is the materialized input texture,
/// <see cref="Destination"/> the pass's output texture, and <see cref="AcquireColorScratch"/>/
/// <see cref="AcquireDepthScratch"/> hand out pooled ping-pong and depth textures released when the pass ends —
/// so a K-pass effect drives K dispatches over pooled buffers instead of allocating fresh textures each frame.
/// </summary>
public interface IComputeContext
{
    /// <summary>The materialized input texture (binding-0 source of the first stage).</summary>
    ITexture2D Source { get; }

    /// <summary>The pass's output texture (write the final stage here).</summary>
    ITexture2D Destination { get; }

    /// <summary>The buffer width in device pixels.</summary>
    int Width { get; }

    /// <summary>The buffer height in device pixels.</summary>
    int Height { get; }

    /// <summary>The working density <c>w</c> resolved for this pass (device px per logical unit); scale absolute-px push constants by it.</summary>
    float WorkingScale { get; }

    /// <summary>Acquires a pooled RGBA16F ping-pong scratch texture; released by the executor at pass end.</summary>
    ITexture2D AcquireColorScratch();

    /// <summary>Acquires a depth scratch texture the fixed-function pipeline needs; released by the executor at pass end.</summary>
    ITexture2D AcquireDepthScratch();

    /// <summary>
    /// Blits <see cref="Source"/> into <see cref="Destination"/> unchanged (a GPU image copy, no shader). The
    /// identity a compute pass falls back to when it cannot produce output — e.g. its shaders failed to compile —
    /// so the layer keeps the source instead of the cleared (transparent) destination.
    /// </summary>
    void CopySourceToDestination();

    /// <summary>Runs one single-texture GLSL pass; counts one <see cref="Rendering.PipelineDiagnostics.GpuPasses"/> (C8).</summary>
    void Run<T>(GLSLShader shader, ITexture2D source, ITexture2D destination, ITexture2D depth, T pushConstants)
        where T : unmanaged;

    /// <summary>Runs one dual-texture GLSL pass (source + mask); counts one <see cref="Rendering.PipelineDiagnostics.GpuPasses"/> (C8).</summary>
    void Run<T>(
        GLSLShader shader, ITexture2D source, ITexture2D mask, ITexture2D destination, ITexture2D depth,
        T pushConstants) where T : unmanaged;
}

/// <summary>
/// A Vulkan compute node (feature 004, data-model §1, contract A2, research D7): a fixed set of GLSL fragment
/// passes the executor schedules on the Vulkan backend, feeding them pooled ping-pong/depth textures and driving
/// the per-frame push constants through the author's <see cref="Dispatch"/> callback. Never fused. The
/// <see cref="PassCount"/> is <b>structural</b> (changing it recompiles, C3.6); the push-constant values the
/// callback writes each frame are parameters. On a context without Vulkan the declared <see cref="Fallback"/>
/// applies.
/// </summary>
public sealed record ComputeNodeDescriptor : EffectNodeDescriptor
{
    private ComputeNodeDescriptor(
        Action<IComputeContext> dispatch, int passCount, int colorScratchCount, int depthScratchCount,
        ComputeFallback fallback,
        Action<GeometrySession>? cpuCallback, object structuralToken, bool cpuFallbackRequiresReadback)
    {
        Dispatch = dispatch;
        PassCount = passCount;
        ColorScratchCount = colorScratchCount;
        DepthScratchCount = depthScratchCount;
        Fallback = fallback;
        CpuCallback = cpuCallback;
        StructuralToken = structuralToken;
        CpuFallbackRequiresReadback = cpuFallbackRequiresReadback;
    }

    /// <summary>The callback that runs the compute stages against the executor-provided textures.</summary>
    public Action<IComputeContext> Dispatch { get; }

    /// <summary>Structural number of GPU dispatches this node performs (each counts one <c>GpuPasses</c>, C8).</summary>
    public int PassCount { get; }

    /// <summary>Maximum concurrently acquired RGBA16F scratch textures. Part of the compiled resource plan.</summary>
    public int ColorScratchCount { get; }

    /// <summary>Maximum concurrently acquired depth scratch textures. Part of the compiled resource plan.</summary>
    public int DepthScratchCount { get; }

    /// <summary>What happens on a context without Vulkan compute support.</summary>
    public ComputeFallback Fallback { get; }

    /// <summary>The CPU fallback callback, present iff <see cref="Fallback"/> is <see cref="ComputeFallback.CpuCallback"/>.</summary>
    public Action<GeometrySession>? CpuCallback { get; }

    /// <summary>Identity of the compute <em>kind</em> for the structural key.</summary>
    public object StructuralToken { get; }

    /// <summary>True when the CPU fallback calls <see cref="EffectInput.Snapshot"/>.</summary>
    public bool CpuFallbackRequiresReadback { get; }

    /// <summary>
    /// A compute pass is render-time resolved (A3): its GLSL stages read the whole materialized input at
    /// full-frame device coordinates (fragCoord / width / height push constants), and non-local kernels
    /// (PixelSort's row/column gather, an arbitrary GLSLScriptEffect sampler) are not coordinate-invariant.
    /// A downstream deflating pass must therefore never ROI-crop it to an offset sub-rect — that would
    /// materialize a cropped source and feed truncated width/height, so crop-then-sort ≠ sort-then-crop.
    /// RenderTime keeps the resolver at full input bounds for the pass's ROI.
    /// </summary>
    public override BoundsContract Bounds => BoundsContract.RenderTime;

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant => false;

    /// <summary>
    /// Builds a compute node. <paramref name="passCount"/> is the structural dispatch count; <paramref name="fallback"/>
    /// is mandatory (declare <see cref="ComputeFallback.CpuCallback"/> only with a non-null
    /// <paramref name="cpuCallback"/>). <paramref name="structuralToken"/> defaults to the dispatch method identity.
    /// </summary>
    public static ComputeNodeDescriptor Create(
        Action<IComputeContext> dispatch,
        int passCount,
        ComputeFallback fallback,
        int colorScratchCount = 0,
        int depthScratchCount = 0,
        Action<GeometrySession>? cpuCallback = null,
        object? structuralToken = null,
        bool cpuFallbackRequiresReadback = false)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentOutOfRangeException.ThrowIfLessThan(passCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(colorScratchCount);
        ArgumentOutOfRangeException.ThrowIfNegative(depthScratchCount);
        if (fallback == ComputeFallback.CpuCallback && cpuCallback is null)
        {
            throw new ArgumentNullException(
                nameof(cpuCallback), "ComputeFallback.CpuCallback requires a non-null CPU callback.");
        }

        return new ComputeNodeDescriptor(
            dispatch, passCount, colorScratchCount, depthScratchCount, fallback, cpuCallback,
            structuralToken ?? dispatch.Method.MethodHandle.Value, cpuFallbackRequiresReadback);
    }
}
