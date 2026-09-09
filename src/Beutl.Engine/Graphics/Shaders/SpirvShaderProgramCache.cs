using Beutl.Graphics.Backend;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics.Shaders;

internal static class SpirvShaderProgramCache
{
    private const long DefaultRetainedByteBudget = 16 * 1024 * 1024;
    private const string ColorAlphaFormatContract = "linear-premultiplied-rgba16f";
    private static readonly object s_defaultCompileOptions = new();
    private static readonly ConditionalWeakTable<ProgramCache<GLSLFilterPipeline>, FailureCache> s_failures = new();

    private sealed class FailureCache
    {
        public Dictionary<(ShaderProgramIdentity Program, ProgramCacheContextKey Context), (long Until, ExceptionDispatchInfo Error)> Entries { get; } = [];
    }

    public static ProgramCache<GLSLFilterPipeline> Create()
        => new(
            retainedByteSize: static program => program.RetainedByteSize,
            maxRetainedBytes: DefaultRetainedByteBudget);

    public static bool SupportsExecution(IGraphicsContext? context)
        => context is VulkanContext { Supports3DRendering: true }
            or CompositeContext { Supports3DRendering: true };

    public static ProgramCacheContextKey CreateContextKey(RenderCacheDeviceContextIdentity context)
    {
        context.ThrowIfUninitialized(nameof(context));
        return new ProgramCacheContextKey(
            context.DeviceIdentity,
            context.ContextIdentity,
            SkslBackendBudgetResolver.SpirvVulkan.CapabilityClass,
            ColorAlphaFormatContract,
            s_defaultCompileOptions);
    }

    public static ProgramCacheLease<GLSLFilterPipeline> Acquire(
        ProgramCache<GLSLFilterPipeline> cache,
        ShaderDescription description,
        IGraphicsContext graphicsContext,
        ProgramCacheContextKey context)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(graphicsContext);
        ArgumentNullException.ThrowIfNull(context);
        if (!SupportsExecution(graphicsContext))
        {
            throw new ArgumentException(
                "SPIR-V shader execution requires the engine Vulkan recording context.",
                nameof(graphicsContext));
        }
        SpirvShaderLowering lowering = description.SpirvLowering
            ?? throw new ArgumentException("The shader description has no SPIR-V lowering.", nameof(description));
        FailureCache failures = s_failures.GetValue(cache, static _ => new FailureCache());
        var key = (lowering.ProgramIdentity, context);
        lock (failures)
        {
            if (failures.Entries.TryGetValue(key, out var failed))
            {
                if (Environment.TickCount64 < failed.Until)
                    failed.Error.Throw();
                failures.Entries.Remove(key);
            }
        }
        try
        {
            return cache.GetOrCreate(
                lowering.ProgramIdentity,
                context,
                new SpirvProgramCreationState(graphicsContext, lowering),
                static state => GLSLFilterPipeline.Create(
                        state.GraphicsContext,
                        state.Lowering.FragmentShaderSource,
                        ShaderOutputCoverage.ProvablyFull)
                    ?? throw new InvalidOperationException("Failed to compile the SPIR-V shader program."));
        }
        catch (InvalidOperationException ex)
        {
            lock (failures)
            {
                // Bounded, renderer-owned failures throttle frame-by-frame compilation without
                // permanently suppressing recovery after a transient native failure.
                if (failures.Entries.Count >= 128) failures.Entries.Clear();
                failures.Entries[key] = (Environment.TickCount64 + 1000, ExceptionDispatchInfo.Capture(ex));
            }
            throw;
        }
    }

    private readonly record struct SpirvProgramCreationState(
        IGraphicsContext GraphicsContext,
        SpirvShaderLowering Lowering);
}
