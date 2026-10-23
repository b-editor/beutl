using System.Runtime.CompilerServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics.Shaders;

internal static class SpirvShaderProgramCache
{
    private const long DefaultRetainedByteBudget = 16 * 1024 * 1024;
    private static readonly object s_defaultCompileOptions = new();
    // The programs that failed in each renderer's cache; they stay on the SkSL path for that renderer.
    private static readonly ConditionalWeakTable<
        ProgramCache<GLSLFilterPipeline>,
        HashSet<(ShaderProgramIdentity Program, ProgramCacheContextKey Context)>> s_failures = new();

    public static ProgramCache<GLSLFilterPipeline> Create()
        => new(
            retainedByteSize: static program => program.RetainedByteSize,
            maxRetainedBytes: DefaultRetainedByteBudget);

    public static bool SupportsExecution(IGraphicsContext? context)
        => GLSLFilterPipeline.TryResolveVulkan(context) is not null;

    public static ProgramCacheContextKey CreateContextKey(RenderCacheDeviceContextIdentity context)
    {
        context.ThrowIfUninitialized(nameof(context));
        return new ProgramCacheContextKey(
            context.DeviceIdentity,
            context.ContextIdentity,
            SkslBackendBudgetResolver.SpirvVulkan.CapabilityClass,
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
        HashSet<(ShaderProgramIdentity, ProgramCacheContextKey)> failures = s_failures.GetValue(cache, static _ => []);
        var key = (lowering.ProgramIdentity, context);
        lock (failures)
        {
            if (failures.Contains(key))
                throw new InvalidOperationException("The SPIR-V shader program failed earlier in this renderer.");
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
        catch (InvalidOperationException)
        {
            lock (failures)
                failures.Add(key);
            throw;
        }
    }

    private readonly record struct SpirvProgramCreationState(
        IGraphicsContext GraphicsContext,
        SpirvShaderLowering Lowering);
}
