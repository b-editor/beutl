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

/// <summary>How an ordinary exception thrown by the Vulkan dispatch callback is handled.</summary>
public enum ComputeDispatchFailureBehavior
{
    /// <summary>Propagate the failure after releasing all executor-owned resources.</summary>
    Throw,

    /// <summary>
    /// Keep the source operation for an interactive preview, but propagate during delivery. Cancellation,
    /// resource-plan violations, and allocation failures retain their dedicated semantics.
    /// </summary>
    IdentityInPreview,
}

/// <summary>
/// The scratch/output resources the executor hands a <see cref="ComputeNodeDescriptor.Dispatch"/> callback
/// (feature 004, T040). The executor owns every buffer: <see cref="Source"/> is the materialized input texture,
/// <see cref="Destination"/> the pass's output texture, and <see cref="AcquireColorScratch"/> hands out pooled
/// ping-pong textures released when the pass ends —
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

    /// <summary>
    /// Blits <see cref="Source"/> into <see cref="Destination"/> unchanged (a GPU image copy, no shader). The
    /// identity a compute pass falls back to when it cannot produce output — e.g. its shaders failed to compile —
    /// so the layer keeps the source instead of the cleared (transparent) destination. This is an exclusive terminal
    /// operation: it cannot be combined with <see cref="Run{T}(GLSLShader, ITexture2D, ITexture2D, T)"/>
    /// or followed by scratch acquisition.
    /// </summary>
    void CopySourceToDestination();

    /// <summary>Runs one single-texture GLSL pass; counts one <see cref="Rendering.PipelineDiagnostics.GpuPasses"/> (C8).</summary>
    void Run<T>(GLSLShader shader, ITexture2D source, ITexture2D destination, T pushConstants)
        where T : unmanaged;

    /// <summary>Runs one dual-texture GLSL pass (source + mask); counts one <see cref="Rendering.PipelineDiagnostics.GpuPasses"/> (C8).</summary>
    void Run<T>(
        GLSLShader shader, ITexture2D source, ITexture2D mask, ITexture2D destination,
        T pushConstants) where T : unmanaged;
}

/// <summary>
/// A Vulkan compute node (feature 004, data-model §1, contract A2, research D7): a fixed set of GLSL fragment
/// passes the executor schedules on the Vulkan backend, feeding them pooled ping-pong textures and driving
/// the per-frame push constants through the author's <see cref="Dispatch"/> callback. Never fused. The
/// <see cref="PassCount"/> is <b>structural</b> (changing it recompiles, C3.6); the push-constant values the
/// callback writes each frame are parameters. On a context without Vulkan the declared <see cref="Fallback"/>
/// applies.
/// </summary>
public sealed record ComputeNodeDescriptor : EffectNodeDescriptor
{
    internal override EffectNodeKind Kind => EffectNodeKind.Compute;

    private ComputeNodeDescriptor(
        Action<IComputeContext> dispatch, int passCount, int colorScratchCount,
        ComputeFallback fallback,
        Action<GeometrySession>? cpuCallback, object structuralToken, bool cpuFallbackRequiresReadback,
        ComputeDispatchFailureBehavior dispatchFailureBehavior)
    {
        Dispatch = dispatch;
        PassCount = passCount;
        ColorScratchCount = colorScratchCount;
        Fallback = fallback;
        CpuCallback = cpuCallback;
        StructuralToken = structuralToken;
        CpuFallbackRequiresReadback = cpuFallbackRequiresReadback;
        DispatchFailureBehavior = dispatchFailureBehavior;
    }

    /// <summary>The callback that runs the compute stages against the executor-provided textures.</summary>
    public Action<IComputeContext> Dispatch { get; }

    /// <summary>
    /// Exact structural number of successful <see cref="IComputeContext.Run{T}(GLSLShader, ITexture2D, ITexture2D, T)"/>
    /// calls this node performs (each counts one <c>GpuPasses</c>, C8), unless the callback uses the exclusive terminal copy.
    /// </summary>
    public int PassCount { get; }

    /// <summary>Maximum concurrently acquired RGBA16F scratch textures. Part of the compiled resource plan.</summary>
    public int ColorScratchCount { get; }

    /// <summary>What happens on a context without Vulkan compute support.</summary>
    public ComputeFallback Fallback { get; }

    /// <summary>The CPU fallback callback, present iff <see cref="Fallback"/> is <see cref="ComputeFallback.CpuCallback"/>.</summary>
    public Action<GeometrySession>? CpuCallback { get; }

    /// <summary>Identity of the compute <em>kind</em> for the structural key. Tokens share a plan only when their
    /// runtime types and <see cref="object.Equals(object?)"/> values match; equality and hash code must stay stable.</summary>
    public object StructuralToken { get; }

    /// <summary>True when the CPU fallback calls <see cref="EffectInput.Snapshot"/>.</summary>
    public bool CpuFallbackRequiresReadback { get; }

    /// <summary>How ordinary Vulkan dispatch exceptions are handled.</summary>
    public ComputeDispatchFailureBehavior DispatchFailureBehavior { get; }

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
    /// Builds a compute node. <paramref name="passCount"/> is the exact structural successful-dispatch count; the
    /// executor rejects over-dispatch before executing it and under-dispatch after a normal callback return.
    /// <paramref name="fallback"/>
    /// is mandatory (declare <see cref="ComputeFallback.CpuCallback"/> only with a non-null
    /// <paramref name="cpuCallback"/>). <paramref name="dispatchFailureBehavior"/> is independent of that no-Vulkan
    /// fallback and defaults to propagating callback exceptions. <paramref name="structuralToken"/> defaults to the
    /// dispatch method identity.
    /// </summary>
    public static ComputeNodeDescriptor Create(
        Action<IComputeContext> dispatch,
        int passCount,
        ComputeFallback fallback,
        int colorScratchCount = 0,
        Action<GeometrySession>? cpuCallback = null,
        object? structuralToken = null,
        bool cpuFallbackRequiresReadback = false,
        ComputeDispatchFailureBehavior dispatchFailureBehavior = ComputeDispatchFailureBehavior.Throw)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentOutOfRangeException.ThrowIfLessThan(passCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(colorScratchCount);
        if (fallback == ComputeFallback.CpuCallback && cpuCallback is null)
        {
            throw new ArgumentNullException(
                nameof(cpuCallback), "ComputeFallback.CpuCallback requires a non-null CPU callback.");
        }

        return new ComputeNodeDescriptor(
            dispatch, passCount, colorScratchCount, fallback, cpuCallback,
            structuralToken ?? dispatch.Method.MethodHandle.Value, cpuFallbackRequiresReadback, dispatchFailureBehavior);
    }
}
